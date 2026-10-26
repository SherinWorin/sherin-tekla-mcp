// WF_BaseCW.cs - the machinery of the P142_BASE_C_W macro (Tekla Structures 2025):
// the BASE, CEILING, WALL framing layout drawing of a pod.
//
// Compiled into WF_BaseCW.dll; the thin loader P142_BASE_C_W.cs (macro catalog)
// loads it fresh on every run.  Needs WF_Rules.dll (block names).
//
// WHAT IT DOES - on the EMPTY BASE, CEILING, WALL drawing that is open (e.g.
// BP20M-10001 made from the template):
//   1. TWIN.  It finds the pod whose walls are identical to this pod's, moved
//      by an offset (dx, dy): the BOTTOM_TRACK lines of every wall / ledge-wall
//      assembly are compared (BP20M = BP02M + 96850 mm in X, 2026-10-24).
//      The twin's own BASE, CEILING, WALL drawing (3 views) is the reference.
//      If no pod is a twin, nothing is written (the rule-based mode for a new
//      pod is a later stage).
//   2. VIEWS.  Each of the reference's three plans (base HRS framing plan,
//      ceiling framing plan "Plan @ T-LGS", wall framing plan) is created again:
//      the view plane and display plane moved by the offset, the same
//      restriction box, the view properties copied live, and EXACTLY the twin
//      parts of the parts the reference view shows (part list) - which also
//      reproduces the ceiling plan's phase filter.  Each view is put in its
//      pane (frame centres measured on BP02M-10001 [78]).
//   3. ANNOTATIONS.  Because the display plane moves with the offset, every
//      annotation keeps its coordinates: dimension chains, notes with leaders,
//      section marks S1..S5, detail marks, lifting-arrow symbols, rectangles,
//      lines - and the part marks, re-attached to the twin part, with the
//      reference's content, position and leader.  Tekla's own automatic part
//      marks of the new views are removed first.
//   4. COLOURS.  Each part's drawing representation is copied from its twin.
//   5. DIMENSIONS.  The reference's dimension sets are NOT copied; the plans are
//      measured by the dimension engines (2026-10-24, user decision):
//        base HRS framing plan -> WfBfDim (the engine of P142_BF_AutoDimension);
//        ceiling framing plan -> WfCeilDim (rules learned from the 19 reference
//        ceiling plans, WF_CeilDim.cs); wall framing plan -> WfCore.DimensionViewAioRun
//        (the engine of WallFramingAutoDimensionPro-AIO: bays, bands, top outline,
//        overall dims, north run, wall-panel marks, section marks, text notes).
//      So on the WALL plan the reference's notes, section marks and part marks
//      are NOT copied either - the AIO engine draws them.
//   CEILING_GYPSUM: the ceiling plan also shows the pod's CEILING_GYPSUM parts,
//   and its view depth goes down to 50 mm below the lowest ceiling part.
//
// SAFETY: it only writes into the open drawing; it never switches the active
// drawing (that closes the open drawing without saving it); issued / frozen
// drawings are refused; it asks before rebuilding views it made itself
// (tagged P142_BCW=1).  The reference is only read (GetSheet(), not opened).
//
// TEKLA 2025 API (checked in the 2025.0 XML docs / DLL metadata): View(ContainerView,
// CS, CS, ArrayList ids) + Insert; View.Attributes / RestrictionBox / Name /
// ViewBase.Origin settable; StraightDimensionSetHandler.CreateDimensionSet(ViewBase,
// PointList, Vector, double, StraightDimensionSetAttributes); Text(ViewBase, Point,
// string, PlacingBase, TextAttributes); LeaderLinePlacing(Point); Symbol(ViewBase,
// Point, SymbolInfo, SymbolAttributes); SectionMark(View, Point, Point,
// SectionMarkAttributes); DetailMark(View, Point, Point, Point, DetailMarkAttributes);
// Rectangle(ViewBase, Point, double, double, RectangleAttributes); Line(ViewBase,
// Point, Point, double, LineAttributes); Mark(ModelObject) with settable
// InsertionPoint / Placing / Attributes; DrawingObject.GetRelatedObjects().
//
// RISK: MEDIUM.  Never touches the model.
// Needs WF_Rules.dll and WF_Core.dll (+ WF_License / WF_Settings) next to it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DPart = Tekla.Structures.Drawing.Part;
using DText = Tekla.Structures.Drawing.Text;
using DLine = Tekla.Structures.Drawing.Line;
using DSymbol = Tekla.Structures.Drawing.Symbol;
using DRect = Tekla.Structures.Drawing.Rectangle;
using DMark = Tekla.Structures.Drawing.Mark;
using DModelObject = Tekla.Structures.Drawing.ModelObject;

namespace P142.WallFraming
{
    /// <summary>Outcome of a macro run (the loader reads these fields).</summary>
    public sealed class WfBaseCwResult
    {
        /// <summary>True when the run succeeded.</summary>
        public bool Ok;
        /// <summary>Summary.</summary>
        public string Message = "";
        /// <summary>Log.</summary>
        public string Log = "";
        /// <summary>Views created.</summary>
        public int Made;
        /// <summary>Failures.</summary>
        public int Failed;
    }

    /// <summary>The machinery of the BASE, CEILING, WALL drawing macro.</summary>
    public static class WfBaseCwCore
    {
        /// <summary>User property written on every view the macro creates.</summary>
        public const string TAG_PROP = "P142_BCW";
        /// <summary>Value of the user property.</summary>
        public const string TAG_VALUE = "1";
        /// <summary>Title fragment of the drawing.</summary>
        public const string TITLE = "BASE, CEILING, WALL";
        /// <summary>Wall tracks match within this (mm) after the offset.</summary>
        const double TWIN_TOL = 2.0;

        // the panes of the three plans: frame centres on the A1 sheet, measured on
        // BP02M-10001 [78] while it was the open drawing (2026-10-24)
        const double BASE_CX = 201.8, BASE_CY = 445.9;      // base HRS framing plan, top-left
        const double CEIL_CX = 570.7, CEIL_CY = 440.4;      // ceiling framing plan, top-right
        const double WALL_CX = 202.9, WALL_CY = 145.3;      // wall framing plan, bottom-left

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly StringBuilder LogB = new StringBuilder();
        static int Made, Failed;
        static void Say(string s) { LogB.AppendLine(s); }
        static string F1(double d) { return d.ToString("F1", Inv); }

        /// <summary>The loader asks this; the core refuses protected drawings itself.</summary>
        public static bool IsProtected(Drawing d) { return Protected(d); }

        static bool Protected(Drawing d) { return d == null || d.IsFrozen || d.IsIssued || d.IsIssuedButModified; }

        static bool IsBcw(Drawing d)
        {
            if (d == null) return false;
            foreach (string t in new[] { d.Title1, d.Title2, d.Title3 })
                if (!string.IsNullOrEmpty(t) && t.IndexOf(TITLE, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string BlockOfName(string name)
        {
            string t = (name ?? "").Trim();
            int i = t.IndexOf('-');
            return i > 0 ? t.Substring(0, i) : t;
        }

        static string BlockOf(Drawing d)
        {
            string b = BlockOfName(d.Name);
            return WfRules.NormaliseBlock(b).Length > 0 ? b : (d.Title1 ?? "").Trim();
        }

        // ------------------------------------------------------------ views
        /// <summary>Every view, including those inside a ContainerView on the sheet.</summary>
        static List<View> AllViews(ContainerView sheet)
        {
            var res = new List<View>();
            var seen = new HashSet<string>();
            Collect(sheet, res, seen, 0);
            return res;
        }

        static void Collect(ContainerView c, List<View> res, HashSet<string> seen, int depth)
        {
            if (c == null || depth > 4) return;
            var subs = new List<ContainerView>();
            DrawingObjectEnumerator ve = c.GetViews();
            while (ve.MoveNext())
            {
                View v = ve.Current as View;
                if (v != null) { if (!v.IsSheet && seen.Add(Key(v))) res.Add(v); continue; }
                ContainerView cv = ve.Current as ContainerView;
                if (cv != null) subs.Add(cv);
            }
            DrawingObjectEnumerator oe = c.GetObjects();
            while (oe.MoveNext())
            {
                ContainerView cv = oe.Current as ContainerView;
                if (cv != null && !(oe.Current is View) && !ReferenceEquals(cv, c)) subs.Add(cv);
            }
            foreach (var cv in subs) Collect(cv, res, seen, depth + 1);
        }

        static string Key(View v)
        {
            try
            {
                AABB b = v.RestrictionBox;
                return v.Name + "|" + P(v.ViewCoordinateSystem.Origin) + P(v.ViewCoordinateSystem.AxisX) +
                       (b == null ? "" : P(b.MinPoint) + P(b.MaxPoint));
            }
            catch (Exception) { return Guid.NewGuid().ToString(); }
        }

        static string P(Point p) { return p == null ? "-" : "(" + F1(p.X) + "," + F1(p.Y) + "," + F1(p.Z) + ")"; }

        static bool IsOurs(View v)
        {
            string val = "";
            try { v.GetUserProperty(TAG_PROP, ref val); } catch (Exception) { }
            return val == TAG_VALUE;
        }

        /// <summary>The pane (frame centre) of a reference view, by its role.</summary>
        static void PaneOf(View rv, out double cx, out double cy, out string role)
        {
            CoordinateSystem vcs = rv.ViewCoordinateSystem;
            AABB b = rv.RestrictionBox;
            if (vcs != null && vcs.Origin.Z > 1000) { cx = CEIL_CX; cy = CEIL_CY; role = "CEILING FRAMING PLAN"; return; }
            if (b != null && b.MinPoint.Z > -100) { cx = BASE_CX; cy = BASE_CY; role = "BASE HRS FRAMING PLAN"; return; }
            cx = WALL_CX; cy = WALL_CY; role = "WALL FRAMING PLAN";
        }

        // ------------------------------------------------------------ the model
        /// <summary>A part of a block, with its global extent.</summary>
        sealed class BPart
        {
            public TSM.Part Part;
            public string Name = "", Profile = "";
            public double[] Min = new double[3], Max = new double[3];
        }

        /// <summary>The parts of every block (by ASSEMBLY_POS), global extents.</summary>
        /// <summary>Every part of the model (with or without a block), by model id.</summary>
        static readonly Dictionary<int, BPart> _byId = new Dictionary<int, BPart>();
        /// <summary>Every part of the model by PlaceKey(p, 0, 0) (first one wins).</summary>
        static readonly Dictionary<string, BPart> _byPlace = new Dictionary<string, BPart>();

        static Dictionary<string, List<BPart>> PartsByBlock(TSM.Model model, Dictionary<string, List<double[]>> tracks)
        {
            var res = new Dictionary<string, List<BPart>>();
            _byId.Clear(); _byPlace.Clear();
            var posOf = new Dictionary<int, string>();
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane());
                TSM.ModelObjectSelector sel = model.GetModelObjectSelector();
                foreach (TSM.ModelObject.ModelObjectEnum kind in new[] {
                    TSM.ModelObject.ModelObjectEnum.BEAM, TSM.ModelObject.ModelObjectEnum.POLYBEAM,
                    TSM.ModelObject.ModelObjectEnum.CONTOURPLATE })
                {
                    TSM.ModelObjectEnumerator en = sel.GetAllObjectsWithType(kind);
                    while (en.MoveNext())
                    {
                        TSM.Part p = en.Current as TSM.Part;
                        if (p == null) continue;
                        string apos = "";
                        TSM.Assembly asm = null;
                        try { asm = p.GetAssembly(); } catch (Exception) { }
                        if (asm != null && !posOf.TryGetValue(asm.Identifier.ID, out apos))
                        {
                            apos = ""; asm.GetReportProperty("ASSEMBLY_POS", ref apos); posOf[asm.Identifier.ID] = apos ?? "";
                        }
                        var bp = new BPart { Part = p, Name = p.Name ?? "" };
                        try
                        {
                            TSM.Solid so = p.GetSolid();
                            bp.Min = new[] { so.MinimumPoint.X, so.MinimumPoint.Y, so.MinimumPoint.Z };
                            bp.Max = new[] { so.MaximumPoint.X, so.MaximumPoint.Y, so.MaximumPoint.Z };
                        }
                        catch (Exception) { continue; }
                        string prof = ""; p.GetReportProperty("PROFILE", ref prof); bp.Profile = prof ?? "";
                        // the whole-model index: parts without a block are matched by position
                        _byId[p.Identifier.ID] = bp;
                        string pk = PlaceKey(bp, 0, 0);
                        if (!_byPlace.ContainsKey(pk)) _byPlace[pk] = bp;
                        if (string.IsNullOrEmpty(apos)) continue;
                        int dash = apos.IndexOf('-');
                        string blk = WfRules.NormaliseBlock(dash > 0 ? apos.Substring(0, dash) : apos);
                        string fam = dash > 0 ? apos.Substring(dash + 1) : "";
                        List<BPart> l;
                        if (!res.TryGetValue(blk, out l)) { l = new List<BPart>(); res[blk] = l; }
                        l.Add(bp);
                        // the wall / ledge-wall bottom tracks: the fingerprint of a pod
                        TSM.Beam bm = p as TSM.Beam;
                        if (bm != null && string.Equals(bm.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase) &&
                            (fam.StartsWith("WP", StringComparison.OrdinalIgnoreCase) || fam.StartsWith("LW", StringComparison.OrdinalIgnoreCase)))
                        {
                            List<double[]> t;
                            if (!tracks.TryGetValue(blk, out t)) { t = new List<double[]>(); tracks[blk] = t; }
                            t.Add(new[] { bm.StartPoint.X, bm.StartPoint.Y, bm.StartPoint.Z, bm.EndPoint.X, bm.EndPoint.Y, bm.EndPoint.Z });
                        }
                    }
                }
            }
            finally { wph.SetCurrentTransformationPlane(original); }
            return res;
        }

        /// <summary>True when every track of a matches a track of b moved by (dx, dy) and the counts agree.</summary>
        static bool SameWalls(List<double[]> a, List<double[]> b, double dx, double dy)
        {
            if (a == null || b == null || a.Count != b.Count || a.Count == 0) return false;
            var used = new bool[b.Count];
            foreach (var t in a)
            {
                bool found = false;
                for (int i = 0; i < b.Count && !found; i++)
                {
                    if (used[i]) continue;
                    var u = b[i];
                    bool same = Near(t[0], u[0] + dx) && Near(t[1], u[1] + dy) && Near(t[2], u[2]) &&
                                Near(t[3], u[3] + dx) && Near(t[4], u[4] + dy) && Near(t[5], u[5]);
                    bool rev = Near(t[0], u[3] + dx) && Near(t[1], u[4] + dy) && Near(t[2], u[5]) &&
                               Near(t[3], u[0] + dx) && Near(t[4], u[1] + dy) && Near(t[5], u[2]);
                    if (same || rev) { used[i] = true; found = true; }
                }
                if (!found) return false;
            }
            return true;
        }

        static bool Near(double a, double b) { return Math.Abs(a - b) <= TWIN_TOL; }

        static double[] Extent(List<double[]> tr)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue;
            foreach (var t in tr) { x0 = Math.Min(x0, Math.Min(t[0], t[3])); y0 = Math.Min(y0, Math.Min(t[1], t[4])); }
            return new[] { x0, y0 };
        }

        /// <summary>Key of a part by its extent only, moved back by the offset.</summary>
        static string PlaceKey(BPart p, double dx, double dy)
        {
            return Math.Round(p.Min[0] - dx).ToString(Inv) + "|" + Math.Round(p.Min[1] - dy).ToString(Inv) + "|" + Math.Round(p.Min[2]).ToString(Inv) + "|" +
                   Math.Round(p.Max[0] - dx).ToString(Inv) + "|" + Math.Round(p.Max[1] - dy).ToString(Inv) + "|" + Math.Round(p.Max[2]).ToString(Inv);
        }

        static List<BPart> _refParts = new List<BPart>();

        /// <summary>The reference part under a point (reference view coordinates), or null.</summary>
        static BPart RefPartAt(Point p)
        {
            Point g = ToGlobal(_rDcs, p);
            BPart best = null; double bd = 30.0;   // within 30 mm in plan
            foreach (var bp in _refParts)
            {
                double ddx = Math.Max(0, Math.Max(bp.Min[0] - g.X, g.X - bp.Max[0]));
                double ddy = Math.Max(0, Math.Max(bp.Min[1] - g.Y, g.Y - bp.Max[1]));
                double d = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (d < bd) { bd = d; best = bp; }
            }
            return best;
        }

        /// <summary>Key of a part for twin matching: name, profile and extent moved back by the offset.</summary>
        static string TwinKey(BPart p, double dx, double dy)
        {
            return p.Name + "|" + p.Profile + "|" +
                   Math.Round(p.Min[0] - dx).ToString(Inv) + "|" + Math.Round(p.Min[1] - dy).ToString(Inv) + "|" + Math.Round(p.Min[2]).ToString(Inv) + "|" +
                   Math.Round(p.Max[0] - dx).ToString(Inv) + "|" + Math.Round(p.Max[1] - dy).ToString(Inv) + "|" + Math.Round(p.Max[2]).ToString(Inv);
        }

        // ------------------------------------------------------------ entry
        /// <summary>The macro core (see the header).</summary>
        public static void Execute(Drawing dr, WfBaseCwResult result, bool save)
        {
            LogB.Length = 0;
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
            result.Log = LogB.ToString();
            if (result.Ok)
                MessageBox.Show("P142_BASE_C_W: " + result.Message + "\n\nLog:\n" + Trim(result.Log, 6000),
                                "P142 Base, ceiling, wall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string Trim(string s, int n) { return s.Length > n ? s.Substring(0, n) + "\n..." : s; }

        static void Run(Drawing dr, WfBaseCwResult result, bool save)
        {
            if (dr == null) { result.Ok = false; result.Message = "No drawing is open."; return; }
            string mark = dr.Mark;
            Say("drawing " + mark + " " + dr.Name + " (" + dr.Title1 + " / " + dr.Title2 + " / " + dr.Title3 + ")");
            if (!IsBcw(dr)) { result.Ok = false; result.Message = "The open drawing is not a BASE, CEILING, WALL drawing - nothing done."; return; }
            if (Protected(dr)) { result.Ok = false; result.Message = "The drawing " + mark + " is issued or frozen - it is not written to."; return; }

            var existing = AllViews(dr.GetSheet());
            var ours = existing.FindAll(IsOurs);
            if (existing.Count > ours.Count)
            {
                // a finished drawing (e.g. BP02M-10001): READ-ONLY study of the
                // ceiling framing plans of every BASE, CEILING, WALL drawing
                DialogResult st = MessageBox.Show(
                    "Drawing " + mark + " already has views this macro did not make, so it is not changed.\n\n" +
                    "YES = READ-ONLY study: record the dimensions and parts of the ceiling framing plan of EVERY\n" +
                    "BASE, CEILING, WALL drawing in WF_CeilDim_study.txt (nothing is written to any drawing).\n" +
                    "NO / CANCEL = do nothing.",
                    "P142 Base, ceiling, wall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (st != DialogResult.Yes) { result.Ok = true; result.Message = "Nothing done."; return; }
                string file = WfCeilStudy.Run(new TSM.Model(), LogB);
                result.Ok = true;
                result.Message = "Ceiling study written (read only): " + file;
                return;
            }
            if (ours.Count > 0)
            {
                DialogResult ans = MessageBox.Show(
                    "Drawing " + mark + " already holds " + ours.Count + " view(s) made by this macro.\n\n" +
                    "YES = delete those views (with their dimensions, notes and marks) and create them again.\n" +
                    "NO / CANCEL = do nothing.",
                    "P142 Base, ceiling, wall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) { result.Ok = true; result.Message = "Nothing done."; return; }
                int del = 0;
                foreach (View v in ours) if (v.Delete()) del++;
                dr.CommitChanges("P142 BCW views deleted for rebuild");
                Say("REBUILD: " + del + " view(s) made by this macro deleted");
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus()) { result.Ok = false; result.Message = "No connection to the Tekla model."; return; }

            // ---- 1. the twin pod
            string block = WfRules.NormaliseBlock(BlockOf(dr));
            var tracks = new Dictionary<string, List<double[]>>();
            var parts = PartsByBlock(model, tracks);
            List<double[]> mine;
            if (!tracks.TryGetValue(block, out mine) || mine.Count == 0)
            {
                result.Ok = false; result.Message = "No wall bottom track of block " + block + " was found in the model - nothing done."; return;
            }
            double[] e0 = Extent(mine);
            string twin = null; double dx = 0, dy = 0;
            var names = new List<string>(tracks.Keys); names.Sort();
            foreach (string b in names)
            {
                if (b == block) continue;
                double[] e1 = Extent(tracks[b]);
                double tx = e0[0] - e1[0], ty = e0[1] - e1[1];
                if (SameWalls(mine, tracks[b], tx, ty)) { twin = b; dx = tx; dy = ty; break; }
            }
            Say("block " + block + ": " + mine.Count + " wall track(s); twin " + (twin ?? "NONE") +
                (twin != null ? " offset (" + F1(dx) + ", " + F1(dy) + ")" : ""));
            if (twin == null)
            {
                result.Ok = false;
                result.Message = "No pod has the same walls as " + block + " - nothing done.\n" +
                                 "(The rule-based mode for a new pod is not built yet.)";
                return;
            }

            // ---- the twin's BASE, CEILING, WALL drawing
            Drawing refDr = null; List<View> refViews = null;
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            while (de.MoveNext())
            {
                Drawing d = de.Current;
                if (d == null || d.Mark == mark || !IsBcw(d)) continue;
                if (WfRules.NormaliseBlock(BlockOf(d)) != twin) continue;
                var vs = AllViews(d.GetSheet());
                if (vs.Count > (refViews == null ? 0 : refViews.Count)) { refDr = d; refViews = vs; }
            }
            if (refDr == null || refViews.Count == 0)
            {
                result.Ok = false; result.Message = "The twin " + twin + " has no BASE, CEILING, WALL drawing with views - nothing done."; return;
            }
            Say("reference " + refDr.Mark + " " + refDr.Name + ": " + refViews.Count + " view(s) (read only, not opened)");

            // ---- twin part map: reference model part id -> target part
            var targetByKey = new Dictionary<string, BPart>();
            var targetByPlace = new Dictionary<string, BPart>();
            foreach (var p in parts[block])
            {
                targetByKey[TwinKey(p, dx, dy)] = p;
                targetByPlace[PlaceKey(p, dx, dy)] = p;
            }
            var refToTarget = new Dictionary<int, BPart>();
            int unmatched = 0, byPlace = 0;
            foreach (var p in parts[twin])
            {
                BPart t;
                if (targetByKey.TryGetValue(TwinKey(p, 0, 0), out t)) refToTarget[p.Part.Identifier.ID] = t;
                else if (targetByPlace.TryGetValue(PlaceKey(p, 0, 0), out t)) { refToTarget[p.Part.Identifier.ID] = t; byPlace++; }
                else unmatched++;
            }
            if (byPlace > 0) Say("  " + byPlace + " part(s) matched by position only (different name or profile in " + block + ")");
            _dxAll = dx; _dyAll = dy;
            _refParts = parts[twin];
            Say("twin parts matched: " + refToTarget.Count + " of " + parts[twin].Count + (unmatched > 0 ? " (" + unmatched + " without a twin)" : ""));

            // ---- 2. the views
            ContainerView sheet = dr.GetSheet();
            var made = new List<KeyValuePair<View, View>>();   // (new, reference)
            var centres = new List<double[]>();
            var roles = new List<string>();
            var framingOf = new List<HashSet<int>>();
            foreach (View rv in refViews)
            {
                double cx, cy; string role;
                PaneOf(rv, out cx, out cy, out role);
                try
                {
                    var ids = new ArrayList();
                    var framing = new HashSet<int>();
                    int missing = 0;
                    DrawingObjectEnumerator pe = rv.GetAllObjects(new[] { typeof(DPart) });
                    while (pe.MoveNext())
                    {
                        DPart dp = pe.Current as DPart;
                        if (dp == null) continue;
                        BPart t;
                        if (refToTarget.TryGetValue(dp.ModelIdentifier.ID, out t) || TwinAnywhere(dp.ModelIdentifier.ID, refToTarget, out t))
                        { ids.Add(t.Part.Identifier); framing.Add(t.Part.Identifier.ID); }
                        else missing++;
                    }
                    bool ceiling = role.StartsWith("CEILING");
                    var gypsum = ceiling ? GypsumOf(framing) : new List<BPart>();
                    foreach (var g in gypsum) ids.Add(g.Part.Identifier);
                    CoordinateSystem vcs = Shift(rv.ViewCoordinateSystem, dx, dy);
                    CoordinateSystem dcs = Shift(rv.DisplayCoordinateSystem, dx, dy);
                    View v = new View(sheet, vcs, dcs, ids);
                    if (!v.Insert()) { Failed++; Say("FAIL " + role + ": Insert() returned false"); continue; }
                    v.SetUserProperty(TAG_PROP, TAG_VALUE);
                    try { v.Attributes = rv.Attributes; } catch (Exception ex) { Say("  " + role + ": properties not copied - " + ex.Message); }
                    v.Name = rv.Name;
                    v.Modify();
                    try
                    {
                        AABB box = rv.RestrictionBox;
                        if (ceiling) box = DeeperBox(box, vcs, framing, gypsum);
                        v.RestrictionBox = box; v.Modify();
                    }
                    catch (Exception ex) { Say("  " + role + ": box not copied - " + ex.Message); }
                    try { v.Select(); } catch (Exception) { }
                    Say("     new view  displayCS " + Cs(v.DisplayCoordinateSystem) + "  viewCS " + Cs(v.ViewCoordinateSystem));
                    Say("     reference displayCS " + Cs(rv.DisplayCoordinateSystem));
                    made.Add(new KeyValuePair<View, View>(v, rv));
                    centres.Add(new[] { cx, cy });
                    roles.Add(role);
                    framingOf.Add(framing);
                    Made++;
                    Say("OK   " + role + " ('" + rv.Name + "'): " + ids.Count + " part(s)" + (missing > 0 ? ", " + missing + " without a twin" : "") +
                        (ceiling ? ", " + gypsum.Count + " CEILING_GYPSUM" : ""));
                }
                catch (Exception ex) { Failed++; Say("FAIL " + role + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
            dr.CommitChanges("P142 BCW views");

            // ---- placement (after the commit: a new view can report a stale frame)
            PlaceAll(dr, made, centres, "after the views");

            // ---- Tekla's own part marks of the new views come late: remove them
            int auto = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                System.Threading.Thread.Sleep(1500);
                int n = 0;
                foreach (var kv in made) n += RemoveMarks(kv.Key);
                auto += n;
                if (n == 0 && pass > 0) break;
            }
            if (auto > 0) dr.CommitChanges("P142 BCW automatic marks removed");
            Say(auto + " automatic part mark(s) of Tekla removed");

            // ---- 3 + 4. annotations and colours
            foreach (var kv in made)
            {
                try { CopyView(kv.Key, kv.Value, refToTarget, dx, dy, roles[made.IndexOf(kv)].StartsWith("WALL")); }
                catch (Exception ex) { Failed++; Say("FAIL annotations of '" + kv.Value.Name + "': " + ex.Message); }
            }
            dr.CommitChanges("P142 BCW annotations");

            // ---- 5. dimensions: the dimension engines measure the new plans
            for (int i = 0; i < made.Count; i++)
            {
                View v = made[i].Key;
                try { v.Select(); } catch (Exception) { }
                try
                {
                    if (roles[i].StartsWith("WALL"))
                    {
                        var wr = new WfResult();
                        bool ok = WfCore.DimensionViewAioRun(v, dr, wr);
                        Say("DIM  " + roles[i] + " (AIO wall engine): " + wr.Made + " created, " + wr.Failed + " failed" + (ok ? "" : " - no wall panel found") +
                            (string.IsNullOrEmpty(wr.Message) ? "" : "  [" + wr.Message + "]"));
                        if (wr.Failed > 0) { Failed += wr.Failed; Say(Indent(wr.Log)); }
                    }
                    else if (roles[i].StartsWith("CEILING"))
                    {
                        var bl = new StringBuilder(); int bf;
                        int n = WfCeilDim.DimensionView(v, dr, bl, framingOf[i], out bf);
                        Say("DIM  " + roles[i] + " (ceiling engine): " + n + " created, " + bf + " failed");
                        Say(Indent(bl.ToString()));
                        Failed += bf;
                    }
                    else
                    {
                        var bl = new StringBuilder(); int bf;
                        int n = WfBfDim.DimensionView(v, dr, bl, framingOf[i], out bf);
                        Say("DIM  " + roles[i] + " (base-frame engine): " + n + " created, " + bf + " failed");
                        if (bf > 0 || n == 0) { Failed += bf; Say(Indent(bl.ToString())); }
                    }
                }
                catch (Exception ex) { Failed++; Say("FAIL dimensions of " + roles[i] + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
            dr.CommitChanges("P142 BCW dimensions");
            PlaceAll(dr, made, centres, "after the annotations");

            if (save && Made > 0)
            {
                try { new DrawingHandler().SaveActiveDrawing(); Say("drawing saved"); }
                catch (Exception ex) { Failed++; Say("the drawing could not be saved: " + ex.Message); }
            }
            result.Ok = Failed == 0;
            result.Message = "BASE, CEILING, WALL on " + mark + " (" + block + ") from its twin " + twin + " (" + refDr.Name + "): " +
                             Made + " view(s), " + Failed + " failure(s).";
            Say(result.Message);
        }

        static double _dxAll, _dyAll;

        static string Indent(string s) { return "     " + (s ?? "").Trim().Replace("\n", "\n     "); }

        /// <summary>
        /// The twin of a reference part that has no block (no ASSEMBLY_POS): the
        /// part of the WHOLE model with the same extent moved by the offset.
        /// </summary>
        static bool TwinAnywhere(int refId, Dictionary<int, BPart> refToTarget, out BPart t)
        {
            t = null;
            BPart r;
            if (!_byId.TryGetValue(refId, out r)) return false;
            if (!_byPlace.TryGetValue(PlaceKey(r, -_dxAll, -_dyAll), out t) || t == null || t.Part.Identifier.ID == refId) { t = null; return false; }
            refToTarget[refId] = t;
            return true;
        }

        /// <summary>The CEILING_GYPSUM parts over the plan extent of the ceiling framing.</summary>
        static List<BPart> GypsumOf(HashSet<int> framing)
        {
            var res = new List<BPart>();
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (int id in framing)
            {
                BPart b; if (!_byId.TryGetValue(id, out b)) continue;
                x0 = Math.Min(x0, b.Min[0]); y0 = Math.Min(y0, b.Min[1]); x1 = Math.Max(x1, b.Max[0]); y1 = Math.Max(y1, b.Max[1]);
            }
            if (x1 < x0) return res;
            foreach (var b in _byId.Values)
            {
                if (framing.Contains(b.Part.Identifier.ID)) continue;
                if (b.Name.IndexOf("CEILING_GYPSUM", StringComparison.OrdinalIgnoreCase) < 0) continue;
                double cx = (b.Min[0] + b.Max[0]) / 2, cy = (b.Min[1] + b.Max[1]) / 2;
                if (cx < x0 - 50 || cx > x1 + 50 || cy < y0 - 50 || cy > y1 + 50) continue;
                res.Add(b);
                string ph = "";
                try { b.Part.GetReportProperty("PHASE", ref ph); } catch (Exception) { }
                Say("     CEILING_GYPSUM " + b.Part.Identifier.ID + " " + b.Profile + "  z " + F1(b.Min[2]) + ".." + F1(b.Max[2]) + "  phase " + ph);
            }
            return res;
        }

        /// <summary>
        /// The ceiling plan's box, with its bottom moved down to 50 mm below the
        /// lowest ceiling part (framing + gypsum), in view-plane coordinates.
        /// </summary>
        static AABB DeeperBox(AABB box, CoordinateSystem vcs, HashSet<int> framing, List<BPart> gypsum)
        {
            double low = double.MaxValue;
            foreach (int id in framing) { BPart b; if (_byId.TryGetValue(id, out b)) low = Math.Min(low, b.Min[2]); }
            foreach (var g in gypsum) low = Math.Min(low, g.Min[2]);
            if (low == double.MaxValue || box == null) return box;
            Vector z = Unit(vcs.AxisX).Cross(Unit(vcs.AxisY));
            // the depth along the view's own Z axis of the global level (low - 50)
            double zLocal = ((low - 50.0) - vcs.Origin.Z) * z.Z;
            double zMin = Math.Min(box.MinPoint.Z, box.MaxPoint.Z), zMax = Math.Max(box.MinPoint.Z, box.MaxPoint.Z);
            if (z.Z >= 0) zMin = Math.Min(zMin, zLocal); else zMax = Math.Max(zMax, zLocal);
            Say("     ceiling view depth: lowest ceiling part z " + F1(low) + " -> box z " + F1(zMin) + ".." + F1(zMax) + " (was " + F1(box.MinPoint.Z) + ".." + F1(box.MaxPoint.Z) + ")");
            return new AABB(new Point(box.MinPoint.X, box.MinPoint.Y, zMin), new Point(box.MaxPoint.X, box.MaxPoint.Y, zMax));
        }

        // ---- point mapping: reference view (display coords) -> model -> + offset -> new view
        static CoordinateSystem _rDcs, _tDcs;
        static double _dx, _dy;

        static Vector Unit(Vector v) { var u = new Vector(v); u.Normalize(); return u; }

        static Point ToGlobal(CoordinateSystem cs, Point p)
        {
            Vector x = Unit(cs.AxisX), y = Unit(cs.AxisY), z = x.Cross(y);
            return new Point(cs.Origin.X + p.X * x.X + p.Y * y.X + p.Z * z.X,
                             cs.Origin.Y + p.X * x.Y + p.Y * y.Y + p.Z * z.Y,
                             cs.Origin.Z + p.X * x.Z + p.Y * y.Z + p.Z * z.Z);
        }

        static Point FromGlobal(CoordinateSystem cs, Point g)
        {
            Vector x = Unit(cs.AxisX), y = Unit(cs.AxisY), z = x.Cross(y);
            double rx = g.X - cs.Origin.X, ry = g.Y - cs.Origin.Y, rz = g.Z - cs.Origin.Z;
            return new Point(rx * x.X + ry * x.Y + rz * x.Z, rx * y.X + ry * y.Y + rz * y.Z, rx * z.X + ry * z.Y + rz * z.Z);
        }

        /// <summary>A reference-view point mapped into the new view.</summary>
        static Point M(Point p)
        {
            Point g = ToGlobal(_rDcs, p);
            return FromGlobal(_tDcs, new Point(g.X + _dx, g.Y + _dy, g.Z));
        }

        /// <summary>A reference-view direction mapped into the new view (no translation).</summary>
        static Vector MV(Vector v)
        {
            Point a = M(new Point(0, 0, 0)), b = M(new Point(v.X, v.Y, v.Z));
            return new Vector(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        }

        static string Cs(CoordinateSystem c)
        {
            return c == null ? "null" : "origin " + P(c.Origin) + " X " + P(new Point(c.AxisX)) + " Y " + P(new Point(c.AxisY));
        }

        /// <summary>Moves every new view so its frame centre is on its pane centre (passes, commit between).</summary>
        static void PlaceAll(Drawing dr, List<KeyValuePair<View, View>> made, List<double[]> centres, string when)
        {
            for (int pass = 1; pass <= 3; pass++)
            {
                bool moved = false;
                for (int i = 0; i < made.Count; i++)
                {
                    View v = made[i].Key;
                    try { v.Select(); } catch (Exception) { }
                    Vector fo = v.FrameOrigin;
                    double fx = v.Origin.X + (fo == null ? 0 : fo.X) + v.Width / 2.0;
                    double fy = v.Origin.Y + (fo == null ? 0 : fo.Y) + v.Height / 2.0;
                    double cx = centres[i][0], cy = centres[i][1];
                    if (Math.Abs(fx - cx) <= 1 && Math.Abs(fy - cy) <= 1) continue;
                    v.Origin = new Point(v.Origin.X + (cx - fx), v.Origin.Y + (cy - fy), 0);
                    v.Modify();
                    moved = true;
                }
                dr.CommitChanges("P142 BCW views placed");
                if (!moved) break;
            }
            foreach (var kv in made)
            {
                View v = kv.Key;
                try { v.Select(); } catch (Exception) { }
                Vector fo = v.FrameOrigin;
                Say("placed '" + kv.Value.Name + "' " + when + ": frame " + F1(v.Width) + " x " + F1(v.Height) + " centred at (" +
                    F1(v.Origin.X + (fo == null ? 0 : fo.X) + v.Width / 2.0) + ", " + F1(v.Origin.Y + (fo == null ? 0 : fo.Y) + v.Height / 2.0) + ")");
            }
        }

        static CoordinateSystem Shift(CoordinateSystem c, double dx, double dy)
        {
            return new CoordinateSystem(new Point(c.Origin.X + dx, c.Origin.Y + dy, c.Origin.Z),
                                        new Vector(c.AxisX), new Vector(c.AxisY));
        }

        static int RemoveMarks(View v)
        {
            var marks = new List<DrawingObject>();
            DrawingObjectEnumerator en = v.GetAllObjects(new[] { typeof(DMark) });
            while (en.MoveNext()) if (en.Current != null) marks.Add(en.Current);
            int n = 0;
            foreach (DrawingObject m in marks) if (m.Delete()) n++;
            return n;
        }

        // ------------------------------------------------------------ copy one view
        static void CopyView(View v, View rv, Dictionary<int, BPart> refToTarget, double dx, double dy, bool wall)
        {
            try { v.Select(); } catch (Exception) { }
            _rDcs = rv.DisplayCoordinateSystem;
            _tDcs = v.DisplayCoordinateSystem;
            _dx = dx; _dy = dy;
            // the drawing parts of the new view, by model id
            var targetDrawn = new Dictionary<int, DPart>();
            DrawingObjectEnumerator te = v.GetAllObjects(new[] { typeof(DPart) });
            while (te.MoveNext()) { DPart dp = te.Current as DPart; if (dp != null) targetDrawn[dp.ModelIdentifier.ID] = dp; }

            var n = new SortedDictionary<string, int>();
            var fail = new SortedDictionary<string, int>();
            DrawingObjectEnumerator oe = rv.GetAllObjects();
            var objs = new List<DrawingObject>();
            while (oe.MoveNext()) if (oe.Current != null) objs.Add(oe.Current);

            int dimsLeft = 0, aioLeft = 0;
            foreach (DrawingObject o in objs)
            {
                if (o is StraightDimensionSet || o is StraightDimension) { if (o is StraightDimensionSet) dimsLeft++; continue; }
                if (wall && (o is DText || o is SectionMark || o is DMark)) { aioLeft++; continue; }
                string kind = o.GetType().Name;
                bool ok;
                try { ok = CopyObject(o, v, refToTarget, targetDrawn); }
                catch (Exception) { ok = false; }
                if (ok) { int c; n.TryGetValue(kind, out c); n[kind] = c + 1; }
                else if (kind != "LeaderLine" && kind != "Connection" && kind != "ReferenceModel" && kind != "StraightDimension")
                { int c; fail.TryGetValue(kind, out c); fail[kind] = c + 1; }
            }
            v.Modify();
            Say("  '" + rv.Name + "' copied: " + Join(n) + (fail.Count > 0 ? "  NOT copied: " + Join(fail) : "") +
                "  (" + dimsLeft + " reference dimension set(s) left to the dimension engines" +
                (wall ? ", " + aioLeft + " note(s) / section mark(s) / mark(s) left to the AIO engine" : "") + ")");
        }

        static string Join(SortedDictionary<string, int> d)
        {
            var l = new List<string>();
            foreach (var kv in d) l.Add(kv.Key + " " + kv.Value);
            return string.Join(", ", l);
        }

        /// <summary>Re-creates one reference object in the new view.  True when made.</summary>
        static bool CopyObject(DrawingObject o, View v, Dictionary<int, BPart> refToTarget, Dictionary<int, DPart> targetDrawn)
        {
            // part: copy its representation (colours) onto the twin part
            DPart rp = o as DPart;
            if (rp != null)
            {
                BPart t; DPart tp;
                if (!refToTarget.TryGetValue(rp.ModelIdentifier.ID, out t) || !targetDrawn.TryGetValue(t.Part.Identifier.ID, out tp)) return false;
                tp.Attributes = rp.Attributes;
                return tp.Modify();
            }

            StraightDimensionSet ds = o as StraightDimensionSet;
            if (ds != null)
            {
                var pts = new List<Point>();
                Vector up = null;
                DrawingObjectEnumerator ce = ds.GetObjects();
                while (ce.MoveNext())
                {
                    StraightDimension sd = ce.Current as StraightDimension;
                    if (sd == null) continue;
                    if (up == null) up = sd.UpDirection;
                    pts.Add(M(sd.StartPoint)); pts.Add(M(sd.EndPoint));
                }
                if (pts.Count < 2 || up == null) return false;
                up = MV(up);
                // order along the measuring direction, merge shared points
                double mx = up.Y, my = -up.X;
                pts.Sort(delegate (Point a, Point b) { return (a.X * mx + a.Y * my).CompareTo(b.X * mx + b.Y * my); });
                var pl = new PointList();
                Point last = null;
                foreach (Point p in pts)
                {
                    if (last != null && Math.Abs((p.X - last.X) * mx + (p.Y - last.Y) * my) < 0.5) continue;
                    pl.Add(new Point(p.X, p.Y, p.Z)); last = p;
                }
                if (pl.Count < 2) return false;
                var made = new StraightDimensionSetHandler().CreateDimensionSet(v, pl, up, ds.Distance,
                    ds.Attributes as StraightDimensionSet.StraightDimensionSetAttributes);
                return made != null;
            }

            DText t0 = o as DText;
            if (t0 != null)
            {
                var ta = t0.Attributes as DText.TextAttributes;
                LeaderLinePlacing lp = t0.Placing as LeaderLinePlacing;
                DText nt = lp != null
                    ? new DText(v, M(t0.InsertionPoint), t0.TextString, new LeaderLinePlacing(M(lp.StartPoint)), ta)
                    : new DText(v, M(t0.InsertionPoint), t0.TextString, ta);
                return nt.Insert();
            }

            SectionMark sm = o as SectionMark;
            if (sm != null)
                return new SectionMark(v, M(sm.LeftPoint), M(sm.RightPoint), sm.Attributes).Insert();

            DetailMark dm = o as DetailMark;
            if (dm != null)
                return new DetailMark(v, M(dm.CenterPoint), M(dm.BoundaryPoint), M(dm.LabelPoint), dm.Attributes).Insert();

            DSymbol sy = o as DSymbol;
            if (sy != null)
            {
                var ns = new DSymbol(v, M(sy.InsertionPoint), new SymbolInfo(sy.SymbolInfo.SymbolFile, sy.SymbolInfo.SymbolIndex),
                                     sy.Attributes as DSymbol.SymbolAttributes);
                return ns.Insert();
            }

            DRect re = o as DRect;
            if (re != null)
            {
                var nr = new DRect(v, M(re.StartPoint), re.Width, re.Height, re.Attributes as DRect.RectangleAttributes);
                nr.Angle = re.Angle;
                return nr.Insert();
            }

            DLine li = o as DLine;
            if (li != null)
                return new DLine(v, M(li.StartPoint), M(li.EndPoint), li.Bulge, li.Attributes as DLine.LineAttributes).Insert();

            DMark mk = o as DMark;
            if (mk != null)
            {
                // the part the reference mark belongs to -> its twin in the new view
                DrawingObjectEnumerator re2 = mk.GetRelatedObjects();
                DModelObject owner = null;
                try { while (re2.MoveNext()) { owner = re2.Current as DModelObject; if (owner != null) break; } } catch (Exception) { }
                int ownerId = owner != null ? owner.ModelIdentifier.ID : 0;
                if (ownerId == 0)
                {
                    LeaderLinePlacing olp = mk.Placing as LeaderLinePlacing;
                    BPart rpart = RefPartAt(olp != null ? olp.StartPoint : mk.InsertionPoint);
                    if (rpart != null) ownerId = rpart.Part.Identifier.ID;
                }
                BPart t; DPart tp;
                if (ownerId == 0 || !refToTarget.TryGetValue(ownerId, out t) || !targetDrawn.TryGetValue(t.Part.Identifier.ID, out tp)) return false;
                var nm = new DMark(tp);
                nm.Attributes = mk.Attributes as DMark.MarkAttributes;
                nm.InsertionPoint = M(mk.InsertionPoint);
                LeaderLinePlacing mlp = mk.Placing as LeaderLinePlacing;
                if (mlp != null) nm.Placing = new LeaderLinePlacing(M(mlp.StartPoint));
                return nm.Insert();
            }
            return false;
        }
    }
}
