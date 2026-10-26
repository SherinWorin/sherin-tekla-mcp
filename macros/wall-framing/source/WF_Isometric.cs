// WF_Isometric.cs - the machinery of the ISOMETRIC VIEW macro (Tekla Structures 2025).
//
// It is compiled into WF_Isometric.dll, which the thin loader P142_WF_Isometric
// calls.  The title rules and the colour code live in WF_IsometricRules.cs and
// the block rules in WF_Rules.cs; neither references Tekla.
//
// HOW IT WORKS - the mode is chosen by the drawing that is open:
//
//   CAPTURE  the open isometric drawing has 3D views that this macro did not
//            make (e.g. the reference BP02M-80001).  READ ONLY: every view's
//            view plane, display plane, scale, sheet position, name and
//            depth cut is recorded in %LOCALAPPDATA%\P142\WallFraming\
//            WF_Isometric_Reference_v2.txt, together with the mark and name
//            of the drawing.  Nothing is written into the drawing.
//
//   CREATE   the open isometric drawing has no 3D view.  The reference views
//            are re-created, each showing only the parts of the drawing's own
//            block (block = drawing name before the first dash).  For each
//            view: the reference view's PROPERTIES are copied live from the
//            reference drawing (read through GetSheet(), HANDOVER trap 7),
//            the reference's depth cut is applied as the same fraction of
//            the block (e.g. ISOMETRIC VIEW 3 cut through the walls), the
//            parts are coloured with the owner's colour code, and every part
//            mark is removed.  If no reference was captured, one is found:
//            another isometric drawing whose 3D views lie on its sheet.
//
//   REBUILD / REPAIR  the drawing already holds views made by this macro.
//            The macro asks: YES deletes those views (only the ones it made,
//            tagged P142_WF_ISO) and creates them again; NO only moves views
//            that lie off the sheet back onto it.
//
// TEKLA 2025 API (checked in the 2025.0 XML docs and DLL metadata):
//   * View.Create3dView throws InvalidTypeException on a GA drawing - not used.
//   * new View(ContainerView, CS viewCS, CS displayCS, ArrayList partIds) + Insert().
//   * View.Attributes, RestrictionBox, Name and ViewBase.Origin are settable.
//   * Part.PartAttributes: Representation, SymbolOffset, Draw*Lines, *Lines
//     (LineTypeAttributes), FaceHatch / SectionFaceHatch (ModelObjectHatchAttributes).
//   * LineTypes is a CLASS of static members (not an enum) - Enum.Parse on it fails.
//   * DrawingColors: NewLine3=156, NewLine4=157, NewLine5=158, NewLine6=159.
//
// RISK: MEDIUM (inserts and colours views in the open drawing; REBUILD deletes
// only views tagged by this macro, after a Yes).  Never touches the model.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DPart = Tekla.Structures.Drawing.Part;

namespace P142.WallFraming
{
    /// <summary>Outcome of a macro run.</summary>
    public sealed class WfIsoResult
    {
        /// <summary>True when the macro did everything it was asked to do.</summary>
        public bool Ok;
        /// <summary>Human-readable summary of the run.</summary>
        public string Message = "";
        /// <summary>Number of views created or moved.</summary>
        public int Made;
        /// <summary>Number of views that failed.</summary>
        public int Failed;
        /// <summary>The full log of the run.</summary>
        public string Log = "";
    }

    /// <summary>One 3D view as captured from a reference drawing.</summary>
    public sealed class WfIsoViewSpec
    {
        /// <summary>The view name in Tekla.</summary>
        public string Name = "";
        /// <summary>The view type as text, for the log only.</summary>
        public string Type = "";
        /// <summary>The scale denominator (1:20 -> 20).</summary>
        public double Scale;
        /// <summary>The view origin on the sheet (paper mm).</summary>
        public double OriginX, OriginY;
        /// <summary>The view plane (origin, X axis, Y axis).</summary>
        public double[] Vcs = new double[9];
        /// <summary>The display plane (origin, X axis, Y axis).</summary>
        public double[] Dcs = new double[9];
        /// <summary>The restriction box in view coordinates (min xyz, max xyz).</summary>
        public double[] Box = new double[6];
        /// <summary>
        /// The restriction box as fractions of the block's extent in view
        /// coordinates (min xyz, max xyz).  0,0,0,1,1,1 = the whole block.
        /// </summary>
        public double[] Cut = { 0, 0, 0, 1, 1, 1 };
        /// <summary>Centre of the view FRAME on the reference sheet (paper mm); NaN when unknown.</summary>
        public double FrameCX = double.NaN, FrameCY = double.NaN;

        /// <summary>True when the reference view cuts the block on some side.</summary>
        public bool HasCut
        {
            get
            {
                for (int i = 0; i < 3; i++)
                    if (Cut[i] > 0.02 || Cut[i + 3] < 0.98) return true;
                return false;
            }
        }
    }

    /// <summary>The machinery of the ISOMETRIC VIEW macro.</summary>
    public static class WfIsometricCore
    {
        // ------------------------------------------------------------- config
        /// <summary>User property written on every view the macro creates.</summary>
        public const string TAG_PROP = "P142_WF_ISO";
        /// <summary>Value of the user property.</summary>
        public const string TAG_VALUE = "1";
        /// <summary>File name of the captured reference, in the module folder.</summary>
        public const string REFERENCE_FILE = "WF_Isometric_Reference_v2.txt";
        /// <summary>Margin added on an uncut side of a cut view's box (mm).</summary>
        const double BOX_MARGIN = 50.0;

        /// <summary>
        /// Reference drawings to prefer, in order, when the macro has to find one
        /// by itself (the owner's choice, 2026-10-24).
        /// </summary>
        static readonly string[] PREFERRED_REFERENCES = { "BP02M-80001", "BP01-80001" };

        /// <summary>
        /// The standard three-pane layout of the project's isometric sheets, as
        /// fractions of the sheet (centre of each view frame), measured on the
        /// reference sheets (A1): top-left, top-right, bottom.  Used when a
        /// captured position would put a view off the sheet.
        /// </summary>
        static readonly double[,] SLOTS = { { 0.228, 0.741 }, { 0.657, 0.728 }, { 0.463, 0.230 } };

        /// <summary>
        /// The pane of each view, by its role (the owner's layout, 2026-10-24):
        /// ISOMETRIC VIEW 1 top-left, ISOMETRIC VIEW 2 top-right, ISOMETRIC VIEW 3
        /// (the cut view) bottom centre.  The cut view is view 3; of the two
        /// whole-block views, the one whose display X axis points to +X is view 1
        /// (0.766, -0.643 on BP01-80001 [115] and BP02M-80001 [118]), the other
        /// view 2 (-0.788, 0.616).  The reference's own sheet positions are NOT
        /// used: Tekla does not report the frame of a view in a drawing that is
        /// not open (it reads back as 0).
        /// Returns the pane index (0, 1, 2) for every spec.
        /// </summary>
        public static int[] PanesOf(List<WfIsoViewSpec> specs)
        {
            var pane = new int[specs.Count];
            var whole = new List<int>();
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].HasCut) pane[i] = 2;
                else whole.Add(i);
            }
            // view 1 = display X axis toward +X; ties keep the reference order
            whole.Sort(delegate (int a, int b)
            {
                int c = -specs[a].Dcs[3].CompareTo(specs[b].Dcs[3]);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int k = 0; k < whole.Count; k++) pane[whole[k]] = k < 2 ? k : 2;
            return pane;
        }

        static WfIsometricConfig _config;
        /// <summary>The title rules (from WF_IsometricRules).</summary>
        public static WfIsometricConfig Config
        {
            get
            {
                if (_config == null) _config = WfIsometricRules.GetDefaultConfig();
                return _config;
            }
        }

        // ------------------------------------------------------------- state
        /// <summary>The log of the run.</summary>
        public static readonly StringBuilder Log = new StringBuilder();
        /// <summary>Views created or moved in this run.</summary>
        public static int Made;
        /// <summary>Views that failed in this run.</summary>
        public static int Failed;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Writes a line into the log of the run.</summary>
        public static void Say(string s) { Log.AppendLine(s); }

        static string F1(double d) { return d.ToString("F1", Inv); }

        // ------------------------------------------------------------- paths
        /// <summary>The captured reference file (%LOCALAPPDATA%\P142\WallFraming).</summary>
        public static string ReferencePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "P142", "WallFraming");
            return Path.Combine(dir, REFERENCE_FILE);
        }

        // ------------------------------------------------------------- drawing tests
        /// <summary>True when a title or the name holds an isometric title fragment.</summary>
        public static bool IsIsometricViewDrawing(Drawing d)
        {
            if (d == null) return false;
            string[] texts = { d.Title1, d.Title2, d.Title3, d.Name };
            foreach (string t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (string f in Config.IsometricTitles)
                    if (t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>True when the drawing must not be written to: frozen or issued.</summary>
        public static bool IsProtected(Drawing d)
        {
            if (d == null) return true;
            return d.IsFrozen || d.IsIssued || d.IsIssuedButModified;
        }

        /// <summary>The 3D / model views of the drawing (the sheet itself is skipped).</summary>
        static List<View> ModelViewsOf(Drawing d)
        {
            var res = new List<View>();
            DrawingObjectEnumerator ve = d.GetSheet().GetViews();
            while (ve.MoveNext())
            {
                View v = ve.Current as View;
                if (v == null || v.IsSheet) continue;
                if (v.ViewType == View.ViewTypes.ModelView || v.ViewType == View.ViewTypes._3DView)
                    res.Add(v);
            }
            return res;
        }

        /// <summary>True when the view carries the tag of this macro.</summary>
        static bool IsOurs(View v)
        {
            string val = "";
            try { v.GetUserProperty(TAG_PROP, ref val); } catch (Exception) { }
            return val == TAG_VALUE;
        }

        /// <summary>The block of the drawing: the name before the first dash, else Title1.</summary>
        public static string BlockOfDrawing(Drawing d)
        {
            foreach (string s in new[] { d.Name, d.Title1 })
            {
                if (string.IsNullOrEmpty(s)) continue;
                string t = s.Trim();
                int i = t.IndexOf('-');
                string b = i > 0 ? t.Substring(0, i) : t;
                if (WfRules.NormaliseBlock(b).Length > 0) return b;
            }
            return "";
        }

        // ------------------------------------------------------------- sheet helpers
        static void SheetSize(Drawing d, out double w, out double h)
        {
            ContainerView sheet = d.GetSheet();
            w = sheet.Width;
            h = sheet.Height;
        }

        static bool OnSheet(double x, double y, double w, double h)
        {
            return w > 0 && h > 0 && x >= 0 && y >= 0 && x <= w && y <= h;
        }

        /// <summary>Re-reads the view from Tekla so Origin / FrameOrigin / Width / Height are current.</summary>
        static void Refresh(View v)
        {
            try { v.Select(); } catch (Exception) { }
        }

        /// <summary>The centre of the view frame on the sheet.</summary>
        static void FrameCentre(View v, out double cx, out double cy)
        {
            Vector fo = v.FrameOrigin;
            cx = v.Origin.X + (fo == null ? 0 : fo.X) + v.Width / 2.0;
            cy = v.Origin.Y + (fo == null ? 0 : fo.Y) + v.Height / 2.0;
        }

        static bool FrameOnSheet(View v, double w, double h)
        {
            Vector fo = v.FrameOrigin;
            double x0 = v.Origin.X + (fo == null ? 0 : fo.X);
            double y0 = v.Origin.Y + (fo == null ? 0 : fo.Y);
            return OnSheet(x0, y0, w, h) && OnSheet(x0 + v.Width, y0 + v.Height, w, h);
        }

        /// <summary>
        /// Moves the view so that its frame centre lands on (cx, cy).  The offset
        /// from the view origin to the frame centre is measured ONCE (after a
        /// fresh Select) and the origin is set in one move: Tekla does not always
        /// refresh the frame right after Modify(), and correcting again from
        /// stale numbers moved a view twice as far (ISOMETRIC VIEW 2, 2026-10-24).
        /// </summary>
        static string CentreOn(View v, double cx, double cy)
        {
            Refresh(v);
            Vector fo = v.FrameOrigin;
            double offX = (fo == null ? 0 : fo.X) + v.Width / 2.0;
            double offY = (fo == null ? 0 : fo.Y) + v.Height / 2.0;
            string before = "origin (" + F1(v.Origin.X) + ", " + F1(v.Origin.Y) + "), frame offset (" +
                            F1(offX) + ", " + F1(offY) + "), frame " + F1(v.Width) + " x " + F1(v.Height);
            v.Origin = new Point(cx - offX, cy - offY, 0);
            v.Modify();
            return before + " -> origin (" + F1(cx - offX) + ", " + F1(cy - offY) + ")";
        }

        /// <summary>
        /// Puts the view's frame centre on the wanted point (paper mm); if no
        /// usable point is given, or the frame would stick out of the sheet,
        /// centres it on layout slot <paramref name="slot"/> instead.
        /// </summary>
        static string Place(View v, double wantCX, double wantCY, int slot, double w, double h)
        {
            if (!double.IsNaN(wantCX) && !double.IsNaN(wantCY) && OnSheet(wantCX, wantCY, w, h))
            {
                CentreOn(v, wantCX, wantCY);
                Refresh(v);
                if (FrameOnSheet(v, w, h))
                    return "frame centred at (" + F1(wantCX) + ", " + F1(wantCY) + ") as on the reference";
            }
            int k = slot % SLOTS.GetLength(0);
            double cx = SLOTS[k, 0] * w, cy = SLOTS[k, 1] * h;
            string how = CentreOn(v, cx, cy);
            return "pane " + (k + 1) + " centre (" + F1(cx) + ", " + F1(cy) + ") [" + how + "]";
        }

        static int CountModelObjects(View v)
        {
            int n = 0;
            try { var en = v.GetModelObjects(); while (en.MoveNext()) n++; }
            catch (Exception) { n = -1; }
            return n;
        }

        // ------------------------------------------------------------- numbers <-> text
        static double[] Cs(CoordinateSystem c)
        {
            return new[] { c.Origin.X, c.Origin.Y, c.Origin.Z,
                           c.AxisX.X, c.AxisX.Y, c.AxisX.Z,
                           c.AxisY.X, c.AxisY.Y, c.AxisY.Z };
        }

        static CoordinateSystem Cs(double[] a)
        {
            return new CoordinateSystem(new Point(a[0], a[1], a[2]),
                                        new Vector(a[3], a[4], a[5]),
                                        new Vector(a[6], a[7], a[8]));
        }

        static string Nums(double[] a)
        {
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString("R", Inv);
            return string.Join(";", parts);
        }

        static double[] ParseNums(string s, int n)
        {
            string[] p = s.Split(';');
            if (p.Length != n) throw new FormatException("expected " + n + " numbers, got " + p.Length);
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = double.Parse(p[i], Inv);
            return a;
        }

        // ------------------------------------------------------------- model read
        /// <summary>The parts whose assembly belongs to the block (read in the global plane).</summary>
        static List<TSM.Part> BlockParts(TSM.Model model, string block, out int scanned)
        {
            scanned = 0;
            var res = new List<TSM.Part>();
            var posOfAssembly = new Dictionary<int, string>();
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane());
                TSM.ModelObjectSelector sel = model.GetModelObjectSelector();
                foreach (TSM.ModelObject.ModelObjectEnum kind in new[] {
                    TSM.ModelObject.ModelObjectEnum.BEAM,
                    TSM.ModelObject.ModelObjectEnum.POLYBEAM,
                    TSM.ModelObject.ModelObjectEnum.CONTOURPLATE })
                {
                    TSM.ModelObjectEnumerator en = sel.GetAllObjectsWithType(kind);
                    while (en.MoveNext())
                    {
                        TSM.Part p = en.Current as TSM.Part;
                        if (p == null) continue;
                        scanned++;
                        string apos = "";
                        TSM.Assembly asm = null;
                        try { asm = p.GetAssembly(); } catch (Exception) { }
                        if (asm != null)
                        {
                            int key = asm.Identifier.ID;
                            if (!posOfAssembly.TryGetValue(key, out apos))
                            {
                                apos = "";
                                asm.GetReportProperty("ASSEMBLY_POS", ref apos);
                                posOfAssembly[key] = apos ?? "";
                            }
                        }
                        if (string.IsNullOrEmpty(apos)) p.GetReportProperty("ASSEMBLY_POS", ref apos);
                        if (string.IsNullOrEmpty(apos)) continue;
                        int dash = apos.IndexOf('-');
                        string partBlock = dash > 0 ? apos.Substring(0, dash) : apos;
                        if (WfRules.SameBlock(partBlock, block)) res.Add(p);
                    }
                }
            }
            finally
            {
                wph.SetCurrentTransformationPlane(original);
            }
            return res;
        }

        /// <summary>
        /// The bounding box of the parts in the given coordinate system
        /// (min xyz, max xyz), or null when no part has a solid.
        /// The work plane is restored in finally.
        /// </summary>
        static double[] ExtentsIn(TSM.Model model, List<TSM.Part> parts, CoordinateSystem cs)
        {
            double[] e = { double.MaxValue, double.MaxValue, double.MaxValue,
                           double.MinValue, double.MinValue, double.MinValue };
            bool any = false;
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(cs));
                foreach (TSM.Part p in parts)
                {
                    TSM.Solid so = null;
                    try { so = p.GetSolid(); } catch (Exception) { }
                    if (so == null) continue;
                    Point a = so.MinimumPoint, b = so.MaximumPoint;
                    e[0] = Math.Min(e[0], Math.Min(a.X, b.X)); e[3] = Math.Max(e[3], Math.Max(a.X, b.X));
                    e[1] = Math.Min(e[1], Math.Min(a.Y, b.Y)); e[4] = Math.Max(e[4], Math.Max(a.Y, b.Y));
                    e[2] = Math.Min(e[2], Math.Min(a.Z, b.Z)); e[5] = Math.Max(e[5], Math.Max(a.Z, b.Z));
                    any = true;
                }
            }
            finally
            {
                wph.SetCurrentTransformationPlane(original);
            }
            return any ? e : null;
        }

        // ------------------------------------------------------------- capture
        /// <summary>
        /// Reads the 3D views of the reference drawing (read only), including the
        /// depth cut of each view as a fraction of the reference block.
        /// </summary>
        static List<WfIsoViewSpec> Capture(Drawing src, List<View> views, TSM.Model model)
        {
            var specs = new List<WfIsoViewSpec>();
            string block = BlockOfDrawing(src);
            List<TSM.Part> parts = null;
            if (block.Length > 0 && model != null)
            {
                int scanned;
                parts = BlockParts(model, block, out scanned);
                Say("reference block " + block + ": " + parts.Count + " part(s)");
            }
            foreach (View v in views)
            {
                var s = new WfIsoViewSpec();
                s.Name = v.Name ?? "";
                s.Type = v.ViewType.ToString();
                s.Scale = v.Attributes.Scale;
                s.OriginX = v.Origin.X;
                s.OriginY = v.Origin.Y;
                s.Vcs = Cs(v.ViewCoordinateSystem);
                s.Dcs = Cs(v.DisplayCoordinateSystem);
                FrameCentre(v, out s.FrameCX, out s.FrameCY);
                AABB b = v.RestrictionBox;
                if (b != null)
                    s.Box = new[] { b.MinPoint.X, b.MinPoint.Y, b.MinPoint.Z,
                                    b.MaxPoint.X, b.MaxPoint.Y, b.MaxPoint.Z };
                if (b != null && parts != null && parts.Count > 0)
                {
                    double[] e = ExtentsIn(model, parts, v.ViewCoordinateSystem);
                    if (e != null)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            double size = e[i + 3] - e[i];
                            if (size < 1) continue;
                            s.Cut[i] = Clamp01((s.Box[i] - e[i]) / size);
                            s.Cut[i + 3] = Clamp01((s.Box[i + 3] - e[i]) / size);
                        }
                    }
                }
                Say("captured view '" + s.Name + "' scale 1:" + s.Scale.ToString("0.##", Inv) +
                    " at (" + F1(s.OriginX) + ", " + F1(s.OriginY) + ")" +
                    (s.HasCut ? ", depth cut " + CutText(s.Cut) : ", whole block"));
                specs.Add(s);
            }
            return specs;
        }

        static double Clamp01(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }

        static string CutText(double[] c)
        {
            return "x " + c[0].ToString("0.00", Inv) + "-" + c[3].ToString("0.00", Inv) +
                   ", y " + c[1].ToString("0.00", Inv) + "-" + c[4].ToString("0.00", Inv) +
                   ", z " + c[2].ToString("0.00", Inv) + "-" + c[5].ToString("0.00", Inv);
        }

        /// <summary>Writes the captured specs to the reference file.</summary>
        static void SaveReference(string path, Drawing source, List<WfIsoViewSpec> specs)
        {
            double sw, sh;
            SheetSize(source, out sw, out sh);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine("# P142 isometric reference - written by the ISOMETRIC VIEW macro, CAPTURE mode.");
            sb.AppendLine("# source drawing: " + source.Mark + "  " + source.Name + "  captured "
                          + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv));
            sb.AppendLine("# view|name|type|scale|originX|originY|viewCS(9)|displayCS(9)|box(6)  then  cut|index|fractions(6)");
            sb.AppendLine("# Optional: add a line  attributes|<view attribute file name>  to create the views with that file.");
            sb.AppendLine("source|" + (source.Mark ?? "").Replace("|", "/") + "|" + (source.Name ?? "").Replace("|", "/"));
            sb.AppendLine("sheet|" + sw.ToString("R", Inv) + "|" + sh.ToString("R", Inv));
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                sb.AppendLine(string.Join("|", new[] {
                    "view", s.Name.Replace("|", "/"), s.Type,
                    s.Scale.ToString("R", Inv), s.OriginX.ToString("R", Inv), s.OriginY.ToString("R", Inv),
                    Nums(s.Vcs), Nums(s.Dcs), Nums(s.Box) }));
                sb.AppendLine("cut|" + i.ToString(Inv) + "|" + Nums(s.Cut));
                sb.AppendLine("frame|" + i.ToString(Inv) + "|" + s.FrameCX.ToString("R", Inv) + "|" + s.FrameCY.ToString("R", Inv));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>The contents of a reference file.</summary>
        public sealed class WfIsoReference
        {
            /// <summary>The views.</summary>
            public List<WfIsoViewSpec> Specs = new List<WfIsoViewSpec>();
            /// <summary>Optional view attribute file.</summary>
            public string AttributesFile = "";
            /// <summary>Mark of the reference drawing.</summary>
            public string SourceMark = "";
            /// <summary>Name of the reference drawing.</summary>
            public string SourceName = "";
            /// <summary>Sheet size of the reference drawing (0 when unknown).</summary>
            public double SheetW, SheetH;
            /// <summary>True when the file records the frame centres (current format).</summary>
            public bool HasFrames;
        }

        /// <summary>Reads a reference file.</summary>
        public static WfIsoReference LoadReference(string path)
        {
            var r = new WfIsoReference();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] f = line.Split('|');
                switch (f[0])
                {
                    case "attributes":
                        if (f.Length >= 2) r.AttributesFile = f[1].Trim();
                        break;
                    case "source":
                        if (f.Length >= 3) { r.SourceMark = f[1]; r.SourceName = f[2]; }
                        break;
                    case "sheet":
                        if (f.Length >= 3) { r.SheetW = double.Parse(f[1], Inv); r.SheetH = double.Parse(f[2], Inv); }
                        break;
                    case "frame":
                    {
                        if (f.Length != 4) throw new FormatException("bad line: " + line);
                        int fi = int.Parse(f[1], Inv);
                        if (fi < 0 || fi >= r.Specs.Count) throw new FormatException("frame line for a missing view: " + line);
                        r.Specs[fi].FrameCX = double.Parse(f[2], Inv);
                        r.Specs[fi].FrameCY = double.Parse(f[3], Inv);
                        r.HasFrames = true;
                        break;
                    }
                    case "cut":
                    {
                        if (f.Length != 3) throw new FormatException("bad line: " + line);
                        int i = int.Parse(f[1], Inv);
                        if (i < 0 || i >= r.Specs.Count) throw new FormatException("cut line for a missing view: " + line);
                        r.Specs[i].Cut = ParseNums(f[2], 6);
                        break;
                    }
                    case "view":
                    {
                        if (f.Length != 9) throw new FormatException("bad line: " + line);
                        var s = new WfIsoViewSpec();
                        s.Name = f[1];
                        s.Type = f[2];
                        s.Scale = double.Parse(f[3], Inv);
                        s.OriginX = double.Parse(f[4], Inv);
                        s.OriginY = double.Parse(f[5], Inv);
                        s.Vcs = ParseNums(f[6], 9);
                        s.Dcs = ParseNums(f[7], 9);
                        s.Box = ParseNums(f[8], 6);
                        r.Specs.Add(s);
                        break;
                    }
                    default:
                        throw new FormatException("bad line: " + line);
                }
            }
            return r;
        }

        // ------------------------------------------------------------- reference lookup
        /// <summary>
        /// Another ISOMETRIC VIEW drawing of the model whose 3D views were not
        /// made by this macro.  Preferred: views on its own sheet, three views,
        /// issued/frozen, then the lowest name.  Read only.
        /// </summary>
        static Drawing FindReferenceDrawing(Drawing target, out List<View> refViews)
        {
            refViews = null;
            Drawing best = null;
            List<View> bestViews = null;
            bool bestFits = false;
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            while (de.MoveNext())
            {
                Drawing d = de.Current;
                if (d == null || d.Mark == target.Mark) continue;
                if (!IsIsometricViewDrawing(d)) continue;
                List<View> views;
                try { views = ModelViewsOf(d); }
                catch (Exception e) { Say("  could not read " + d.Mark + ": " + e.Message); continue; }
                if (views.Count == 0) continue;
                bool ours = false;
                foreach (View v in views) if (IsOurs(v)) { ours = true; break; }
                if (ours) continue;
                double sw, sh;
                SheetSize(d, out sw, out sh);
                bool fits = true;
                foreach (View v in views) if (!FrameOnSheet(v, sw, sh)) { fits = false; break; }
                if (best == null || Better(d, views.Count, fits, best, bestViews.Count, bestFits))
                {
                    best = d;
                    bestViews = views;
                    bestFits = fits;
                }
            }
            refViews = bestViews;
            return best;
        }

        static int Preference(Drawing d)
        {
            for (int i = 0; i < PREFERRED_REFERENCES.Length; i++)
                if (string.Equals((d.Name ?? "").Trim(), PREFERRED_REFERENCES[i], StringComparison.OrdinalIgnoreCase)) return i;
            return PREFERRED_REFERENCES.Length;
        }

        static bool Better(Drawing a, int aViews, bool aFits, Drawing b, int bViews, bool bFits)
        {
            if (aFits != bFits) return aFits;
            int qa = Preference(a), qb = Preference(b);
            if (qa != qb) return qa < qb;
            if ((aViews == 3) != (bViews == 3)) return aViews == 3;
            bool pa = IsProtected(a), pb = IsProtected(b);
            if (pa != pb) return pa;
            return string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>The reference drawing named in the file, read live (or null).</summary>
        static List<View> LiveReferenceViews(WfIsoReference r)
        {
            if (string.IsNullOrEmpty(r.SourceMark)) return null;
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            while (de.MoveNext())
            {
                Drawing d = de.Current;
                if (d == null || d.Mark != r.SourceMark) continue;
                if (!string.IsNullOrEmpty(r.SourceName) && d.Name != r.SourceName) continue;
                try
                {
                    var views = new List<View>();
                    foreach (View v in ModelViewsOf(d)) if (!IsOurs(v)) views.Add(v);
                    return views.Count == r.Specs.Count ? views : null;
                }
                catch (Exception e) { Say("  the reference " + d.Mark + " could not be read: " + e.Message); return null; }
            }
            return null;
        }

        // ------------------------------------------------------------- colour + marks
        /// <summary>
        /// Applies the owner's colour code (WfIsoColours) to the parts of the view.
        /// Returns the number of parts coloured.
        /// </summary>
        static int ColourParts(View v, TSM.Model model, Dictionary<string, int> perRule)
        {
            int n = 0;
            DrawingObjectEnumerator en = v.GetAllObjects(new[] { typeof(DPart) });
            while (en.MoveNext())
            {
                DPart dp = en.Current as DPart;
                if (dp == null) continue;
                TSM.Part mp = model.SelectModelObject(dp.ModelIdentifier) as TSM.Part;
                if (mp == null) continue;
                string profile = "";
                mp.GetReportProperty("PROFILE", ref profile);
                string profile2 = mp.Profile != null ? (mp.Profile.ProfileString ?? "") : "";
                WfIsoColourRule r = WfIsoColours.Match(mp.Name ?? "", profile, profile2);
                if (r == null) continue;
                Apply(dp.Attributes, r);
                if (dp.Modify())
                {
                    n++;
                    int c;
                    perRule.TryGetValue(r.Label, out c);
                    perRule[r.Label] = c + 1;
                }
            }
            return n;
        }

        static void Apply(DPart.PartAttributes a, WfIsoColourRule r)
        {
            DrawingColors c = (DrawingColors)r.Color;
            DrawingColors rc = r.RefColor < 0 ? c : (DrawingColors)r.RefColor;
            a.Representation = DPart.Representation.Outline;
            a.SymbolOffset = r.SymbolOffset;
            a.DrawHiddenLines = r.Hidden;
            a.DrawOwnHiddenLines = r.OwnHidden;
            a.DrawReferenceLine = false;
            a.DrawCenterLine = false;
            a.DrawConnectingSideMarks = false;
            a.DrawOrientationMark = false;
            a.DrawChamfers = false;
            a.DrawPopMarks = false;
            a.VisibleLines = new LineTypeAttributes(LineTypes.SolidLine, c);
            a.SectionLines = new LineTypeAttributes(LineTypes.SolidLine, c);
            a.HiddenLines = new LineTypeAttributes(LineTypes.DashedLine, c);
            a.OwnHiddenLines = new LineTypeAttributes(LineTypes.DashedLine, c);
            a.CenterLine = new LineTypeAttributes(LineTypes.DashDot, c);
            a.ReferenceLine = new LineTypeAttributes(LineTypes.DashDot, rc);
            NoFill(a.FaceHatch);
            NoFill(a.SectionFaceHatch);
        }

        static void NoFill(ModelObjectHatchAttributes h)
        {
            if (h == null) return;
            h.Name = "None";
            h.DrawBackgroundColor = false;
        }

        /// <summary>Deletes every mark in the view (no part marks on isometric views).</summary>
        static int RemoveMarks(View v)
        {
            var marks = new List<DrawingObject>();
            DrawingObjectEnumerator en = v.GetAllObjects(new[] { typeof(Mark) });
            while (en.MoveNext()) if (en.Current != null) marks.Add(en.Current);
            int n = 0;
            foreach (DrawingObject m in marks) if (m.Delete()) n++;
            return n;
        }

        // ------------------------------------------------------------- create
        static void CreateViews(Drawing dr, TSM.Model model, WfIsoReference r, List<View> live,
                                List<TSM.Part> parts)
        {
            ContainerView sheet = dr.GetSheet();
            double w, h;
            SheetSize(dr, out w, out h);
            Say("target sheet " + F1(w) + " x " + F1(h) + (r.SheetW > 0 ? ", reference sheet " + F1(r.SheetW) + " x " + F1(r.SheetH) : ""));
            Say(live != null ? "view properties: copied live from the reference " + r.SourceMark + " " + r.SourceName
                             : "view properties: reference drawing not readable - scale only" +
                               (r.AttributesFile.Length > 0 ? ", attributes file '" + r.AttributesFile + "'" : ""));

            var ids = new ArrayList();
            foreach (TSM.Part p in parts) ids.Add(p.Identifier);
            int[] panes = PanesOf(r.Specs);
            var made = new List<KeyValuePair<View, int>>();   // view, pane - placed after all are made

            for (int i = 0; i < r.Specs.Count; i++)
            {
                WfIsoViewSpec s = r.Specs[i];
                string label = "view '" + s.Name + "'";
                try
                {
                    View v = string.IsNullOrEmpty(r.AttributesFile)
                        ? new View(sheet, Cs(s.Vcs), Cs(s.Dcs), ids)
                        : new View(sheet, Cs(s.Vcs), Cs(s.Dcs), ids, r.AttributesFile);
                    if (!v.Insert())
                    {
                        Failed++;
                        Say("FAIL " + label + " : Insert() returned false");
                        continue;
                    }
                    v.SetUserProperty(TAG_PROP, TAG_VALUE);

                    // the properties of the reference view
                    if (live != null && i < live.Count)
                    {
                        try { v.Attributes = live[i].Attributes; }
                        catch (Exception e) { Say("  " + label + ": properties not copied - " + e.Message); }
                    }
                    if (s.Scale > 0) v.Attributes.Scale = s.Scale;
                    if (!string.IsNullOrEmpty(s.Name)) v.Name = s.Name;
                    v.Modify();

                    // the depth cut of the reference, as the same fraction of this block
                    string cutNote = "whole block";
                    if (s.HasCut)
                    {
                        double[] e = ExtentsIn(model, parts, Cs(s.Vcs));
                        if (e != null)
                        {
                            double[] lo = new double[3], hi = new double[3];
                            for (int k = 0; k < 3; k++)
                            {
                                double size = e[k + 3] - e[k];
                                lo[k] = s.Cut[k] > 0.02 ? e[k] + s.Cut[k] * size : e[k] - BOX_MARGIN;
                                hi[k] = s.Cut[k + 3] < 0.98 ? e[k] + s.Cut[k + 3] * size : e[k + 3] + BOX_MARGIN;
                            }
                            v.RestrictionBox = new AABB(new Point(lo[0], lo[1], lo[2]), new Point(hi[0], hi[1], hi[2]));
                            v.Modify();
                            cutNote = "depth cut " + CutText(s.Cut);
                        }
                        else cutNote = "depth cut NOT applied (no part solids)";
                    }

                    // the position on the sheet
                    string where = "ISOMETRIC VIEW " + (panes[i] + 1);
                    made.Add(new KeyValuePair<View, int>(v, panes[i]));

                    // colour code and no part marks
                    var perRule = new Dictionary<string, int>();
                    int coloured = ColourParts(v, model, perRule);
                    int marks = RemoveMarks(v);
                    v.Modify();

                    Made++;
                    var rules = new List<string>();
                    foreach (var kv in perRule) rules.Add(kv.Key + " " + kv.Value);
                    Say("OK   " + label + " " + where + ", scale 1:" + v.Attributes.Scale.ToString("0.##", Inv) +
                        ", " + cutNote + ", " + CountModelObjects(v) + " model object(s), " +
                        coloured + " coloured (" + string.Join(", ", rules) + "), " + marks + " mark(s) removed");
                }
                catch (Exception e)
                {
                    Failed++;
                    Say("FAIL " + label + " : " + e.GetType().Name + ": " + e.Message);
                }
            }
            dr.CommitChanges("P142 isometric views");

            // PLACEMENT - only now, after the commit: right after Insert() Tekla
            // can still report the frame of the first view at a default scale
            // (921 x 991 instead of 182 x 237 on 2026-10-24), and a move computed
            // from that put ISOMETRIC VIEW 2 off the sheet.  Measure, move,
            // commit, and measure again until every frame centre is in its pane.
            for (int pass = 1; pass <= 3; pass++)
            {
                bool moved = false;
                foreach (var kv in made)
                {
                    View v = kv.Key;
                    int k = kv.Value % SLOTS.GetLength(0);
                    double cx = SLOTS[k, 0] * w, cy = SLOTS[k, 1] * h;
                    Refresh(v);
                    double fx, fy;
                    FrameCentre(v, out fx, out fy);
                    if (Math.Abs(fx - cx) <= 1.0 && Math.Abs(fy - cy) <= 1.0) continue;
                    Say("  pass " + pass + ": ISOMETRIC VIEW " + (k + 1) + " frame " + F1(v.Width) + " x " + F1(v.Height) +
                        " centred at (" + F1(fx) + ", " + F1(fy) + ") -> pane centre (" + F1(cx) + ", " + F1(cy) + ")");
                    v.Origin = new Point(v.Origin.X + (cx - fx), v.Origin.Y + (cy - fy), 0);
                    v.Modify();
                    moved = true;
                }
                dr.CommitChanges("P142 isometric views placed");
                if (!moved) break;
            }
            foreach (var kv in made)
            {
                View v = kv.Key;
                Refresh(v);
                double fx, fy;
                FrameCentre(v, out fx, out fy);
                Say("placed ISOMETRIC VIEW " + (kv.Value + 1) + ": frame " + F1(v.Width) + " x " + F1(v.Height) +
                    " centred at (" + F1(fx) + ", " + F1(fy) + ")" + (FrameOnSheet(v, w, h) ? "" : "  - NOT fully on the sheet"));
            }

            // Tekla creates the part marks of a new view in the background
            // ("Creating marks..."), after Insert() has returned.  Wait for it,
            // then remove the marks once more.
            int late = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                System.Threading.Thread.Sleep(1500);
                int n = 0;
                foreach (View v in ModelViewsOf(dr)) if (IsOurs(v)) n += RemoveMarks(v);
                late += n;
                if (n == 0 && pass > 0) break;
            }
            if (late > 0)
            {
                dr.CommitChanges("P142 isometric marks removed");
                Say(late + " part mark(s) created late by Tekla were removed");
            }
        }

        // ------------------------------------------------------------ main entry
        /// <summary>The macro's core: CAPTURE, CREATE or REBUILD/REPAIR (see the header).</summary>
        public static void Execute(Drawing dr, WfIsoResult result, bool save)
        {
            Log.Length = 0;
            Made = Failed = 0;
            try
            {
                Run(dr, result, save);
            }
            catch (Exception e)
            {
                Failed++;
                Say("EXCEPTION " + e.GetType().Name + ": " + e.Message);
                result.Ok = false;
                result.Message = "The macro stopped: " + e.Message;
            }
            result.Made = Made;
            result.Failed = Failed;
            result.Log = Log.ToString();
        }

        static void Run(Drawing dr, WfIsoResult result, bool save)
        {
            if (dr == null) { result.Ok = false; result.Message = "No drawing is open."; return; }
            string mark = dr.Mark;
            Say("drawing " + mark + "  " + dr.Name + "  (" + dr.Title1 + " / " + dr.Title2 + " / " + dr.Title3 + ")");

            if (!IsIsometricViewDrawing(dr))
            {
                result.Ok = false;
                result.Message = "The open drawing is not an ISOMETRIC VIEW drawing - nothing done.";
                return;
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                result.Ok = false;
                result.Message = "No connection to the Tekla model.";
                return;
            }

            List<View> existing = ModelViewsOf(dr);
            string refPath = ReferencePath();
            var ours = new List<View>();
            foreach (View v in existing) if (IsOurs(v)) ours.Add(v);

            // ---------------- REBUILD / REPAIR: views made by this macro
            if (ours.Count > 0)
            {
                if (IsProtected(dr))
                {
                    result.Ok = false;
                    result.Message = "The drawing " + mark + " is issued or frozen - it is not written to.";
                    return;
                }
                DialogResult ans = MessageBox.Show(
                    "Drawing " + mark + " already holds " + ours.Count + " isometric view(s) made by this macro.\n\n" +
                    "YES  = delete those views and create them again from the reference\n" +
                    "         (view properties, depth cut, colour code, no part marks).\n" +
                    "NO   = only move views that lie off the sheet back onto it.\n" +
                    "CANCEL = do nothing.",
                    "P142 Isometric views", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel)
                {
                    result.Ok = true;
                    result.Message = "Cancelled - nothing done.";
                    return;
                }
                if (ans == DialogResult.No)
                {
                    Repair(dr, ours, result, save, mark);
                    return;
                }
                int deleted = 0;
                foreach (View v in ours) if (v.Delete()) deleted++;
                dr.CommitChanges("P142 isometric views deleted for rebuild");
                Say("REBUILD: " + deleted + " of " + ours.Count + " view(s) made by this macro deleted");
                existing = ModelViewsOf(dr);
            }

            // ---------------- CAPTURE: 3D views that this macro did not make
            if (existing.Count > 0)
            {
                List<WfIsoViewSpec> specs = Capture(dr, existing, model);
                double cw, ch;
                SheetSize(dr, out cw, out ch);
                foreach (var s0 in specs)
                    if (!OnSheet(s0.FrameCX, s0.FrameCY, cw, ch))
                        Say("WARNING: view '" + s0.Name + "' lies off its own sheet - CREATE will use the standard layout for it");
                SaveReference(refPath, dr, specs);
                Say("reference written: " + refPath);
                result.Ok = true;
                result.Message = "CAPTURE: " + specs.Count + " view(s) of " + mark + " " + dr.Name + " recorded as the reference.\n" +
                    "Nothing was written into this drawing.\n\n" +
                    "Next: open the isometric drawing of the block you want and run the macro again.";
                return;
            }

            // ---------------- CREATE
            if (IsProtected(dr))
            {
                result.Ok = false;
                result.Message = "The drawing " + mark + " is issued or frozen - it is not written to.";
                return;
            }
            WfIsoReference existingRef = File.Exists(refPath) ? LoadReference(refPath) : null;
            if (existingRef != null && (string.IsNullOrEmpty(existingRef.SourceMark) || !existingRef.HasFrames))
            {
                string old = refPath + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(refPath, old);
                Say("the reference file was written by an older version - retired to " + Path.GetFileName(old));
            }
            if (!File.Exists(refPath))
            {
                Say("no reference captured yet - looking for one in the model ...");
                List<View> refViews;
                Drawing refDr = FindReferenceDrawing(dr, out refViews);
                if (refDr == null)
                {
                    result.Ok = false;
                    result.Message = "No reference found: no other ISOMETRIC VIEW drawing of this model has 3D views.\n\n" +
                        "Open an isometric drawing that already has its 3D views and run this macro on it " +
                        "(it only reads that drawing), then come back here and run it again.";
                    return;
                }
                Say("reference taken from " + refDr.Mark + " " + refDr.Name + (IsProtected(refDr) ? " (issued/frozen - read only)" : ""));
                SaveReference(refPath, refDr, Capture(refDr, refViews, model));
                Say("reference written: " + refPath);
            }

            WfIsoReference r = LoadReference(refPath);
            if (r.Specs.Count == 0)
            {
                result.Ok = false;
                result.Message = "The reference file holds no view: " + refPath;
                return;
            }
            Say("reference: " + r.Specs.Count + " view(s) from " + r.SourceMark + " " + r.SourceName);

            string block = BlockOfDrawing(dr);
            if (block.Length == 0)
            {
                result.Ok = false;
                result.Message = "Could not tell the block of drawing " + mark + " from its name '" + dr.Name + "'.";
                return;
            }
            int scanned;
            List<TSM.Part> parts = BlockParts(model, block, out scanned);
            Say("block " + block + ": " + parts.Count + " part(s) of " + scanned + " scanned");
            if (parts.Count == 0)
            {
                result.Ok = false;
                result.Message = "No part of block " + block + " was found (by ASSEMBLY_POS) - nothing created.";
                return;
            }

            List<View> live = LiveReferenceViews(r);
            CreateViews(dr, model, r, live, parts);

            if (save && Made > 0)
            {
                try { new DrawingHandler().SaveActiveDrawing(); Say("drawing saved"); }
                catch (Exception e) { Failed++; Say("the drawing could not be saved: " + e.Message); }
            }

            result.Ok = Failed == 0;
            result.Message = "CREATE on " + mark + " (block " + block + ") from reference " + r.SourceMark + " " + r.SourceName +
                             ": " + Made + " view(s) created, " + Failed + " failed.";
            Say(result.Message);
        }

        /// <summary>Moves views made by this macro that lie off the sheet back onto it.</summary>
        static void Repair(Drawing dr, List<View> ours, WfIsoResult result, bool save, string mark)
        {
            double w, h;
            SheetSize(dr, out w, out h);
            Say("REPAIR: " + ours.Count + " view(s) made by this macro, sheet " + F1(w) + " x " + F1(h));
            int moved = 0;
            for (int i = 0; i < ours.Count; i++)
            {
                View v = ours[i];
                string before = "(" + F1(v.Origin.X) + ", " + F1(v.Origin.Y) + ")";
                if (FrameOnSheet(v, w, h))
                {
                    Say("ok   view '" + v.Name + "' at " + before + " is on the sheet");
                    continue;
                }
                string where = Place(v, double.NaN, double.NaN, i, w, h);
                moved++;
                Made++;
                Say("MOVED view '" + v.Name + "' from " + before + " " + where);
            }
            if (moved > 0)
            {
                dr.CommitChanges("P142 isometric views repaired");
                if (save)
                {
                    try { new DrawingHandler().SaveActiveDrawing(); Say("drawing saved"); }
                    catch (Exception e) { Failed++; Say("the drawing could not be saved: " + e.Message); }
                }
            }
            result.Ok = Failed == 0;
            result.Message = "REPAIR on " + mark + ": " + moved + " of " + ours.Count + " view(s) moved onto the sheet.";
            Say(result.Message);
        }
    }
}
