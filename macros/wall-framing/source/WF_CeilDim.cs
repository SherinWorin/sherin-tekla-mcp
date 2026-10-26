// WF_CeilDim.cs - the CEILING FRAMING PLAN dimension engine (Tekla Structures 2025).
//
// Rules learned on 2026-10-24 from the 19 hand-dimensioned ceiling framing
// plans "Plan @ T-LGS" of BP01 ... BP10M-10001 (WF_CeilDim_study.txt):
//
//   1. PANEL CHAINS   every LGS ceiling panel (assembly BP4-CP-n): panel edge |
//                     centre of every STUD / JAMBS (CC61) | panel edge, on the
//                     inner face of the panel's track nearest the frame edge,
//                     placed just outside the frame there.  Panels whose chains
//                     lie on the same line are one chain (BP08).
//   2. FRAME CHAINS   each side: both faces of every RHS ceiling beam on the
//                     outline of that side, and of every beam that runs into it
//                     (59 of 76 reference chains reproduced exactly).
//   3. OVERALL        each side: the outer faces of the frame.
//   4. GYPSUM         each free edge of each CEILING_GYPSUM board: board edge ->
//                     outer face of the frame beyond it (typically 96.5).
//   5. OPENINGS       the centres of the cuts in the gypsum (downlights,
//                     extract): one chain in X from the nearer frame side, one
//                     in Y from the nearer frame side.  (Rule 5 is the one rule
//                     not yet checked against the study - see the log.)
//   Order outwards on a side: panel chain, frame chain, overall - 6 mm apart.
//   Style: the reference sets (Relative, NewLine6) = the STYLE of WfBfDim.
//
// API (Tekla 2025, checked in the 2025.0 XML docs): StraightDimensionSetHandler.
// CreateDimensionSet(ViewBase, PointList, Vector, double); Part.GetSolid();
// Part.GetBooleans(); BooleanPart.OperativePart; WorkPlaneHandler.
// The Distance of a set is measured from its outermost point along the up
// vector (read off the reference sets: chain and overall of one side).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DPart = Tekla.Structures.Drawing.Part;

namespace P142.WallFraming
{
    /// <summary>The ceiling framing plan dimension engine.</summary>
    public static class WfCeilDim
    {
        const double TOL = 1.0;
        // paper mm
        const double P_PANEL = 6.0, P_STEP = 6.0, P_FRAME_MIN = 8.0, P_GYP = 3.0, P_OPEN = 8.0;
        const double GYP_REACH = 300.0, OPEN_MAX = 600.0;

        sealed class B
        {
            public double x0, y0, x1, y1;
            public string name = "", apos = "";
            public bool Vert { get { return (y1 - y0) > (x1 - x0); } }
        }

        static readonly List<B> Frame = new List<B>(), Gyp = new List<B>(), Panel = new List<B>();
        static readonly List<double[]> Openings = new List<double[]>();
        static StringBuilder log = new StringBuilder();
        static int made, failed;
        static double sc;

        // side: 0 bottom, 1 top, 2 left, 3 right
        static bool AlongX(int s) { return s < 2; }
        static int Sign(int s) { return (s == 1 || s == 3) ? 1 : -1; }
        static double A0(B b, int s) { return AlongX(s) ? b.x0 : b.y0; }
        static double A1(B b, int s) { return AlongX(s) ? b.x1 : b.y1; }
        static double Key(B b, int s) { switch (s) { case 0: return b.y0; case 1: return b.y1; case 2: return b.x0; default: return b.x1; } }
        static double Ov(double a0, double a1, double b0, double b1) { return Math.Min(a1, b1) - Math.Max(a0, b0); }
        static Point Pt(int s, double along, double perp) { return AlongX(s) ? new Point(along, perp, 0) : new Point(perp, along, 0); }
        static Vector Up(int s) { switch (s) { case 0: return new Vector(0, -1, 0); case 1: return new Vector(0, 1, 0); case 2: return new Vector(-1, 0, 0); default: return new Vector(1, 0, 0); } }
        static string[] SN = { "bottom", "top", "left", "right" };

        sealed class Seg { public double a, b, ext; }

        static List<Seg> Silhouette(List<B> boxes, int s)
        {
            var xs = new List<double>();
            foreach (B p in boxes) { xs.Add(A0(p, s)); xs.Add(A1(p, s)); }
            xs.Sort();
            var segs = new List<Seg>();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double a = xs[i], c = xs[i + 1];
                if (c - a < TOL) continue;
                double m = (a + c) / 2, e = 0; bool any = false;
                foreach (B p in boxes)
                    if (A0(p, s) < m && m < A1(p, s))
                    {
                        double k = Key(p, s);
                        if (!any) { e = k; any = true; } else e = Sign(s) > 0 ? Math.Max(e, k) : Math.Min(e, k);
                    }
                if (!any) continue;
                if (segs.Count > 0 && Math.Abs(segs[segs.Count - 1].ext - e) < TOL && Math.Abs(segs[segs.Count - 1].b - a) < TOL) segs[segs.Count - 1].b = c;
                else segs.Add(new Seg { a = a, b = c, ext = e });
            }
            return segs;
        }

        /// <summary>The outermost frame coordinate of side s over [a0, a1] (NaN when none).</summary>
        static double LocalExt(int s, double a0, double a1)
        {
            double e = double.NaN;
            foreach (Seg g in Silhouette(Frame, s))
                if (Ov(a0, a1, g.a, g.b) > -TOL)
                    e = double.IsNaN(e) ? g.ext : (Sign(s) > 0 ? Math.Max(e, g.ext) : Math.Min(e, g.ext));
            return e;
        }

        /// <summary>
        /// The outer face (in direction s) of the nearest frame beam beyond
        /// <paramref name="from"/> that crosses the line at <paramref name="at"/>
        /// (along the side); NaN when there is none.
        /// </summary>
        static double FrameFace(int s, double at, double from)
        {
            double best = double.NaN, bd = double.MaxValue;
            foreach (B b in Frame)
            {
                if (at < A0(b, s) - TOL || at > A1(b, s) + TOL) continue;
                double inner = Key(b, s ^ 1), d = Sign(s) * (inner - from);
                if (d < -TOL || d >= bd) continue;
                bd = d; best = Key(b, s);
            }
            return best;
        }

        static double GlobalExt(int s)
        {
            double e = double.NaN;
            foreach (B b in Frame) { double k = Key(b, s); e = double.IsNaN(e) ? k : (Sign(s) > 0 ? Math.Max(e, k) : Math.Min(e, k)); }
            return e;
        }

        // ---------------------------------------------------------------- dims
        /// <summary>Creates a set whose dimension line lies at <paramref name="line"/> (coordinate along up).</summary>
        static void DimAt(View view, Drawing dr, StraightDimensionSetHandler h, string what, List<Point> pts, int s, double line)
        {
            int sg = Sign(s); double ext = double.MinValue;
            foreach (Point p in pts) ext = Math.Max(ext, sg * (AlongX(s) ? p.Y : p.X));
            double dist = line - ext;
            if (dist < 1) dist = 1;
            Dim(view, dr, h, what, pts, Up(s), dist);
        }

        static void Dim(View view, Drawing dr, StraightDimensionSetHandler h, string what, List<Point> pts, Vector up, double dist)
        {
            if (pts.Count < 2) return;
            var pl = new PointList();
            foreach (Point p in pts) pl.Add(p);
            try
            {
                StraightDimensionSet set = h.CreateDimensionSet(view, pl, up, dist);
                if (set == null) { failed++; log.AppendLine("FAIL " + what); return; }
                dr.CommitChanges();
                object attr = set.Attributes;
                foreach (string kv in WfBfDim.STYLE)
                {
                    int i = kv.IndexOf('=');
                    try { WfBfDim.SetPath(attr, kv.Substring(0, i), kv.Substring(i + 1)); } catch (Exception) { }
                }
                PropertyInfo ap = WfBfDim.FindProp(set.GetType(), "Attributes");
                if (ap != null && ap.CanWrite) ap.SetValue(set, attr, null);
                set.Modify(); dr.CommitChanges();
                set.Distance = dist;           // modifying the style resets the line position
                set.Modify(); dr.CommitChanges();
                made++;
                var sb = new StringBuilder();
                foreach (Point p in pts) sb.AppendFormat("({0:F0},{1:F0}) ", p.X, p.Y);
                log.AppendLine("OK   " + what + " : " + sb);
            }
            catch (Exception e) { failed++; log.AppendLine("FAIL " + what + " : " + e.Message); }
        }

        static List<double> Uniq(List<double> v)
        {
            v.Sort();
            var u = new List<double>();
            foreach (double d in v) if (u.Count == 0 || d - u[u.Count - 1] > 0.5) u.Add(d);
            return u;
        }

        // ---------------------------------------------------------------- read
        static void Read(View view, HashSet<int> only) { Read(view, only, false); }

        /// <param name="allFrame">True for the base plan: every part is frame (HRS).</param>
        static void Read(View view, HashSet<int> only, bool allFrame)
        {
            Frame.Clear(); Gyp.Clear(); Panel.Clear(); Openings.Clear();
            var model = new TSM.Model();
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                DrawingObjectEnumerator pe = view.GetAllObjects(typeof(DPart));
                while (pe.MoveNext())
                {
                    DPart dp = pe.Current as DPart;
                    if (dp == null) continue;
                    if (only != null && !only.Contains(dp.ModelIdentifier.ID)) continue;
                    TSM.Part mp = model.SelectModelObject(new Identifier(dp.ModelIdentifier.ID)) as TSM.Part;
                    if (mp == null) continue;
                    TSM.Solid so;
                    try { so = mp.GetSolid(); } catch (Exception) { continue; }
                    string apos = ""; mp.GetReportProperty("ASSEMBLY_POS", ref apos);
                    var b = new B { x0 = so.MinimumPoint.X, y0 = so.MinimumPoint.Y, x1 = so.MaximumPoint.X, y1 = so.MaximumPoint.Y, name = (mp.Name ?? "").ToUpperInvariant(), apos = apos ?? "" };
                    if (allFrame || b.name == "CEILING BEAM") Frame.Add(b);
                    else if (b.name.Contains("GYPSUM"))
                    {
                        Gyp.Add(b);
                        try
                        {
                            TSM.ModelObjectEnumerator be = mp.GetBooleans();
                            while (be.MoveNext())
                            {
                                TSM.BooleanPart bp = be.Current as TSM.BooleanPart;
                                TSM.Part op = bp == null ? null : bp.OperativePart as TSM.Part;
                                if (op == null) continue;
                                TSM.Solid os = op.GetSolid();
                                double w = os.MaximumPoint.X - os.MinimumPoint.X, hh = os.MaximumPoint.Y - os.MinimumPoint.Y;
                                double cx = (os.MinimumPoint.X + os.MaximumPoint.X) / 2, cy = (os.MinimumPoint.Y + os.MaximumPoint.Y) / 2;
                                bool inside = cx > b.x0 + 20 && cx < b.x1 - 20 && cy > b.y0 + 20 && cy < b.y1 - 20;
                                log.AppendLine("     cut in " + apos + " " + (op.Name ?? "") + " centre (" + cx.ToString("F0") + "," + cy.ToString("F0") + ") size " + w.ToString("F0") + " x " + hh.ToString("F0") + (inside && w < OPEN_MAX && hh < OPEN_MAX ? "" : " - ignored"));
                                if (inside && w < OPEN_MAX && hh < OPEN_MAX) Openings.Add(new[] { cx, cy, Gyp.Count - 1 });
                            }
                        }
                        catch (Exception e) { log.AppendLine("     cuts of " + apos + " not read: " + e.Message); }
                    }
                    else Panel.Add(b);
                }
            }
            finally { wph.SetCurrentTransformationPlane(original); }
        }

        // ---------------------------------------------------------------- entry
        /// <summary>
        /// Dimensions the ceiling framing plan.  <paramref name="only"/> (model
        /// ids, may be null) limits the parts read; the gypsum is read anyway
        /// when it is in the view.
        /// </summary>
        /// <returns>The number of dimension sets created.</returns>
        public static int DimensionView(View view, Drawing dr, StringBuilder outLog, HashSet<int> only, out int failedOut)
        {
            log = new StringBuilder(); made = failed = 0; failedOut = 0;
            if (view == null || dr == null) return 0;
            sc = view.Attributes.Scale;
            Read(view, null);           // everything shown: frame, panels, gypsum
            log.Insert(0, "ceiling plan: " + Frame.Count + " frame beam(s), " + Panel.Count + " panel member(s), " + Gyp.Count + " gypsum board(s), " + Openings.Count + " opening(s)\r\n");
            if (Frame.Count < 2) { log.AppendLine("no ceiling frame - no dimensions"); outLog.Append(log); return 0; }
            var h = new StraightDimensionSetHandler();

            // ---- 1. panel chains
            var panelLine = new double[] { double.NaN, double.NaN, double.NaN, double.NaN };
            var chains = new List<KeyValuePair<int, KeyValuePair<double, List<double>>>>();   // side, (perp, values)
            var byPanel = new Dictionary<string, List<B>>();
            foreach (B b in Panel) { List<B> l; if (!byPanel.TryGetValue(b.apos, out l)) { l = new List<B>(); byPanel[b.apos] = l; } l.Add(b); }
            double FX0 = GlobalExt(2), FX1 = GlobalExt(3), FY0 = GlobalExt(0), FY1 = GlobalExt(1);
            var names = new List<string>(byPanel.Keys); names.Sort();
            foreach (string ap in names)
            {
                List<B> ps = byPanel[ap];
                var studs = ps.FindAll(b => b.name == "STUD" || b.name == "JAMBS");
                if (studs.Count == 0) continue;
                int nv = studs.FindAll(b => b.Vert).Count;
                bool vert = nv * 2 > studs.Count;                      // studs run along Y -> chain along X
                var tracks = ps.FindAll(b => b.name.Contains("TRACK"));
                var src = tracks.Count > 0 ? tracks : studs;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (B b in ps) { lo = Math.Min(lo, vert ? b.x0 : b.y0); hi = Math.Max(hi, vert ? b.x1 : b.y1); }
                double pLo = double.MaxValue, pHi = double.MinValue;   // track extent across the chain
                foreach (B b in src) { pLo = Math.Min(pLo, vert ? b.y0 : b.x0); pHi = Math.Max(pHi, vert ? b.y1 : b.x1); }
                int s = vert ? ((pLo - FY0) <= (FY1 - pHi) + 5 ? 0 : 1) : ((pLo - FX0) <= (FX1 - pHi) + 5 ? 2 : 3);
                // the inner face of the outermost track on that side
                double perp = Sign(s) < 0 ? pLo : pHi;
                foreach (B t in src)
                {
                    double outer = Key(t, s);
                    if (Math.Abs(outer - perp) < TOL) { perp = Sign(s) < 0 ? (vert ? t.y1 : t.x1) : (vert ? t.y0 : t.x0); break; }
                }
                var vals = new List<double> { lo, hi };
                foreach (B b in studs)
                {
                    if (b.Vert != vert) continue;
                    double c = vert ? (b.x0 + b.x1) / 2 : (b.y0 + b.y1) / 2;
                    if (c - lo > 25 && hi - c > 25) vals.Add(c);
                }
                vals = Uniq(vals);
                // one chain per line: merge with a chain of the same side on the same line
                bool merged = false;
                foreach (var ch in chains)
                    if (ch.Key == s && Math.Abs(ch.Value.Key - perp) < TOL) { ch.Value.Value.AddRange(vals); merged = true; log.AppendLine("     panel " + ap + " joins the chain on the same line"); break; }
                if (!merged) chains.Add(new KeyValuePair<int, KeyValuePair<double, List<double>>>(s, new KeyValuePair<double, List<double>>(perp, vals)));
            }
            foreach (var ch in chains)
            {
                int s = ch.Key; var vals = Uniq(ch.Value.Value);
                double local = LocalExt(s, vals[0], vals[vals.Count - 1]);
                if (double.IsNaN(local)) local = GlobalExt(s);
                double line = Sign(s) * local + P_PANEL * sc;
                var pts = new List<Point>();
                foreach (double v in vals) pts.Add(Pt(s, v, ch.Value.Key));
                DimAt(view, dr, h, "panel chain " + SN[s], pts, s, line);
                if (double.IsNaN(panelLine[s]) || line > panelLine[s]) panelLine[s] = line;
            }

            // ---- 2 + 3. frame chains and overall dimensions
            for (int s = 0; s < 4; s++)
            {
                List<Seg> segs = Silhouette(Frame, s);
                if (segs.Count == 0) continue;
                double T = double.MaxValue;
                foreach (B b in Frame) T = Math.Min(T, Math.Min(b.x1 - b.x0, b.y1 - b.y0));
                var res = new SortedDictionary<double, double>();
                foreach (B p in Frame)
                    foreach (Seg g in segs)
                        if (Ov(A0(p, s), A1(p, s), g.a, g.b) > -TOL && Math.Abs(Key(p, s) - g.ext) <= T + TOL)
                            foreach (double v in new[] { A0(p, s), A1(p, s) })
                            {
                                if (v < g.a - TOL || v > g.b + TOL) continue;
                                double r = Math.Round(v, 1), k = Key(p, s), old;
                                if (!res.TryGetValue(r, out old) || (Sign(s) > 0 ? k > old : k < old)) res[r] = k;
                            }
                double frameLine = Sign(s) * GlobalExt(s) + P_FRAME_MIN * sc;
                if (!double.IsNaN(panelLine[s])) frameLine = Math.Max(frameLine, panelLine[s] + P_STEP * sc);
                var pts = new List<Point>();
                foreach (var kv in res) pts.Add(Pt(s, kv.Key, kv.Value));
                if (pts.Count > 2) DimAt(view, dr, h, "frame chain " + SN[s], pts, s, frameLine);
                else frameLine -= P_STEP * sc;
                var ov = new List<Point> { Pt(s, segs[0].a, segs[0].ext), Pt(s, segs[segs.Count - 1].b, segs[segs.Count - 1].ext) };
                DimAt(view, dr, h, "overall " + SN[s], ov, s, frameLine + P_STEP * sc);
            }

            // ---- 4. gypsum edge -> outer face of the frame (once per line: boards
            //         in a row share their top / bottom offset - BP08 ... BP10)
            var gypSeen = new HashSet<string>();
            foreach (B g in Gyp)
                for (int s = 0; s < 4; s++)
                {
                    double e = Key(g, s), a0 = A0(g, s), a1 = A1(g, s);
                    double at = a0 + 0.35 * (a1 - a0);
                    // a neighbouring board beyond this edge: shared edge, not dimensioned
                    bool shared = false;
                    foreach (B o in Gyp)
                        if (o != g && Math.Abs(Key(o, s ^ 1) - e) < 30 && Ov(A0(o, s), A1(o, s), a0, a1) > 10) shared = true;
                    if (shared) continue;
                    double ext = LocalExt(s, at - 1, at + 1);
                    if (double.IsNaN(ext)) continue;
                    double gap = Sign(s) * (ext - e);
                    if (gap <= TOL || gap > GYP_REACH) continue;
                    if (!gypSeen.Add(s + "|" + Math.Round(e) + "|" + Math.Round(ext))) continue;
                    var pts = new List<Point> { Pt(s, at, e), Pt(s, at, ext) };
                    // measuring X -> up (0,-1); measuring Y -> up (1,0)  (as on the references)
                    Dim(view, dr, h, "gypsum " + g.apos + " " + SN[s] + " " + gap.ToString("F1"), pts, AlongX(s) ? new Vector(1, 0, 0) : new Vector(0, -1, 0), P_GYP * sc);
                }

            // ---- 5. openings in the gypsum - per board (one room each), from the
            //         nearest frame face (BP07-10001: 482/383/415 + 360/187/584 ...)
            for (int gi = 0; gi < Gyp.Count; gi++)
            {
                var ops = Openings.FindAll(o => (int)o[2] == gi);
                if (ops.Count == 0) continue;
                B g = Gyp[gi];
                double cx = 0, cy = 0, minY = double.MaxValue, maxX = double.MinValue;
                foreach (var o in ops) { cx += o[0]; cy += o[1]; minY = Math.Min(minY, o[1]); maxX = Math.Max(maxX, o[0]); }
                cx /= ops.Count; cy /= ops.Count;
                int sx = (cx - g.x0) <= (g.x1 - cx) ? 2 : 3, sy = (cy - g.y0) <= (g.y1 - cy) ? 0 : 1;
                double fx = FrameFace(sx, cy, sx == 2 ? g.x0 : g.x1), fy = FrameFace(sy, cx, sy == 0 ? g.y0 : g.y1);
                var xs = new List<double>(); var ys = new List<double>();
                if (!double.IsNaN(fx)) xs.Add(fx);
                if (!double.IsNaN(fy)) ys.Add(fy);
                foreach (var o in ops) { xs.Add(o[0]); ys.Add(o[1]); }
                xs = Uniq(xs); ys = Uniq(ys);
                var px = new List<Point>(); foreach (double x in xs) px.Add(new Point(x, minY, 0));
                var py = new List<Point>(); foreach (double y in ys) py.Add(new Point(maxX, y, 0));
                Dim(view, dr, h, "openings X " + g.apos, px, new Vector(0, -1, 0), P_OPEN * sc);
                Dim(view, dr, h, "openings Y " + g.apos, py, new Vector(1, 0, 0), P_OPEN * sc);
            }

            log.AppendLine("Ceiling dimensions: " + made + " created, " + failed + " failed.");
            outLog.Append(log);
            failedOut = failed;
            return made;
        }

        // ================================================================ BASE PLAN
        /// <summary>
        /// The BASE HRS FRAMING PLAN of a BASE, CEILING, WALL drawing, by the rules
        /// read off BP07-10001 (2026-10-26):
        ///   MEMBER CHAIN  an inner member (not on the outline) within 300 mm of a
        ///                 side: outline segment end | member ends | segment end,
        ///                 just outside the frame (684/856/129, 130/956/134)
        ///   OUTLINE       each side: the steps of the outer faces (1769/1000,
        ///                 450/550/1219/550, 1669/1250/250, 500/2419/250)
        ///   OVERALL       each side (2769, 3169)
        ///   INNER MEMBER  further inside: the members on one line end to end
        ///                 (474/150/471) + the offset from the frame face (122)
        /// The tube thicknesses (the 60 / 20 steps) are NOT dimensioned.
        /// </summary>
        /// <returns>The number of dimension sets created.</returns>
        public static int DimensionBase(View view, Drawing dr, StringBuilder outLog, HashSet<int> only, out int failedOut)
        {
            log = new StringBuilder(); made = failed = 0; failedOut = 0;
            if (view == null || dr == null) return 0;
            sc = view.Attributes.Scale;
            Read(view, only, true);
            log.Insert(0, "base plan: " + Frame.Count + " HRS part(s)\r\n");
            if (Frame.Count < 2) { log.AppendLine("no base frame - no dimensions"); outLog.Append(log); return 0; }
            var h = new StraightDimensionSetHandler();

            // the outline members: they set the silhouette of a side somewhere
            var outline = new HashSet<B>();
            var segsOf = new List<Seg>[4];
            for (int s = 0; s < 4; s++)
            {
                segsOf[s] = Silhouette(Frame, s);
                foreach (B b in Frame)
                    foreach (Seg g in segsOf[s])
                        if (Ov(A0(b, s), A1(b, s), g.a, g.b) > TOL && Math.Abs(Key(b, s) - g.ext) < TOL) outline.Add(b);
            }

            // ---- inner members: to the nearest side (member chain) or inside
            var sideVals = new List<double>[4];
            var sidePerp = new double[4];
            for (int s = 0; s < 4; s++) { sideVals[s] = new List<double>(); sidePerp[s] = double.NaN; }
            var inner = new List<B>();
            foreach (B m in Frame)
            {
                if (outline.Contains(m)) continue;
                bool horiz = !m.Vert;
                int best = -1; double bd = double.MaxValue;
                foreach (int s in horiz ? new[] { 0, 1 } : new[] { 2, 3 })
                {
                    double ext = LocalExt(s, A0(m, s), A1(m, s));
                    if (double.IsNaN(ext)) continue;
                    double d = Sign(s) * (ext - Key(m, s));
                    if (d >= -TOL && d < bd) { bd = d; best = s; }
                }
                if (best < 0 || bd > 300) { inner.Add(m); continue; }
                double mid = (A0(m, best) + A1(m, best)) / 2;
                foreach (Seg g in segsOf[best])
                    if (mid >= g.a - TOL && mid <= g.b + TOL) { sideVals[best].Add(g.a); sideVals[best].Add(g.b); break; }
                sideVals[best].Add(A0(m, best)); sideVals[best].Add(A1(m, best));
                double k = Key(m, best);
                sidePerp[best] = double.IsNaN(sidePerp[best]) ? k : (Sign(best) > 0 ? Math.Max(sidePerp[best], k) : Math.Min(sidePerp[best], k));
            }

            for (int s = 0; s < 4; s++)
            {
                List<Seg> segs = segsOf[s];
                if (segs.Count == 0) continue;
                double level = Sign(s) * GlobalExt(s);
                double memberLine = double.NaN;
                if (sideVals[s].Count > 0)
                {
                    var vals = Uniq(sideVals[s]);
                    double local = LocalExt(s, vals[0], vals[vals.Count - 1]);
                    memberLine = Sign(s) * (double.IsNaN(local) ? GlobalExt(s) : local) + P_PANEL * sc;
                    var pts = new List<Point>();
                    foreach (double v in vals) pts.Add(Pt(s, v, sidePerp[s]));
                    DimAt(view, dr, h, "member chain " + SN[s], pts, s, memberLine);
                }
                double outlineLine = level + P_FRAME_MIN * sc;
                if (!double.IsNaN(memberLine)) outlineLine = Math.Max(outlineLine, memberLine + P_STEP * sc);
                if (segs.Count >= 2)
                {
                    var t2 = new List<Point> { Pt(s, segs[0].a, segs[0].ext) };
                    for (int i = 0; i < segs.Count; i++)
                    {
                        double e = segs[i].ext;
                        if (i + 1 < segs.Count) e = Sign(s) > 0 ? Math.Max(e, segs[i + 1].ext) : Math.Min(e, segs[i + 1].ext);
                        t2.Add(Pt(s, segs[i].b, e));
                    }
                    DimAt(view, dr, h, "outline " + SN[s], t2, s, outlineLine);
                }
                else outlineLine -= P_STEP * sc;
                var ov = new List<Point> { Pt(s, segs[0].a, segs[0].ext), Pt(s, segs[segs.Count - 1].b, segs[segs.Count - 1].ext) };
                DimAt(view, dr, h, "overall " + SN[s], ov, s, outlineLine + P_STEP * sc);
            }

            // ---- inner members: end to end along their line + offset from the frame
            var done = new HashSet<B>();
            foreach (B m in inner)
            {
                if (done.Contains(m)) continue;
                bool vert = m.Vert;
                double c = vert ? (m.x0 + m.x1) / 2 : (m.y0 + m.y1) / 2;
                var row = inner.FindAll(o => o.Vert == vert && Math.Abs((vert ? (o.x0 + o.x1) / 2 : (o.y0 + o.y1) / 2) - c) < 5);
                var vals = new List<double>();
                foreach (B o in row) { done.Add(o); vals.Add(vert ? o.y0 : o.x0); vals.Add(vert ? o.y1 : o.x1); }
                vals = Uniq(vals);
                var pts = new List<Point>();
                foreach (double v in vals) pts.Add(vert ? new Point(m.x1, v, 0) : new Point(v, m.y1, 0));
                Dim(view, dr, h, "inner member " + (vert ? "x=" : "y=") + c.ToString("F0"), pts, vert ? new Vector(1, 0, 0) : new Vector(0, 1, 0), P_GYP * sc);
                // offset: member outer face -> the nearest parallel frame face on its outer side
                int so = vert ? ((m.x0 + m.x1) / 2 - GlobalExt(2) <= GlobalExt(3) - (m.x0 + m.x1) / 2 ? 2 : 3)
                              : ((m.y0 + m.y1) / 2 - GlobalExt(0) <= GlobalExt(1) - (m.y0 + m.y1) / 2 ? 0 : 1);
                double at = (A0(m, so) + A1(m, so)) / 2;
                double face = FrameFace(so, at, Key(m, so));
                if (!double.IsNaN(face))
                {
                    var op = new List<Point> { Pt(so, at, Key(m, so)), Pt(so, at, face) };
                    Dim(view, dr, h, "inner offset " + SN[so], op, AlongX(so) ? new Vector(1, 0, 0) : new Vector(0, -1, 0), P_GYP * sc);
                }
            }

            log.AppendLine("Base dimensions: " + made + " created, " + failed + " failed.");
            outLog.Append(log);
            failedOut = failed;
            return made;
        }
    }
}
