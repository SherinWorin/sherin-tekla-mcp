// WF_BfDim.cs - the base-frame dimension engine of P142_BF_AutoDimension as a
// library function: dimensions ONE given plan view (no active-drawing lookup,
// no message boxes).  P142_BASE_C_W calls it for the base HRS framing plan and
// the ceiling framing plan of a BASE, CEILING, WALL drawing.
//
// The rules, the STYLE strings and the paper offsets are the ones of the
// catalog script P142_BF_AutoDimension.cs (verified 2026-09-24 on BP4-BF.1);
// that script itself is unchanged.  API calls: the same as the script.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using Part = Tekla.Structures.Drawing.Part;

namespace P142.WallFraming
{
    /// <summary>The base-frame dimension engine (port of P142_BF_AutoDimension).</summary>
    public static class WfBfDim
    {
        const double TOL = 1.0;
        internal static readonly string[] STYLE = {
            "CombinedDimension.MinimumNumberToCombine=0","CombinedDimension.Format=Off","DimensionType=Relative",
            "ExtensionLine=No","ShortDimension=Outside","ExcludePartsAccordingToFilter=None",
            "Exaggeration.ExaggerationEnabled=False","Arrowhead.ArrowPosition=Both","Arrowhead.Head=NoArrow",
            "Arrowhead.Height=1","Arrowhead.Width=2.5","Color=NewLine6","TrueColor.DrawingColorEnum=NewLine6",
            "TransparentBackground=False","Format.Precision=Whole","Format.Format=NoDecimals","Format.Unit=Automatic",
            "Format.UseDigitGrouping=False","Placing.Placing=Fixed","Placing.Direction.Positive=True",
            "Placing.Direction.Negative=False","Placing.Distance.SearchMargin=1","Placing.Distance.MinimalDistance=15",
            "Placing.Distance.MaximalDistance=0","Text.Font.Color=Black","Text.Font.TrueColor.DrawingColorEnum=Black",
            "Text.Font.Height=2.75","Text.Font.Name=Arial narrow","Text.Font.Italic=False","Text.Font.Bold=False",
            "Text.TextPlacing=AboveDimensionLine" };

        // paper-mm offsets taken from BP4-BF.1
        const double D_CHAIN_BOTTOM = 10.91, D_CHAIN_TOP = 9.255, D_CHAIN_LEFT = 10.365, D_CHAIN_RIGHT = 11.125;
        const double D_TIER2 = 15.415, D_TIER2_RIGHT = 18.08;
        const double D_ALL_BOTTOM = 16.32, D_ALL_TOP = 20.64, D_ALL_LEFT = 16.86;
        const double D_STUB_COLLINEAR = 6.71, D_STUB_H = 6.93, D_STUB_H_TOTAL = 12.27, D_STUB_V = 5.24, D_STUB_V_TOTAL = 10.02;
        const double D_LEN_V = 17.28, D_LEN_H = 19.75, D_POS_H = 6.34, D_POS_V = 12.68, D_SHS_HSIDE = 10.54, D_SHS_VSIDE = 6.14;

        class Box { public double x0, y0, x1, y1; public string pos; public double W { get { return Math.Min(x1 - x0, y1 - y0); } }
            public double L { get { return Math.Max(x1 - x0, y1 - y0); } } public bool H { get { return (x1 - x0) >= (y1 - y0); } } }
        class Seg { public double a, b, ext; }

        static List<Box> parts = new List<Box>();
        static double T, X0, Y0, X1, Y1;
        static StringBuilder log = new StringBuilder();
        static int made = 0, failed = 0;

        // side: 0 bottom, 1 top, 2 left, 3 right
        static bool AlongX(int s) { return s < 2; }
        static int Sign(int s) { return (s == 1 || s == 3) ? 1 : -1; }
        static double A0(Box b, int s) { return AlongX(s) ? b.x0 : b.y0; }
        static double A1(Box b, int s) { return AlongX(s) ? b.x1 : b.y1; }
        static double Key(Box b, int s) { switch (s) { case 0: return b.y0; case 1: return b.y1; case 2: return b.x0; default: return b.x1; } }
        static double Ov(double a0, double a1, double b0, double b1) { return Math.Min(a1, b1) - Math.Max(a0, b0); }
        static bool Touch(Box a, Box b) { return Ov(a.x0, a.x1, b.x0, b.x1) >= -TOL && Ov(a.y0, a.y1, b.y0, b.y1) >= -TOL; }
        static Point Pt(int s, double along, double perp) { return AlongX(s) ? new Point(along, perp, 0) : new Point(perp, along, 0); }
        static Vector Up(int s) { switch (s) { case 0: return new Vector(0, -1, 0); case 1: return new Vector(0, 1, 0); case 2: return new Vector(-1, 0, 0); default: return new Vector(1, 0, 0); } }

        static List<Seg> Silhouette(int s) {
            List<double> xs = new List<double>();
            foreach (Box p in parts) { xs.Add(A0(p, s)); xs.Add(A1(p, s)); }
            xs.Sort();
            List<Seg> segs = new List<Seg>();
            for (int i = 0; i + 1 < xs.Count; i++) {
                double a = xs[i], b = xs[i + 1];
                if (b - a < TOL) continue;
                double m = (a + b) / 2; bool any = false; double e = 0;
                foreach (Box p in parts) if (A0(p, s) < m && m < A1(p, s)) {
                    double k = Key(p, s);
                    if (!any) { e = k; any = true; } else e = Sign(s) > 0 ? Math.Max(e, k) : Math.Min(e, k);
                }
                if (!any) continue;
                if (segs.Count > 0 && Math.Abs(segs[segs.Count - 1].ext - e) < TOL && Math.Abs(segs[segs.Count - 1].b - a) < TOL) segs[segs.Count - 1].b = b;
                else segs.Add(new Seg { a = a, b = b, ext = e });
            }
            return segs;
        }

        static int InBand(Box p) {
            for (int s = 0; s < 4; s++) foreach (Seg g in Silhouette(s))
                if (Ov(A0(p, s), A1(p, s), g.a, g.b) > TOL && Math.Abs(Key(p, s) - g.ext) < TOL) return s;
            return -1;
        }

        // side chain: along-value -> outermost perpendicular coordinate
        static SortedDictionary<double, double> SideChain(int s) {
            List<Seg> segs = Silhouette(s); var res = new SortedDictionary<double, double>();
            if (segs.Count == 0) return res;
            double span = segs[segs.Count - 1].b - segs[0].a;
            List<Seg> kept = segs.FindAll(g => g.b - g.a >= 0.35 * span);
            foreach (Box p in parts) foreach (Seg g in kept) {
                if (Ov(A0(p, s), A1(p, s), g.a, g.b) > TOL && Math.Abs(Key(p, s) - g.ext) <= T + TOL) {
                    foreach (double v in new double[] { A0(p, s), A1(p, s) }) {
                        if (v < g.a - TOL || v > g.b + TOL) continue;
                        double r = Math.Round(v, 1), k = Key(p, s), old;
                        if (!res.TryGetValue(r, out old) || (Sign(s) > 0 ? k > old : k < old)) res[r] = k;
                    }
                }
            }
            return res;
        }

        internal static PropertyInfo FindProp(Type t, string name) {
            PropertyInfo first = null;
            foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (pi.Name != name || pi.GetIndexParameters().Length > 0) continue;
                if (pi.DeclaringType == t) return pi;
                if (first == null) first = pi;
            }
            return first;
        }

        internal static void SetPath(object target, string path, string value) {
            string[] parts_ = path.Split('.');
            object[] chain = new object[parts_.Length]; chain[0] = target;
            PropertyInfo[] props = new PropertyInfo[parts_.Length];
            for (int i = 0; i < parts_.Length; i++) {
                props[i] = FindProp(chain[i].GetType(), parts_[i]);
                if (props[i] == null) return;
                if (i < parts_.Length - 1) { chain[i + 1] = props[i].GetValue(chain[i], null); if (chain[i + 1] == null) return; }
            }
            PropertyInfo last = props[parts_.Length - 1];
            if (!last.CanWrite) return;
            Type t = last.PropertyType; object v;
            if (t.IsEnum) v = Enum.Parse(t, value, true);
            else if (t == typeof(string)) v = value;
            else v = Convert.ChangeType(value, t, System.Globalization.CultureInfo.InvariantCulture);
            last.SetValue(chain[parts_.Length - 1], v, null);
            for (int i = parts_.Length - 2; i >= 0; i--)          // write back value-type holders
                if (props[i].CanWrite && props[i].PropertyType.IsValueType) props[i].SetValue(chain[i], chain[i + 1], null);
        }

        static void Dim(View view, Drawing dr, StraightDimensionSetHandler h, string what, List<Point> pts, Vector up, double paperDist) {
            PointList pl = new PointList(); List<string> vals = new List<string>();
            foreach (Point p in pts) pl.Add(p);
            try {
                StraightDimensionSet set = h.CreateDimensionSet(view, pl, up, paperDist * view.Attributes.Scale);
                if (set == null) { failed++; log.AppendLine("FAIL " + what); return; }
                dr.CommitChanges();
                object attr = set.Attributes;
                foreach (string kv in STYLE) {
                    int i = kv.IndexOf('=');
                    try { SetPath(attr, kv.Substring(0, i), kv.Substring(i + 1)); } catch (Exception e) { log.AppendLine("  style skip " + kv + " : " + e.Message); }
                }
                PropertyInfo ap = FindProp(set.GetType(), "Attributes");
                if (ap != null && ap.CanWrite) ap.SetValue(set, attr, null);
                set.Modify(); dr.CommitChanges();
                // re-apply the offset: modifying the style resets the dimension line position
                set.Distance = paperDist * view.Attributes.Scale;
                set.Modify(); dr.CommitChanges(); made++;
                StringBuilder s = new StringBuilder();
                foreach (Point p in pts) s.AppendFormat("({0:F1},{1:F1}) ", p.X, p.Y);
                log.AppendLine("OK   " + what + " : " + s);
            } catch (Exception e) { failed++; log.AppendLine("FAIL " + what + " : " + e.Message); }
        }


        /// <summary>
        /// Dimensions the given plan view the way P142_BF_AutoDimension does.
        /// When <paramref name="only"/> is not null only those model parts are
        /// measured (the ceiling plan also shows the gypsum, which is not framing).
        /// </summary>
        /// <returns>The number of dimension sets created.</returns>
        public static int DimensionView(View view, Drawing dr, StringBuilder outLog, HashSet<int> only, out int failedOut)
        {
            parts.Clear(); log.Length = 0; made = failed = 0; failedOut = 0;
            if (view == null || dr == null) return 0;
            // part outlines in view coordinates
            TSM.Model model = new TSM.Model(); TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try {
                CoordinateSystem cs = view.DisplayCoordinateSystem;
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(cs));
                DrawingObjectEnumerator pe = view.GetAllObjects(typeof(Part));
                while (pe.MoveNext()) {
                    Part dp = pe.Current as Part; if (dp == null) continue; if (only != null && !only.Contains(dp.ModelIdentifier.ID)) continue;
                    TSM.Part mp = model.SelectModelObject(new Identifier(dp.ModelIdentifier.ID)) as TSM.Part; if (mp == null) continue;
                    TSM.Solid so = mp.GetSolid(); string pos = ""; mp.GetReportProperty("PART_POS", ref pos);
                    parts.Add(new Box { x0 = so.MinimumPoint.X, y0 = so.MinimumPoint.Y, x1 = so.MaximumPoint.X, y1 = so.MaximumPoint.Y, pos = pos });
                }
            } finally { wph.SetCurrentTransformationPlane(original); }
            if (parts.Count < 4) { log.AppendLine("too few parts in the view (" + parts.Count + ") - no dimensions"); outLog.Append(log); return 0; }

            X0 = double.MaxValue; Y0 = double.MaxValue; X1 = double.MinValue; Y1 = double.MinValue;
            foreach (Box p in parts) { X0 = Math.Min(X0, p.x0); Y0 = Math.Min(Y0, p.y0); X1 = Math.Max(X1, p.x1); Y1 = Math.Max(Y1, p.y1); }
            Box post = parts.Find(p => Math.Abs(p.x0 - X0) < TOL);
            if (post == null) { log.AppendLine("no edge part - no dimensions"); outLog.Append(log); return 0; }
            T = post.W;
            log.AppendLine("Drawing " + dr.Mark + "  parts=" + parts.Count + "  extents " + X0.ToString("F1") + "," + Y0.ToString("F1") + " - " + X1.ToString("F1") + "," + Y1.ToString("F1") + "  band=" + T.ToString("F1"));

            var h = new StraightDimensionSetHandler();
            string[] names = { "bottom", "top", "left", "right" };
            double[] dChain = { D_CHAIN_BOTTOM, D_CHAIN_TOP, D_CHAIN_LEFT, D_CHAIN_RIGHT };
            double[] dAll = { D_ALL_BOTTOM, D_ALL_TOP, D_ALL_LEFT, 0 };
            var chains = new List<SortedDictionary<double, double>>();

            // ---- sides
            for (int s = 0; s < 4; s++) {
                var ch = SideChain(s); chains.Add(ch);
                var pts = new List<Point>(); foreach (var kv in ch) pts.Add(Pt(s, kv.Key, kv.Value));
                if (pts.Count >= 2) Dim(view, dr, h, names[s] + " chain", pts, Up(s), dChain[s]);
                List<Seg> segs = Silhouette(s);
                if (segs.Count >= 2) {
                    var t2 = new List<Point>(); t2.Add(Pt(s, segs[0].a, segs[0].ext));
                    for (int i = 0; i < segs.Count; i++) {
                        double e = segs[i].ext;
                        if (i + 1 < segs.Count) e = Sign(s) > 0 ? Math.Max(e, segs[i + 1].ext) : Math.Min(e, segs[i + 1].ext);
                        t2.Add(Pt(s, segs[i].b, e));
                    }
                    Dim(view, dr, h, names[s] + " tier2", t2, Up(s), s == 3 ? D_TIER2_RIGHT : D_TIER2);
                }
                if (dAll[s] > 0 && segs.Count > 0) {
                    var ov = new List<Point> { Pt(s, segs[0].a, segs[0].ext), Pt(s, segs[segs.Count - 1].b, segs[segs.Count - 1].ext) };
                    Dim(view, dr, h, names[s] + " overall", ov, Up(s), dAll[s]);
                }
            }

            // ---- classify inside parts
            var plates = new List<Box>(); var frames = new List<Box>(); var stubs = new List<Box>(); var bandSmall = new List<KeyValuePair<Box, int>>();
            foreach (Box p in parts) {
                if (p.W <= 5 + TOL) { plates.Add(p); continue; }
                int sd = InBand(p);
                if (p.W < T - TOL && sd >= 0) bandSmall.Add(new KeyValuePair<Box, int>(p, sd));
                else if (p.W < T - TOL) frames.Add(p);
                else if (p.L <= 4 * T && sd < 0) stubs.Add(p);
            }
            Func<double, bool> inAnyChain = v => { foreach (var c in chains) foreach (var k in c.Keys) if (Math.Abs(k - v) < TOL) return true; return false; };

            // SHS set in the perimeter band
            foreach (var kv in bandSmall) {
                Box p = kv.Key; int s = kv.Value; double outer = Key(p, s), inner = outer - Sign(s) * T;
                if (AlongX(s)) {   // top/bottom band: dims along Y at SHS start
                    double shsIn = s == 1 ? p.y0 : p.y1;
                    var pts = new List<Point> { new Point(p.x0, inner, 0), new Point(p.x0, shsIn, 0), new Point(p.x0, outer, 0) };
                    Dim(view, dr, h, "SHS set-back " + p.pos, pts, new Vector(1, 0, 0), D_SHS_HSIDE);
                } else {           // left/right band: dims along X at SHS top end
                    double shsIn = s == 3 ? p.x0 : p.x1;
                    var pts = new List<Point> { new Point(inner, p.y1, 0), new Point(shsIn, p.y1, 0), new Point(outer, p.y1, 0) };
                    Dim(view, dr, h, "SHS set-back " + p.pos, pts, new Vector(0, -1, 0), D_SHS_VSIDE);
                }
            }

            // stub + plate once per frame member
            var lenDone = new HashSet<Box>();
            foreach (Box m in frames) {
                Box best = null, bestPl = null; double bestD = double.MaxValue;
                foreach (Box st in stubs) {
                    Box pl = plates.Find(q => Touch(q, st) && Touch(q, m)); if (pl == null) continue;
                    double c = m.H ? (st.x0 + st.x1) / 2 : (st.y0 + st.y1) / 2;
                    if (st.H != m.H && (m.H ? (c < m.x0 - TOL || c > m.x1 + TOL) : (c < m.y0 - TOL || c > m.y1 + TOL))) continue; // a perpendicular stub must sit within the member span
                    double d = m.H ? Math.Min(Math.Abs(c - m.x0), Math.Abs(c - m.x1)) : Math.Min(Math.Abs(c - m.y0), Math.Abs(c - m.y1));
                    if (d < bestD) { bestD = d; best = st; bestPl = pl; }
                }
                if (best == null) continue;
                bool sh = best.H, collinear = (sh == m.H);
                var vals = new SortedSet<double>();
                if (sh) { vals.Add(best.x0); vals.Add(best.x1); vals.Add(bestPl.x0); vals.Add(bestPl.x1); if (collinear) { vals.Add(m.x0); vals.Add(m.x1); } }
                else { vals.Add(best.y0); vals.Add(best.y1); vals.Add(bestPl.y0); vals.Add(bestPl.y1); if (collinear) { vals.Add(m.y0); vals.Add(m.y1); } }
                var uniq = new List<double>();
                foreach (double v in vals) if (uniq.Count == 0 || v - uniq[uniq.Count - 1] > 0.1) uniq.Add(v);
                var pts = new List<Point>();
                foreach (double v in uniq) {
                    double perp = sh ? ((collinear && Math.Abs(v - m.x1) < TOL) ? m.y0 : best.y0) : best.x1;
                    pts.Add(sh ? new Point(v, perp, 0) : new Point(perp, v, 0));
                }
                if (sh) Dim(view, dr, h, "stub+plate " + best.pos + (collinear ? " + " + m.pos : ""), pts, new Vector(0, -1, 0), collinear ? D_STUB_COLLINEAR : D_STUB_H);
                else Dim(view, dr, h, "stub+plate " + best.pos, pts, new Vector(1, 0, 0), D_STUB_V);
                if (collinear) lenDone.Add(m);
                else {
                    var tot = new List<Point> { pts[0], pts[pts.Count - 1] };
                    Dim(view, dr, h, "stub+plate total " + best.pos, tot, sh ? new Vector(0, -1, 0) : new Vector(1, 0, 0), sh ? D_STUB_H_TOTAL : D_STUB_V_TOTAL);
                }
            }
            // frame member lengths
            foreach (Box m in frames) {
                if (lenDone.Contains(m)) continue;
                if (m.H) Dim(view, dr, h, "length " + m.pos, new List<Point> { new Point(m.x0, m.y1, 0), new Point(m.x1, m.y1, 0) }, new Vector(0, 1, 0), D_LEN_H);
                else Dim(view, dr, h, "length " + m.pos, new List<Point> { new Point(m.x1, m.y0, 0), new Point(m.x1, m.y1, 0) }, new Vector(1, 0, 0), D_LEN_V);
            }
            // frame position from the parallel inner face
            foreach (Box m in frames) {
                double near = m.H ? m.y0 : m.x0, far = m.H ? m.y1 : m.x1, inner = (m.H ? Y0 : X0) + T;
                if (inAnyChain(Math.Round(near, 1))) continue;
                var vals = new List<double> { inner, near }; if (!inAnyChain(Math.Round(far, 1))) vals.Add(far);
                var pts = new List<Point>();
                if (m.H) { double x = m.x0 + 0.6604 * (m.x1 - m.x0);   /* same station along the member as BP4-BF.1 */ foreach (double v in vals) pts.Add(new Point(x, v, 0)); Dim(view, dr, h, "position " + m.pos, pts, new Vector(1, 0, 0), D_POS_H); }
                else { foreach (double v in vals) pts.Add(new Point(v, m.y1, 0)); Dim(view, dr, h, "position " + m.pos, pts, new Vector(0, 1, 0), D_POS_V); }
            }


            log.AppendLine("Base-frame dimensions: " + made + " created, " + failed + " failed.");
            outLog.Append(log);
            failedOut = failed;
            return made;
        }
    }
}
