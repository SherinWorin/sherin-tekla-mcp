// WF_BaseCW.cs - the machinery of the P142_BASE_C_W macro (Tekla Structures 2025):
// the BASE, CEILING, WALL framing layout drawing of a pod.
//
// Compiled into WF_BaseCW.dll; the thin loader P142_BASE_C_W.cs (macro catalog)
// loads it fresh on every run.  Needs WF_Rules.dll and WF_Core.dll.
//
// RULE-BASED (2026-10-26): the drawing is built from THIS pod's own parts in
// the model - no twin pod, nothing copied from another drawing.  (The twin /
// copy mode of 2026-10-24 was only used to study BP02M-10001 [78]; its rules
// are below.)  On the EMPTY BASE, CEILING, WALL drawing that is open:
//
//   1. THE POD.  Block = the drawing name before the first dash (BP7-10001 ->
//      BP7).  Its extent in plan = the parts whose ASSEMBLY_POS starts with the
//      block (walls, base frame, ceiling frame).  Every part whose centre lies
//      in that extent belongs to the pod - the ceiling panels too, whose
//      assembly positions carry another prefix (e.g. BP4-CP-31).
//   2. THE LEVELS and THE SPLIT (corrected 2026-10-26 on BP07-10001):
//        base top     = the lowest wall BOTTOM_TRACK (the walls stand on it)
//        ceiling      = the lowest CEILING BEAM
//      ceiling parts = bottom at or above the ceiling - 20 (the gypsum hangs
//                      12.5 under the beams)
//      wall parts    = the LGS: a wall part name (WfRules.WallParts) or an LGS
//                      / wall-gypsum profile of the colour code (WfIsoColours)
//      base parts    = the rest: the HRS base frame, its perimeter RHS120*60
//                      included (it rises past the wall bottom tracks, so a
//                      split by height alone put it in the wall plan)
//   3. THE VIEWS - three plans looking down, global axes, part list = the
//      parts of that level, restriction box = the pod extent + 100 and:
//        BASE HRS FRAMING PLAN  z: lowest .. highest base part
//        WALL FRAMING PLAN      z: base top - 326 .. base top + 34  ([78]: -200 .. 160)
//        CEILING FRAMING PLAN   view plane 50 below the lowest ceiling part
//                               (CEILING_GYPSUM shows), z up to the highest one
//      Each view goes to its pane on the sheet (frame centres of [78]).
//      View properties: those of the same plan in any other BASE, CEILING, WALL
//      drawing of the model (house style only, no geometry); none -> Tekla's
//      default properties at scale 1:15.
//   4. DIMENSIONS - the dimension engines:
//        base plan    -> WfBfDim (the P142_BF_AutoDimension engine)
//        ceiling plan -> WfCeilDim (rules from the 19 reference ceiling plans)
//        wall plan    -> WfCore.DimensionViewAioRun (WallFramingAutoDimensionPro-
//                        AIO: bays, bands, top outline, overall, north run,
//                        notes); its wall-panel marks and section marks are
//                        removed (not on the BP07 reference)
//      Part marks / weld marks: none - Tekla's automatic ones are removed (BP07).
//      Colours: the owner's colour code (WfIsoColours, as on the isometric):
//      HRS green, LGS grey, wall gypsum orange, ceiling gypsum violet, no
//      hidden lines.
//   NOT YET RULE-BASED: the base plan's S1..S5 section marks, notes and lifting
//   arrows (the next rule set - from the survey of the 19 finished drawings).
//
// SAFETY: it only writes into the open drawing; it never switches the active
// drawing; issued / frozen drawings are refused; it asks before rebuilding
// views it made itself (tagged P142_BCW=1); on a finished drawing it only
// offers the read-only ceiling study.  Never touches the model.
//
// TEKLA 2025 API (compiled against the 2025 DLLs): View(ContainerView, CS, CS,
// ArrayList ids) + Insert; View.Attributes / RestrictionBox / Name / Origin;
// WfIsometricCore.ColourParts / RemoveMarks (WF_Isometric.dll);
// View.ViewAttributes(); Part.GetSolid(); Model.GetModelObjectSelector().
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

        // the panes of the three plans: frame centres on the A1 sheet, measured on
        // BP02M-10001 [78] while it was the open drawing (2026-10-24)
        const double BASE_CX = 201.8, BASE_CY = 445.9;      // base HRS framing plan, top-left
        const double CEIL_CX = 570.7, CEIL_CY = 440.4;      // ceiling framing plan, top-right
        const double WALL_CX = 202.9, WALL_CY = 145.3;      // wall framing plan, bottom-left

        // the view boxes, relative to the levels read from the model (from [78])
        // around the main wall tracks; above the track tops (C63*40: +40), else Tekla
        // draws the cut 0.7 mm track flanges as nothing (BP20M run 2026-10-26)
        const double WALL_BELOW = 326, WALL_ABOVE = 300;
        const double CEIL_BELOW = 50;                       // view plane under the lowest ceiling part
        const double CEIL_HANG = 20;                        // gypsum under the ceiling beams
        const double MARGIN = 100;                          // plan margin of the boxes
        const double DEFAULT_SCALE = 15;
        const double POD_MAX = 15000;                       // a view box wider than this is not a pod plan

        const string ROLE_BASE = "BASE HRS FRAMING PLAN", ROLE_CEIL = "CEILING FRAMING PLAN", ROLE_WALL = "WALL FRAMING PLAN";

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

        /// <summary>The role of a plan of a finished BASE, CEILING, WALL drawing (as on [78]).</summary>
        static string RoleOf(View v)
        {
            CoordinateSystem vcs = v.ViewCoordinateSystem;
            AABB b = v.RestrictionBox;
            if (vcs != null && vcs.Origin.Z > 1000) return ROLE_CEIL;
            if (b != null && b.MinPoint.Z > -100) return ROLE_BASE;
            return ROLE_WALL;
        }

        // ------------------------------------------------------------ the model
        /// <summary>A part with its global extent.</summary>
        sealed class BPart
        {
            public TSM.Part Part;
            public string Name = "", Block = "", Profile = "";
            public int AsmId;
            public double[] Min = new double[3], Max = new double[3];
        }

        /// <summary>Every beam / polybeam / contour plate of the model, global extents.</summary>
        static List<BPart> ReadParts(TSM.Model model)
        {
            var res = new List<BPart>();
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
                        var bp = new BPart { Part = p, Name = (p.Name ?? "").ToUpperInvariant() };
                        try
                        {
                            TSM.Solid so = p.GetSolid();
                            bp.Min = new[] { so.MinimumPoint.X, so.MinimumPoint.Y, so.MinimumPoint.Z };
                            bp.Max = new[] { so.MaximumPoint.X, so.MaximumPoint.Y, so.MaximumPoint.Z };
                        }
                        catch (Exception) { continue; }
                        string prof = ""; p.GetReportProperty("PROFILE", ref prof); bp.Profile = prof ?? "";
                        TSM.Assembly asm = null;
                        try { asm = p.GetAssembly(); } catch (Exception) { }
                        string apos = "";
                        if (asm != null && !posOf.TryGetValue(asm.Identifier.ID, out apos))
                        {
                            apos = ""; asm.GetReportProperty("ASSEMBLY_POS", ref apos); posOf[asm.Identifier.ID] = apos ?? "";
                        }
                        apos = apos ?? "";
                        if (asm != null) bp.AsmId = asm.Identifier.ID;
                        int dash = apos.IndexOf('-');
                        bp.Block = WfRules.NormaliseBlock(dash > 0 ? apos.Substring(0, dash) : apos);
                        res.Add(bp);
                    }
                }
            }
            finally { wph.SetCurrentTransformationPlane(original); }
            return res;
        }

        /// <summary>An LGS wall part: wall part name, or LGS / wall-gypsum profile of the colour code.</summary>
        static bool IsLgs(BPart p)
        {
            if (WfRules.IsWallPart(p.Name)) return true;
            WfIsoColourRule r = WfIsoColours.Match(p.Name, p.Profile);
            return r != null && (r.Label == "LGS_GREY" || r.Label == "WALL_ORANGE");
        }

        /// <summary>One plan to create.</summary>
        sealed class Plan
        {
            public string Role, Name;
            public double Cx, Cy, Z0, ZMin, ZMax;     // view plane level; box z (relative to Z0)
            public List<BPart> Parts = new List<BPart>();
            public HashSet<int> Ids = new HashSet<int>();
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
                    "YES = READ-ONLY study: record the parts, colours, dimensions, texts, symbols and section marks of the\n" +
                    "three plans of EVERY BASE, CEILING, WALL drawing in WF_BCW_study.txt (nothing is written).\n" +
                    "NO / CANCEL = do nothing.",
                    "P142 Base, ceiling, wall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (st != DialogResult.Yes) { result.Ok = true; result.Message = "Nothing done."; return; }
                string file = WfCeilStudy.Run(new TSM.Model(), LogB);
                result.Ok = true;
                result.Message = "Study written (read only): " + file;
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

            // ---- 1. the pod
            string block = WfRules.NormaliseBlock(BlockOf(dr));
            var all = ReadParts(model);
            var own = all.FindAll(p => p.Block == block);
            if (own.Count == 0)
            {
                result.Ok = false; result.Message = "No part of block " + block + " (ASSEMBLY_POS " + block + "-...) was found in the model - nothing done."; return;
            }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in own) { x0 = Math.Min(x0, p.Min[0]); y0 = Math.Min(y0, p.Min[1]); x1 = Math.Max(x1, p.Max[0]); y1 = Math.Max(y1, p.Max[1]); }
            var pod = all.FindAll(p =>
            {
                double cx = (p.Min[0] + p.Max[0]) / 2, cy = (p.Min[1] + p.Max[1]) / 2;
                return cx >= x0 - 1 && cx <= x1 + 1 && cy >= y0 - 1 && cy <= y1 + 1;
            });
            Say("block " + block + ": " + own.Count + " part(s) of its own, " + pod.Count + " part(s) in its plan extent (" +
                F1(x0) + ", " + F1(y0) + ") - (" + F1(x1) + ", " + F1(y1) + ")");

            // ---- 2. the levels
            double baseTop = double.MaxValue, ceil = double.MaxValue;
            foreach (var p in pod)
            {
                if (p.Name == "BOTTOM_TRACK") baseTop = Math.Min(baseTop, p.Min[2]);
                if (p.Name == "CEILING BEAM") ceil = Math.Min(ceil, p.Min[2]);
            }
            if (baseTop == double.MaxValue || ceil == double.MaxValue)
            {
                result.Ok = false;
                result.Message = "Block " + block + " has no " + (baseTop == double.MaxValue ? "wall BOTTOM_TRACK" : "CEILING BEAM") +
                                 " - the levels cannot be found - nothing done.";
                return;
            }
            Say("levels: base top " + F1(baseTop) + " (lowest BOTTOM_TRACK), ceiling " + F1(ceil) + " (lowest CEILING BEAM)");

            var basePlan = new Plan { Role = ROLE_BASE, Name = "Plan 0", Cx = BASE_CX, Cy = BASE_CY, Z0 = 0 };
            var wallPlan = new Plan { Role = ROLE_WALL, Name = "Plan 0", Cx = WALL_CX, Cy = WALL_CY, Z0 = 0 };
            var ceilPlan = new Plan { Role = ROLE_CEIL, Name = "Plan @ T-LGS", Cx = CEIL_CX, Cy = CEIL_CY };
            foreach (var p in pod)
            {
                Plan pl = p.Min[2] >= ceil - CEIL_HANG ? ceilPlan : (IsLgs(p) ? wallPlan : basePlan);
                pl.Parts.Add(p);
                pl.Ids.Add(p.Part.Identifier.ID);
            }
            // the base plan shows the BASE FRAME assembly only (the one holding the
            // BASE_FRAME tubes) - not vanity posts, sheets, plywood (BP20M run 2026-10-26)
            var baseAsm = new HashSet<int>();
            foreach (var p in basePlan.Parts) if (p.Name == "BASE_FRAME" && p.AsmId != 0) baseAsm.Add(p.AsmId);
            if (baseAsm.Count > 0)
            {
                int before = basePlan.Parts.Count;
                basePlan.Parts = basePlan.Parts.FindAll(p => baseAsm.Contains(p.AsmId));
                basePlan.Ids = new HashSet<int>(); foreach (var p in basePlan.Parts) basePlan.Ids.Add(p.Part.Identifier.ID);
                Say("base plan: the base frame assembly only - " + basePlan.Parts.Count + " of " + before + " part(s)");
            }
            double bLow = double.MaxValue, bHigh = double.MinValue, cLow = double.MaxValue, cHigh = double.MinValue;
            foreach (var p in basePlan.Parts) { bLow = Math.Min(bLow, p.Min[2]); bHigh = Math.Max(bHigh, p.Max[2]); }
            foreach (var p in ceilPlan.Parts) { cLow = Math.Min(cLow, p.Min[2]); cHigh = Math.Max(cHigh, p.Max[2]); }
            basePlan.ZMin = (bLow == double.MaxValue ? baseTop - 150 : bLow) - 1; basePlan.ZMax = (bHigh == double.MinValue ? baseTop : bHigh) + 1;
            // the wall cut follows the MAIN wall tracks (the highest wall BOTTOM_TRACK):
            // a ledge wall sits lower (z 36 on BP20M) and would cut the main walls away
            double wallTrack = baseTop;
            foreach (var p in pod) if (p.Name == "BOTTOM_TRACK" && p.Min[2] < ceil - 500) wallTrack = Math.Max(wallTrack, p.Min[2]);
            // view depth: 50 mm under the underside of the lowest LGS bottom track (owner, 2026-10-26)
            double lowTrack = wallTrack;
            foreach (var p in pod) if (p.Name == "BOTTOM_TRACK" && p.Min[2] < ceil - 500) lowTrack = Math.Min(lowTrack, p.Min[2]);
            wallPlan.ZMin = lowTrack - 50; wallPlan.ZMax = wallTrack + WALL_ABOVE;
            Say("wall cut: main wall tracks at " + F1(wallTrack) + " -> box z " + F1(wallPlan.ZMin) + " .. " + F1(wallPlan.ZMax));
            ceilPlan.Z0 = cLow - CEIL_BELOW; ceilPlan.ZMin = 0; ceilPlan.ZMax = cHigh - ceilPlan.Z0 + 1;
            int gyp = ceilPlan.Parts.FindAll(p => p.Name.Contains("GYPSUM")).Count;
            Say("parts: base " + basePlan.Parts.Count + ", wall " + wallPlan.Parts.Count + ", ceiling " + ceilPlan.Parts.Count +
                " (" + gyp + " CEILING_GYPSUM)");

            // ---- 3. the views
            var style = StyleViews(dr);
            ContainerView sheet = dr.GetSheet();
            var made = new List<KeyValuePair<View, View>>();
            var centres = new List<double[]>();
            var plans = new List<Plan>();
            foreach (Plan pl in new[] { basePlan, ceilPlan, wallPlan })
            {
                if (pl.Parts.Count == 0) { Say("SKIP " + pl.Role + ": no part at that level"); continue; }
                try
                {
                    var ids = new ArrayList();
                    foreach (var p in pl.Parts) ids.Add(p.Part.Identifier);
                    var cs = new CoordinateSystem(new Point(0, 0, pl.Z0), new Vector(1, 0, 0), new Vector(0, 1, 0));
                    View v = new View(sheet, cs, cs, ids);
                    if (!v.Insert()) { Failed++; Say("FAIL " + pl.Role + ": Insert() returned false"); continue; }
                    v.SetUserProperty(TAG_PROP, TAG_VALUE);
                    View sv;
                    if (style.TryGetValue(pl.Role, out sv))
                    {
                        try { v.Attributes = sv.Attributes; Say("     properties of '" + sv.Name + "' (house style)"); }
                        catch (Exception ex) { Say("     properties not taken: " + ex.Message); }
                    }
                    else
                    {
                        var a = new View.ViewAttributes();
                        a.Scale = DEFAULT_SCALE;
                        v.Attributes = a;
                        Say("     no finished BASE, CEILING, WALL drawing in the model - default properties, 1:" + DEFAULT_SCALE);
                    }
                    v.Name = pl.Name;
                    v.RestrictionBox = new AABB(new Point(x0 - MARGIN, y0 - MARGIN, pl.ZMin), new Point(x1 + MARGIN, y1 + MARGIN, pl.ZMax));
                    v.Modify();
                    made.Add(new KeyValuePair<View, View>(v, v));
                    centres.Add(new[] { pl.Cx, pl.Cy });
                    plans.Add(pl);
                    Made++;
                    Say("OK   " + pl.Role + " ('" + pl.Name + "'): " + ids.Count + " part(s), view plane z " + F1(pl.Z0) +
                        ", box z " + F1(pl.ZMin) + " .. " + F1(pl.ZMax));
                }
                catch (Exception ex) { Failed++; Say("FAIL " + pl.Role + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
            dr.CommitChanges("P142 BCW views");
            PlaceAll(dr, made, centres, "after the views");

            // ---- Tekla's own part marks come late (in the background): remove them, wait, again
            int auto = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                System.Threading.Thread.Sleep(1500);
                int n = 0;
                foreach (var kv in made) n += RemoveAll(kv.Key, typeof(Mark), typeof(WeldMark), typeof(Weld));
                auto += n;
                if (n == 0 && pass > 0) break;
            }
            if (auto > 0) dr.CommitChanges("P142 BCW automatic marks removed");
            Say(auto + " automatic part mark(s), weld mark(s) and weld symbol(s) of Tekla removed");

            // ---- the colour code
            foreach (var kv in made)
            {
                var per = new Dictionary<string, int>();
                int n = 0;
                try { n = WfIsometricCore.ColourParts(kv.Key, model, per); }
                catch (Exception ex) { Say("     colours not applied: " + ex.Message); }
                var l = new List<string>(); foreach (var e in per) l.Add(e.Key + " " + e.Value);
                Say("colours '" + kv.Key.Name + "': " + n + " part(s)  " + string.Join(", ", l.ToArray()));
                if (plans[made.IndexOf(kv)].Role == ROLE_BASE)
                    Say("     base frame hidden lines on: " + HiddenLinesOn(kv.Key) + " part(s)");
            }
            dr.CommitChanges("P142 BCW colours");

            // ---- 4. dimensions: the dimension engines measure the new plans
            for (int i = 0; i < made.Count; i++)
            {
                View v = made[i].Key; string role = plans[i].Role;
                try { v.Select(); } catch (Exception) { }
                try
                {
                    if (role == ROLE_WALL)
                    {
                        var wr = new WfResult();
                        bool ok = WfCore.DimensionViewAioRun(v, dr, wr);
                        Say("DIM  " + role + " (AIO wall engine): " + wr.Made + " created, " + wr.Failed + " failed" + (ok ? "" : " - no wall panel found") +
                            (string.IsNullOrEmpty(wr.Message) ? "" : "  [" + wr.Message + "]"));
                        if (wr.Failed > 0) { Failed += wr.Failed; Say(Indent(wr.Log)); }
                        // BP07 reference: no wall-panel marks, no section marks on the wall plan
                        int rm = RemoveAll(v, typeof(Mark), typeof(SectionMark), typeof(WeldMark), typeof(Weld));
                        Say("     wall plan: " + rm + " panel / section / weld mark(s) removed (not on the reference)");
                    }
                    else
                    {
                        var bl = new StringBuilder(); int bf;
                        // base plan: the P142_BF_AutoDimension engine (owner, 2026-10-26)
                        int n = role == ROLE_CEIL ? WfCeilDim.DimensionView(v, dr, bl, plans[i].Ids, out bf)
                                                  : WfBfDim.DimensionView(v, dr, bl, plans[i].Ids, out bf);
                        Say("DIM  " + role + ": " + n + " created, " + bf + " failed");
                        Say(Indent(bl.ToString()));
                        Failed += bf;
                    }
                }
                catch (Exception ex) { Failed++; Say("FAIL dimensions of " + role + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
            dr.CommitChanges("P142 BCW dimensions");
            // Tekla keeps creating marks / welds in the background: one more sweep
            int late = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                System.Threading.Thread.Sleep(2000);
                int n = 0;
                for (int i = 0; i < made.Count; i++)
                    n += plans[i].Role == ROLE_WALL ? RemoveAll(made[i].Key, typeof(Mark), typeof(SectionMark), typeof(WeldMark), typeof(Weld))
                                                    : RemoveAll(made[i].Key, typeof(Mark), typeof(WeldMark), typeof(Weld));
                late += n;
                if (n > 0) dr.CommitChanges("P142 BCW late marks removed");
                else if (pass > 0) break;
            }
            Say(late + " late mark / weld object(s) removed");

            // ---- 5. the standard notes of each plan (house standard) + lifting arrows
            for (int i = 0; i < made.Count; i++)
            {
                View v = made[i].Key; string role = plans[i].Role;
                View sv; style.TryGetValue(role, out sv);
                var nl = new StringBuilder();
                try
                {
                    if (role == ROLE_WALL)
                    {
                        // the reference notes replace the AIO engine's own notes
                        int gone = WfCore.RemoveTagged<Text>(v, dr);
                        if (gone > 0) nl.AppendLine("     " + gone + " AIO note(s) replaced by the standard notes");
                    }
                    WfBcwNotes.CopyStandard(v, sv, model, nl);
                    if (role == ROLE_BASE) WfBcwNotes.LiftingArrows(v, sv, model, nl);
                }
                catch (Exception ex) { Failed++; nl.AppendLine("FAIL notes: " + ex.GetType().Name + ": " + ex.Message); }
                Say("NOTES " + role + ":"); Say(nl.ToString().TrimEnd());
            }
            dr.CommitChanges("P142 BCW standard notes");
            PlaceAll(dr, made, centres, "after the dimensions");

            if (save && Made > 0)
            {
                try { new DrawingHandler().SaveActiveDrawing(); Say("drawing saved"); }
                catch (Exception ex) { Failed++; Say("the drawing could not be saved: " + ex.Message); }
            }
            result.Ok = Failed == 0;
            result.Message = "BASE, CEILING, WALL on " + mark + " (" + block + "), built from its own parts: " +
                             Made + " view(s), " + Failed + " failure(s).";
            Say(result.Message);
        }

        /// <summary>
        /// The house style: for each plan, the view of the same role in another
        /// BASE, CEILING, WALL drawing of the model (read only, not opened).  Only
        /// its view PROPERTIES are used - no geometry, no part, no annotation.
        /// </summary>
        static Dictionary<string, View> StyleViews(Drawing current)
        {
            var res = new Dictionary<string, View>();
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            while (de.MoveNext() && res.Count < 3)
            {
                Drawing d = de.Current;
                if (d == null || d.Mark == current.Mark || !IsBcw(d)) continue;
                try
                {
                    foreach (View v in AllViews(d.GetSheet()))
                    {
                        AABB b = v.RestrictionBox;
                        // a pod plan, not the POD LOCATION PLAN of the whole floor
                        if (b == null || b.MaxPoint.X - b.MinPoint.X > POD_MAX || b.MaxPoint.Y - b.MinPoint.Y > POD_MAX) continue;
                        if (IsOurs(v)) continue;                     // a plan this macro made is not a standard
                        string r = RoleOf(v);
                        if (!res.ContainsKey(r)) { res[r] = v; Say("house style for " + r + ": '" + v.Name + "' of " + d.Mark + " " + d.Name + ", 1:" + v.Attributes.Scale); }
                    }
                }
                catch (Exception) { }
            }
            return res;
        }

        /// <summary>Turns the hidden and own hidden lines of every part of the view on (base plan, BP02M reference).</summary>
        static int HiddenLinesOn(View v)
        {
            int n = 0;
            DrawingObjectEnumerator en = v.GetAllObjects(typeof(Part));
            while (en.MoveNext())
            {
                Part dp = en.Current as Part;
                if (dp == null) continue;
                try
                {
                    dp.Attributes.DrawHiddenLines = true;
                    dp.Attributes.DrawOwnHiddenLines = true;
                    if (dp.Modify()) n++;
                }
                catch (Exception) { }
            }
            return n;
        }

        /// <summary>Deletes every object of the given drawing types in the view.</summary>
        static int RemoveAll(View v, params Type[] types)
        {
            var list = new List<DrawingObject>();
            DrawingObjectEnumerator en = v.GetAllObjects(types);
            while (en.MoveNext()) if (en.Current != null) list.Add(en.Current);
            int n = 0;
            foreach (DrawingObject o in list) { try { if (o.Delete()) n++; } catch (Exception) { } }
            return n;
        }

        static string Indent(string s) { return "     " + (s ?? "").Trim().Replace("\n", "\n     "); }

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
                Say("placed '" + v.Name + "' " + when + ": frame " + F1(v.Width) + " x " + F1(v.Height) + " centred at (" +
                    F1(v.Origin.X + (fo == null ? 0 : fo.X) + v.Width / 2.0) + ", " + F1(v.Origin.Y + (fo == null ? 0 : fo.Y) + v.Height / 2.0) + ")");
            }
        }
    }
}
