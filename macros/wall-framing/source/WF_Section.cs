// WF_Section.cs - SECTIONAL ELEVATION macro, STUDY (read-only) edition.  Tekla Structures 2025.
//
// On a BASE, CEILING, WALL drawing it also writes WF_BCW_survey.txt: a read-only
// survey of EVERY BASE, CEILING, WALL drawing of the model (section marks in
// global coordinates, views, notes, detail marks, symbols) and the wall lines
// of every block, to learn where the sections cut.
//
// The first draft of this macro was disabled on 2026-10-24: it switched the
// active drawing to the wall framing layout and back, which closes the open
// drawing without saving it.  Until the real macro is rebuilt, this core only
// READS the open drawing and writes what it finds to
//     %LOCALAPPDATA%\P142\WallFraming\WF_Section_dump_<mark>.txt
// It never switches drawings, never inserts, modifies or deletes anything,
// and never saves.  It is safe on issued and frozen drawings.
//
// For every view: type, name, scale, origin, frame, coordinate systems,
// restriction box; every drawing object with all its public properties
// (reflection, two levels deep: texts, leader placings, dimension sets and
// their dimensions, level marks, lines, polylines, clouds, marks ...); and
// every model part shown, with its name, profile, assembly position and its
// extent in the view's display coordinates (to know which part a leader
// points at).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DText = Tekla.Structures.Drawing.Text;

namespace P142.WallFraming
{
    /// <summary>Outcome of a macro run (the loader reads these fields).</summary>
    public sealed class WfSectionResult
    {
        /// <summary>True when the run succeeded.</summary>
        public bool Ok;
        /// <summary>Summary.</summary>
        public string Message = "";
        /// <summary>Log.</summary>
        public string Log = "";
        /// <summary>Objects created (always 0 in the study edition).</summary>
        public int Made;
        /// <summary>Failures.</summary>
        public int Failed;
    }

    /// <summary>The SECTIONAL ELEVATION macro core - study (read-only) edition.</summary>
    public static class WfSectionCore
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static StringBuilder Out = new StringBuilder();

        /// <summary>
        /// The study edition writes nothing into the drawing, so it may read an
        /// issued or frozen drawing.  (The loader asks this before Execute.)
        /// </summary>
        public static bool IsProtected(Drawing d) { return false; }

        /// <summary>Reads the open drawing and writes the dump file.</summary>
        public static void Execute(Drawing dr, WfSectionResult result, bool save)
        {
            Out = new StringBuilder();
            string path = "";
            try
            {
                if (dr == null) { result.Ok = false; result.Message = "No drawing is open."; return; }
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "P142", "WallFraming");
                Directory.CreateDirectory(dir);
                string safe = (dr.Mark ?? "drawing").Replace("[", "").Replace("]", "").Replace(" ", "_");
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                path = Path.Combine(dir, "WF_Section_dump_" + safe + ".txt");

                var counts = Dump(dr);
                File.WriteAllText(path, Out.ToString(), Encoding.UTF8);
                if (IsMarksDrawing(dr))
                {
                    Out = new StringBuilder();
                    string sp = Path.Combine(dir, "WF_BCW_survey.txt");
                    counts += "\nSURVEY of every BASE, CEILING, WALL drawing: " + Survey() + "\nWritten to " + sp;
                    File.WriteAllText(sp, Out.ToString(), Encoding.UTF8);
                }

                result.Ok = true;
                result.Message = "STUDY (read only): " + counts + "\nWritten to " + path;
                result.Log = result.Message;
                MessageBox.Show("WF_Section STUDY - nothing was changed in the drawing.\n\n" + result.Message,
                                "P142 Section study", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.Failed = 1;
                result.Message = "The study stopped: " + e.GetType().Name + ": " + e.Message +
                                 (path.Length > 0 ? "\n(partial dump not written)" : "");
                result.Log = Out.ToString();
                if (result.Log.Length > 3000) result.Log = result.Log.Substring(0, 3000) + "...";
            }
        }

        static void W(string s) { Out.AppendLine(s); }

        /// <summary>
        /// Every view of a container, including the views inside a ContainerView
        /// on the sheet (the ceiling plan of the BASE, CEILING, WALL drawings is
        /// one of those - sheet.GetViews() alone does not return it).
        /// </summary>
        static List<View> AllViews(ContainerView c)
        {
            var res = new List<View>();
            Collect(c, res, 0);
            // the same view can be reached twice (sheet and container): keep one
            var seen = new HashSet<string>();
            var uniq = new List<View>();
            foreach (View v in res)
            {
                string k;
                try
                {
                    AABB b = v.RestrictionBox;
                    k = v.Name + "|" + v.ViewType + "|" + Cs(v.ViewCoordinateSystem) + "|" + (b == null ? "" : P(b.MinPoint) + P(b.MaxPoint));
                }
                catch (Exception) { k = Guid.NewGuid().ToString(); }
                if (seen.Add(k)) uniq.Add(v);
            }
            return uniq;
        }

        static void Collect(ContainerView c, List<View> res, int depth)
        {
            if (c == null || depth > 4) return;
            DrawingObjectEnumerator ve = c.GetViews();
            while (ve.MoveNext())
            {
                object o = ve.Current;
                View v = o as View;
                if (v != null) { if (!v.IsSheet) res.Add(v); }
                ContainerView cv = o as ContainerView;
                if (cv != null) Collect(cv, res, depth + 1);
            }
            // a ContainerView placed on the sheet as an object
            DrawingObjectEnumerator oe = c.GetObjects();
            while (oe.MoveNext())
            {
                ContainerView cv = oe.Current as ContainerView;
                if (cv != null && !ReferenceEquals(cv, c)) Collect(cv, res, depth + 1);
            }
        }

        static bool IsMarksDrawing(Drawing d)
        {
            if (d == null) return false;
            foreach (string t in new[] { d.Title1, d.Title2, d.Title3 })
                if (!string.IsNullOrEmpty(t) && t.IndexOf("BASE, CEILING, WALL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static Point ToGlobal(CoordinateSystem cs, Point p)
        {
            if (cs == null) return p;
            Vector x = new Vector(cs.AxisX), y = new Vector(cs.AxisY);
            x.Normalize(); y.Normalize();
            Vector z = x.Cross(y);
            return new Point(cs.Origin.X + p.X * x.X + p.Y * y.X + p.Z * z.X,
                             cs.Origin.Y + p.X * x.Y + p.Y * y.Y + p.Z * z.Y,
                             cs.Origin.Z + p.X * x.Z + p.Y * y.Z + p.Z * z.Z);
        }

        /// <summary>
        /// READ ONLY survey of every BASE, CEILING, WALL drawing of the model
        /// (read through GetSheet(), never opened): its views, its section marks
        /// in global coordinates, its detail marks, symbols, texts and dimension
        /// sets; then, per block, the wall lines (BOTTOM_TRACK start/end, global)
        /// and the block's global extent - to learn where the sections cut.
        /// </summary>
        static string Survey()
        {
            W("# P142 BASE, CEILING, WALL survey - read only");
            W("# written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            var blocks = new List<string>();
            int nd = 0, nm = 0;
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            var drawings = new List<Drawing>();
            while (de.MoveNext()) if (IsMarksDrawing(de.Current)) drawings.Add(de.Current);
            drawings.Sort(delegate (Drawing a, Drawing b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            foreach (Drawing d in drawings)
            {
                nd++;
                string block = d.Name ?? "";
                int dash = block.IndexOf('-');
                if (dash > 0) block = block.Substring(0, dash);
                if (!blocks.Contains(block)) blocks.Add(block);
                W("");
                W("DRAWING " + d.Mark + " name=" + d.Name + " block=" + block + " titles='" + d.Title1 + "'/'" + d.Title2 + "'/'" + d.Title3 +
                  "' frozen=" + d.IsFrozen + " issued=" + d.IsIssued);
                List<View> views;
                try
                {
                    views = AllViews(d.GetSheet());
                }
                catch (Exception e) { W("  could not read: " + e.Message); continue; }
                foreach (View v in views)
                {
                    W("  VIEW name='" + v.Name + "' type=" + v.ViewType + " scale=" + v.Attributes.Scale.ToString("0.###", Inv));
                    W("    viewCS " + Cs(v.ViewCoordinateSystem));
                    W("    displayCS " + Cs(v.DisplayCoordinateSystem));
                    AABB b = v.RestrictionBox;
                    W("    box " + (b == null ? "null" : P(b.MinPoint) + " - " + P(b.MaxPoint)));
                    var counts = new SortedDictionary<string, int>();
                    DrawingObjectEnumerator oe = v.GetAllObjects();
                    while (oe.MoveNext())
                    {
                        DrawingObject o = oe.Current;
                        if (o == null) continue;
                        string tn = o.GetType().Name;
                        int c; counts.TryGetValue(tn, out c); counts[tn] = c + 1;
                        SectionMark sm = o as SectionMark;
                        if (sm != null)
                        {
                            nm++;
                            Point gl = ToGlobal(v.DisplayCoordinateSystem, sm.LeftPoint), gr = ToGlobal(v.DisplayCoordinateSystem, sm.RightPoint);
                            W("    SECTIONMARK " + sm.Attributes.MarkName + " left " + P(sm.LeftPoint) + " right " + P(sm.RightPoint) +
                              " | global " + P(gl) + " -> " + P(gr));
                            continue;
                        }
                        DText t = o as DText;
                        if (t != null)
                        {
                            string ls = "";
                            LeaderLinePlacing lp = t.Placing as LeaderLinePlacing;
                            if (lp != null) ls = " leader " + P(lp.StartPoint);
                            W("    TEXT '" + (t.TextString ?? "").Replace("\n", "\\n").Replace("\r", "") + "' at " + P(t.InsertionPoint) + ls);
                            continue;
                        }
                        if (o is DetailMark) { W("    DETAILMARK " + ((DetailMark)o).Attributes.MarkName + " center " + P(((DetailMark)o).CenterPoint)); continue; }
                        if (o is Symbol) { var sy = (Symbol)o; W("    SYMBOL " + sy.SymbolInfo.SymbolFile + "/" + sy.SymbolInfo.SymbolIndex + " at " + P(sy.InsertionPoint)); continue; }
                    }
                    var cl = new List<string>();
                    foreach (var kv in counts) cl.Add(kv.Key + " " + kv.Value);
                    W("    counts: " + string.Join(", ", cl));
                }
            }

            // the wall lines of every block, from the model
            W("");
            W("=== WALL LINES PER BLOCK (BOTTOM_TRACK start -> end, global, and the block extent) ===");
            try
            {
                TSM.Model model = new TSM.Model();
                if (model.GetConnectionStatus())
                {
                    TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
                    TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
                    try
                    {
                        wph.SetCurrentTransformationPlane(new TSM.TransformationPlane());
                        var lines = new Dictionary<string, List<string>>();
                        var ext = new Dictionary<string, double[]>();
                        var posOf = new Dictionary<int, string>();
                        TSM.ModelObjectEnumerator en = model.GetModelObjectSelector().GetAllObjectsWithType(TSM.ModelObject.ModelObjectEnum.BEAM);
                        while (en.MoveNext())
                        {
                            TSM.Beam bm = en.Current as TSM.Beam;
                            if (bm == null) continue;
                            string apos = "";
                            TSM.Assembly asm = null;
                            try { asm = bm.GetAssembly(); } catch (Exception) { }
                            if (asm != null && !posOf.TryGetValue(asm.Identifier.ID, out apos))
                            {
                                apos = ""; asm.GetReportProperty("ASSEMBLY_POS", ref apos); posOf[asm.Identifier.ID] = apos ?? "";
                            }
                            if (string.IsNullOrEmpty(apos)) continue;
                            int dsh = apos.IndexOf('-');
                            string blk = WfRules.NormaliseBlock(dsh > 0 ? apos.Substring(0, dsh) : apos);
                            double[] e;
                            if (!ext.TryGetValue(blk, out e)) { e = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue }; ext[blk] = e; }
                            foreach (Point q in new[] { bm.StartPoint, bm.EndPoint })
                            {
                                e[0] = Math.Min(e[0], q.X); e[1] = Math.Min(e[1], q.Y); e[2] = Math.Min(e[2], q.Z);
                                e[3] = Math.Max(e[3], q.X); e[4] = Math.Max(e[4], q.Y); e[5] = Math.Max(e[5], q.Z);
                            }
                            if (!string.Equals(bm.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase)) continue;
                            List<string> l;
                            if (!lines.TryGetValue(blk, out l)) { l = new List<string>(); lines[blk] = l; }
                            l.Add(apos + " " + P(bm.StartPoint) + " -> " + P(bm.EndPoint));
                        }
                        var keys = new List<string>(ext.Keys);
                        keys.Sort();
                        foreach (string k in keys)
                        {
                            double[] e = ext[k];
                            W("");
                            W("BLOCK " + k + " extent (" + e[0].ToString("0.#", Inv) + ", " + e[1].ToString("0.#", Inv) + ", " + e[2].ToString("0.#", Inv) +
                              ") - (" + e[3].ToString("0.#", Inv) + ", " + e[4].ToString("0.#", Inv) + ", " + e[5].ToString("0.#", Inv) + ")");
                            List<string> l;
                            if (lines.TryGetValue(k, out l)) foreach (string s2 in l) W("  " + s2);
                        }
                    }
                    finally { wph.SetCurrentTransformationPlane(original); }
                }
            }
            catch (Exception e) { W("model read failed: " + e.Message); }
            return nd + " drawing(s), " + blocks.Count + " block(s), " + nm + " section mark(s)";
        }

        static string P(Point p)
        {
            return p == null ? "null" : "(" + p.X.ToString("0.###", Inv) + ", " + p.Y.ToString("0.###", Inv) + ", " + p.Z.ToString("0.###", Inv) + ")";
        }

        static string Cs(CoordinateSystem c)
        {
            return c == null ? "null" : "origin " + P(c.Origin) + " X " + P(c.AxisX) + " Y " + P(c.AxisY);
        }

        static string Dump(Drawing dr)
        {
            W("# P142 sectional elevation STUDY dump - read only");
            W("# written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            W("drawing mark=" + dr.Mark + " name=" + dr.Name + " type=" + dr.GetType().Name);
            W("titles: 1='" + dr.Title1 + "' 2='" + dr.Title2 + "' 3='" + dr.Title3 + "'");
            W("frozen=" + dr.IsFrozen + " issued=" + dr.IsIssued + " issuedButModified=" + dr.IsIssuedButModified);
            ContainerView sheet = dr.GetSheet();
            W("sheet " + sheet.Width.ToString("0.###", Inv) + " x " + sheet.Height.ToString("0.###", Inv));

            // objects placed directly on the sheet (not in a view)
            int nViews = 0, nObjects = 0, nParts = 0;
            TSM.Model model = new TSM.Model();
            bool modelOk = false;
            try { modelOk = model.GetConnectionStatus(); } catch (Exception) { }

            var views = AllViews(sheet);

            W("");
            W("=== SHEET-LEVEL OBJECTS ===");
            DrawingObjectEnumerator se = sheet.GetObjects();
            while (se.MoveNext())
            {
                DrawingObject o = se.Current;
                if (o == null || o is View) continue;
                nObjects++;
                DumpObject(o, "  ");
            }

            foreach (View v in views)
            {
                nViews++;
                W("");
                W("==========================================================================");
                W("VIEW name='" + v.Name + "' type=" + v.ViewType + " scale=" + v.Attributes.Scale.ToString("0.###", Inv));
                W("  origin " + P(v.Origin) + " frameOrigin " + (v.FrameOrigin == null ? "null" : P(new Point(v.FrameOrigin))) +
                  " width " + v.Width.ToString("0.###", Inv) + " height " + v.Height.ToString("0.###", Inv));
                W("  viewCS    " + Cs(v.ViewCoordinateSystem));
                W("  displayCS " + Cs(v.DisplayCoordinateSystem));
                AABB b = v.RestrictionBox;
                W("  restrictionBox " + (b == null ? "null" : P(b.MinPoint) + " - " + P(b.MaxPoint)));
                W("  attributes:");
                DumpProps(v.Attributes, "    ", 1);

                // annotations and graphics (everything but model objects)
                var perType = new SortedDictionary<string, int>();
                DrawingObjectEnumerator oe = v.GetAllObjects();
                var partIds = new List<Tekla.Structures.Identifier>();
                while (oe.MoveNext())
                {
                    DrawingObject o = oe.Current;
                    if (o == null) continue;
                    string tn = o.GetType().Name;
                    int c; perType.TryGetValue(tn, out c); perType[tn] = c + 1;
                    var mo = o as Tekla.Structures.Drawing.ModelObject;
                    if (mo != null)
                    {
                        if (o is Tekla.Structures.Drawing.Part) partIds.Add(mo.ModelIdentifier);
                        continue;
                    }
                    nObjects++;
                    W("  ---");
                    DumpObject(o, "  ");
                }
                W("  counts: " + string.Join(", ", Counts(perType)));

                // the parts of the view, in view (display) coordinates
                if (modelOk && partIds.Count > 0)
                {
                    W("  PARTS (extent in display coordinates of the view):");
                    nParts += DumpParts(model, partIds, v.DisplayCoordinateSystem);
                }
            }
            return nViews + " view(s), " + nObjects + " annotation/graphic object(s), " + nParts + " part(s)";
        }

        static IEnumerable<string> Counts(SortedDictionary<string, int> d)
        {
            foreach (var kv in d) yield return kv.Key + " " + kv.Value;
        }

        static int DumpParts(TSM.Model model, List<Tekla.Structures.Identifier> ids, CoordinateSystem cs)
        {
            int n = 0;
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(cs));
                foreach (var id in ids)
                {
                    TSM.Part p = model.SelectModelObject(id) as TSM.Part;
                    if (p == null) continue;
                    string profile = "", material = "", apos = "", pos = "";
                    p.GetReportProperty("PROFILE", ref profile);
                    p.GetReportProperty("MATERIAL", ref material);
                    p.GetReportProperty("PART_POS", ref pos);
                    try { TSM.Assembly a = p.GetAssembly(); if (a != null) a.GetReportProperty("ASSEMBLY_POS", ref apos); } catch (Exception) { }
                    string ext = "";
                    try
                    {
                        TSM.Solid so = p.GetSolid();
                        if (so != null) ext = P(so.MinimumPoint) + " - " + P(so.MaximumPoint);
                    }
                    catch (Exception) { }
                    W("    part id=" + id.ID + " name=" + p.Name + " profile=" + profile + " material=" + material +
                      " pos=" + pos + " asm=" + apos + " class=" + p.Class + " ext " + ext);
                    n++;
                }
            }
            finally
            {
                wph.SetCurrentTransformationPlane(original);
            }
            return n;
        }

        static void DumpObject(DrawingObject o, string ind)
        {
            W(ind + "OBJECT " + o.GetType().FullName);
            DumpProps(o, ind + "  ", 2);
            // children: the dimensions of a dimension set, the leader lines of a mark ...
            try
            {
                MethodInfo gm = o.GetType().GetMethod("GetObjects", Type.EmptyTypes);
                if (gm == null) return;
                DrawingObjectEnumerator ce = gm.Invoke(o, null) as DrawingObjectEnumerator;
                if (ce == null) return;
                int k = 0;
                while (ce.MoveNext() && k < 200)
                {
                    DrawingObject c = ce.Current;
                    if (c == null) continue;
                    W(ind + "  CHILD " + c.GetType().FullName);
                    DumpProps(c, ind + "    ", 1);
                    k++;
                }
            }
            catch (Exception) { }
        }

        static readonly HashSet<string> Skip = new HashSet<string> { "Parent", "View", "Drawing", "Sheet" };

        static void DumpProps(object o, string ind, int depth)
        {
            if (o == null) return;
            PropertyInfo[] props;
            try { props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance); }
            catch (Exception) { return; }
            foreach (PropertyInfo p in props)
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead || Skip.Contains(p.Name)) continue;
                object val;
                try { val = p.GetValue(o, null); }
                catch (Exception e) { W(ind + p.Name + " = <" + (e.InnerException ?? e).GetType().Name + ">"); continue; }
                if (val == null) { W(ind + p.Name + " = null"); continue; }
                Type t = val.GetType();
                if (val is Point) { W(ind + p.Name + " = " + P((Point)val)); continue; }
                if (val is string) { W(ind + p.Name + " = '" + ((string)val).Replace("\r", "").Replace("\n", "\\n") + "'"); continue; }
                if (t.IsPrimitive || t.IsEnum || val is decimal) { W(ind + p.Name + " = " + Convert.ToString(val, Inv)); continue; }
                if (val is CoordinateSystem) { W(ind + p.Name + " = " + Cs((CoordinateSystem)val)); continue; }
                if (val is AABB) { var b = (AABB)val; W(ind + p.Name + " = " + P(b.MinPoint) + " - " + P(b.MaxPoint)); continue; }
                if (val is IEnumerable && !(val is string))
                {
                    int k = 0;
                    var items = new List<string>();
                    foreach (object it in (IEnumerable)val)
                    {
                        if (k++ >= 20) { items.Add("..."); break; }
                        items.Add(it is Point ? P((Point)it) : Convert.ToString(it, Inv));
                    }
                    W(ind + p.Name + " = [" + string.Join("; ", items) + "]");
                    continue;
                }
                if (depth > 0 && t.Namespace != null && t.Namespace.StartsWith("Tekla"))
                {
                    W(ind + p.Name + " : " + t.Name);
                    DumpProps(val, ind + "  ", depth - 1);
                }
                else W(ind + p.Name + " = " + Convert.ToString(val, Inv));
            }
        }
    }
}
