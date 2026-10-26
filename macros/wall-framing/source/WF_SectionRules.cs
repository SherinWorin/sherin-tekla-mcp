// WF_SectionRules.cs - the rules of the SECTIONAL ELEVATION macro, with NO Tekla dependency.
//
// Compiled into WF_SectionRules.dll and unit-tested without Tekla
// (tests/test_section_rules - see docs/NEW_MACROS_README.md).
//
// Derived on 2026-10-24 from the reference sectional elevation [98]
// BP02M-20001 and its BASE, CEILING, WALL drawing [78] BP02M-10001
// (model RIV-MAR-3-R00), read with the read-only study edition of the macro:
//   * every section view Sk sits on the section mark Sk of the block's
//     BASE, CEILING, WALL drawing: view X axis = mark left -> right point,
//     view Y axis = global Z, restriction box along X = the mark's length;
//   * the depth of each view is a per-view choice (below);
//   * almost every annotation is at a fixed offset from the left or right
//     wall end of its view, or from the part its leader points at.
using System;
using System.Collections.Generic;

namespace P142.WallFraming
{
    /// <summary>One section view of the layout.</summary>
    public sealed class WfSectionPane
    {
        /// <summary>Section name (S1 ... S5) - the section mark name.</summary>
        public string Name;
        /// <summary>Centre of the view frame on an A1 sheet (paper mm), measured on [98].</summary>
        public double CentreX, CentreY;
        /// <summary>Restriction box depth behind (negative) and in front of the cut, model mm, from [98].</summary>
        public double DepthBack, DepthFront;
    }

    /// <summary>The rules of the SECTIONAL ELEVATION macro.</summary>
    public static class WfSectionRules
    {
        /// <summary>The version of the rules interface.</summary>
        public const int ApiVersion = 2;

        /// <summary>
        /// The five sections: frame centres on the A1 sheet (S1 top-left, S2
        /// top-right, S3 / S4 / S5 bottom left / middle / right, matching the
        /// template's panes) and the depth of each view, both from [98].
        /// </summary>
        public static readonly WfSectionPane[] Panes =
        {
            new WfSectionPane { Name = "S1", CentreX = 183.4, CentreY = 434.1, DepthBack = -1203.658, DepthFront = 48.046 },
            new WfSectionPane { Name = "S2", CentreX = 562.3, CentreY = 434.1, DepthBack = -1141.360, DepthFront = 58.233 },
            new WfSectionPane { Name = "S3", CentreX = 148.4, CentreY = 158.2, DepthBack = -1637.917, DepthFront = 58.950 },
            new WfSectionPane { Name = "S4", CentreX = 398.5, CentreY = 158.2, DepthBack = -1410.019, DepthFront = 4.728 },
            new WfSectionPane { Name = "S5", CentreX = 626.6, CentreY = 158.2, DepthBack = -342.845, DepthFront = 75.595 },
        };

        /// <summary>The pane of a section name, or null.</summary>
        public static WfSectionPane PaneOf(string name)
        {
            foreach (var p in Panes)
                if (string.Equals(p.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>Reference drawings to prefer, in order (the owner's choice).</summary>
        public static readonly string[] PreferredReferences = { "BP02M-20001" };

        /// <summary>Title fragment of the drawing that carries the section marks.</summary>
        public const string MarksDrawingTitle = "BASE, CEILING, WALL";

        /// <summary>Title fragment of a sectional elevation drawing.</summary>
        public const string SectionTitle = "SECTIONAL ELEVATION";

        /// <summary>
        /// A point near a wall end (closer than this, model mm) keeps its
        /// distance from that end; points in between are scaled.
        /// </summary>
        public const double EndAnchor = 450.0;

        /// <summary>
        /// Maps an x coordinate (view mm, along the wall) from the reference view
        /// (wall ends rx0..rx1) to the new view (tx0..tx1).
        /// </summary>
        public static double MapX(double x, double rx0, double rx1, double tx0, double tx1)
        {
            if (x >= rx1 - EndAnchor) return tx1 + (x - rx1);
            if (x <= rx0 + EndAnchor) return tx0 + (x - rx0);
            double rw = rx1 - rx0, tw = tx1 - tx0;
            if (rw <= 1) return tx0 + (x - rx0);
            // the middle part is scaled between the two anchored bands
            double r0 = rx0 + EndAnchor, r1 = rx1 - EndAnchor;
            double t0 = tx0 + EndAnchor, t1 = tx1 - EndAnchor;
            if (r1 - r0 <= 1 || t1 <= t0) return tx0 + (x - rx0) * tw / rw;
            return t0 + (x - r0) * (t1 - t0) / (r1 - r0);
        }

        /// <summary>
        /// The part name a note's leader must touch, from the note text, or null
        /// when the note is not tied to a part (it is then only moved).
        /// </summary>
        public static string TargetPartOf(string noteText)
        {
            string t = (noteText ?? "").ToUpperInvariant();
            if (t.Contains("CISTERN")) return "PLATE";
            if (t.Contains("CEILING HRS")) return "CEILING BEAM";
            if (t.Contains("CEILING LGS")) return null;
            if (t.Contains("BASE HRS")) return "BASE_FRAME";
            if (t.Contains("TOP TRACK")) return "TOP_TRACK";
            if (t.Contains("BOTTOM TRACK")) return "BOTTOM_TRACK";
            if (t.Contains("MIDDLE")) return "MIDDLE_TRACK";
            if (t.Contains("VERTICAL STUD")) return "STUD";
            if (t.Contains("GYPSUM")) return "GYPSUM_BOARD";
            return null;
        }

        /// <summary>True for the level texts (T.O.S, FFL, SSL): moved with the right wall end.</summary>
        public static bool IsLevelText(string noteText)
        {
            string t = (noteText ?? "").Trim().ToUpperInvariant();
            return t == "T.O.S" || t == "FFL" || t == "SSL";
        }

        /// <summary>
        /// Orders the points of a dimension chain along its measuring direction
        /// (perpendicular to the up direction) and merges points closer than
        /// <paramref name="tol"/> along that direction.  Points are (x, y) pairs.
        /// </summary>
        public static List<double[]> ChainPoints(List<double[]> pts, double upX, double upY, double tol)
        {
            // measuring direction = up rotated by -90 degrees
            double dx = upY, dy = -upX;
            var list = new List<double[]>(pts);
            list.Sort(delegate (double[] a, double[] b)
            {
                return (a[0] * dx + a[1] * dy).CompareTo(b[0] * dx + b[1] * dy);
            });
            var res = new List<double[]>();
            foreach (var p in list)
            {
                if (res.Count > 0)
                {
                    var q = res[res.Count - 1];
                    if (Math.Abs((p[0] - q[0]) * dx + (p[1] - q[1]) * dy) < tol) continue;
                }
                res.Add(p);
            }
            return res;
        }

        /// <summary>
        /// Snaps a value to the nearest candidate within <paramref name="tol"/>;
        /// returns the value unchanged when none is that close.
        /// </summary>
        public static double Snap(double v, IEnumerable<double> candidates, double tol)
        {
            double best = v, bd = tol;
            foreach (double c in candidates)
            {
                double d = Math.Abs(c - v);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------ where the sections cut
        // Learned 2026-10-26 from the S marks of the 19 BASE, CEILING, WALL drawings of
        // RIV-MAR-3-R00 (BP01 ... BP10M, built independently - no twin is read):
        //   * every pod gets four "inside-out" sections, one looking at each outer
        //     wall line (-x, +x, -y, +y);
        //   * plus one per full-length partition (an inner wall line spanning >= 55 %
        //     of the pod, > 200 mm from the outer lines; lines closer than 300 mm
        //     are one partition), seen from the larger room;
        //   * a pod without partitions, with a ledge wall (-LW) that has returns
        //     along y: one more cut on the ledge, looking at the nearest inner wall.
        // 16 of 19 reference plans match exactly, the other 3 differ by one extra
        // cut or its direction.

        /// <summary>A wall line (bottom track, global plan mm).</summary>
        public sealed class WallLine
        {
            /// <summary>Start / end.</summary>
            public double X0, Y0, X1, Y1;
            /// <summary>True for a ledge (vanity) wall - assembly position ...-LWn.</summary>
            public bool Ledge;
        }

        /// <summary>One section cut, its mark line and the wall it looks at.</summary>
        public sealed class Cut
        {
            /// <summary>S1, S2 ...</summary>
            public string Name;
            /// <summary>'x' (the cut looks along x) or 'y'.</summary>
            public char Axis;
            /// <summary>+1 / -1: the look direction along the axis.</summary>
            public int Look;
            /// <summary>The viewed wall line (along the axis) and the mark line position.</summary>
            public double Target, At;
            /// <summary>The section mark: left point -> right point (look = left->right turned +90 degrees).</summary>
            public double LeftX, LeftY, RightX, RightY;
            /// <summary>Distance from the mark line to the viewed wall line.</summary>
            public double Ahead { get { return Math.Abs(Target - At); } }
        }

        /// <summary>The mark line runs past the pod by this much at both ends.</summary>
        public const double MarkOverhang = 80;

        /// <summary>
        /// The section cuts of a pod from its wall lines and plan extent (global).
        /// Always at least four; named S1 ... in the reference order (the axis of
        /// the extra cut first, its outer cut S1; then -, + of the other axis; extras last).
        /// </summary>
        public static List<Cut> SectionCuts(IList<WallLine> walls, double ex0, double ey0, double ex1, double ey1)
        {
            var outer = new List<Cut>();
            var extra = new List<Cut>();
            foreach (char ax in new[] { 'x', 'y' })
            {
                // walls across the axis: for 'x' the walls running along y
                var perp = new List<double[]>();     // c, lo, hi
                foreach (var w in walls)
                {
                    bool alongY = Math.Abs(w.X1 - w.X0) < Math.Abs(w.Y1 - w.Y0);
                    if ((ax == 'x') != alongY) continue;
                    perp.Add(ax == 'x' ? new[] { (w.X0 + w.X1) / 2, Math.Min(w.Y0, w.Y1), Math.Max(w.Y0, w.Y1) }
                                       : new[] { (w.Y0 + w.Y1) / 2, Math.Min(w.X0, w.X1), Math.Max(w.X0, w.X1) });
                }
                double e0 = ax == 'x' ? ex0 : ey0, e1 = ax == 'x' ? ex1 : ey1;
                double cmin = e0, cmax = e1;
                if (perp.Count > 0)
                {
                    cmin = double.MaxValue; cmax = double.MinValue;
                    foreach (var p in perp) { cmin = Math.Min(cmin, p[0]); cmax = Math.Max(cmax, p[0]); }
                }
                double span = ax == 'x' ? ey1 - ey0 : ex1 - ex0;
                var inner = new List<double>();
                foreach (var p in perp)
                    if (p[0] - cmin > 200 && cmax - p[0] > 200 && p[2] - p[1] >= 0.55 * span) inner.Add(Math.Round(p[0]));
                inner.Sort();
                var parts = new List<double>();
                foreach (double c in inner) if (parts.Count == 0 || c - parts[parts.Count - 1] > 300) parts.Add(c);
                var bounds = new List<double> { cmin }; bounds.AddRange(parts); bounds.Add(cmax);

                outer.Add(new Cut { Axis = ax, Look = -1, Target = cmin, At = cmin + Front(bounds[1] - cmin, false) });
                outer.Add(new Cut { Axis = ax, Look = +1, Target = cmax, At = cmax - Front(cmax - bounds[bounds.Count - 2], false) });
                for (int i = 0; i < parts.Count; i++)
                {
                    double c = parts[i], left = c - bounds[i], right = bounds[i + 2] - c;
                    // seen from the larger room
                    if (right >= left) extra.Add(new Cut { Axis = ax, Look = -1, Target = c, At = c + Front(right, true) });
                    else extra.Add(new Cut { Axis = ax, Look = +1, Target = c, At = c - Front(left, true) });
                }
            }
            if (extra.Count == 0) extra.AddRange(LedgeCut(walls));

            // order: the axis of the first extra first, its outer cut with the same look as S1
            char first = extra.Count > 0 ? extra[0].Axis : 'x';
            int firstLook = extra.Count > 0 ? extra[0].Look : +1;
            var res = new List<Cut>();
            res.Add(outer.Find(c => c.Axis == first && c.Look == firstLook));
            res.Add(outer.Find(c => c.Axis == first && c.Look == -firstLook));
            res.Add(outer.Find(c => c.Axis != first && c.Look == -1));
            res.Add(outer.Find(c => c.Axis != first && c.Look == +1));
            res.AddRange(extra);
            for (int i = 0; i < res.Count; i++)
            {
                Cut c = res[i];
                c.Name = "S" + (i + 1);
                double lo = (c.Axis == 'x' ? ey0 : ex0) - MarkOverhang, hi = (c.Axis == 'x' ? ey1 : ex1) + MarkOverhang;
                // left -> right turned +90 degrees is the look direction
                if (c.Axis == 'x')
                {
                    c.LeftX = c.RightX = c.At;
                    c.LeftY = c.Look > 0 ? hi : lo; c.RightY = c.Look > 0 ? lo : hi;
                }
                else
                {
                    c.LeftY = c.RightY = c.At;
                    c.LeftX = c.Look > 0 ? lo : hi; c.RightX = c.Look > 0 ? hi : lo;
                }
            }
            return res;
        }

        /// <summary>How far in front of the viewed wall the mark line sits (room = depth of the room it is in).</summary>
        static double Front(double room, bool partition)
        {
            double f = partition ? room * 0.2 : room * 0.45;
            return Math.Max(150, Math.Min(partition ? 300 : 900, f));
        }

        /// <summary>The ledge-wall cut of a pod without partitions (along x only, as on the reference).</summary>
        static List<Cut> LedgeCut(IList<WallLine> walls)
        {
            var res = new List<Cut>();
            var returns = new List<double>(); var main = new List<double>();
            foreach (var w in walls)
            {
                if (Math.Abs(w.X1 - w.X0) >= 1) continue;          // returns / walls running along y
                (w.Ledge ? returns : main).Add(w.X0);
            }
            if (returns.Count == 0 || main.Count == 0) return res;
            returns.Sort(); main.Sort();
            double mid = (returns[0] + returns[returns.Count - 1]) / 2, cmin = main[0], cmax = main[main.Count - 1];
            double best = double.NaN;
            foreach (double x in main)
                if (x - cmin > 200 && cmax - x > 200 && (double.IsNaN(best) || Math.Abs(x - mid) < Math.Abs(best - mid))) best = x;
            if (double.IsNaN(best)) return res;
            res.Add(new Cut { Axis = 'x', Look = best > mid ? +1 : -1, Target = best, At = mid });
            return res;
        }

        /// <summary>The look direction of a mark (left -> right turned +90 degrees): "+x", "-x", "+y" or "-y".</summary>
        public static string LookOf(double lx, double ly, double rx, double ry)
        {
            double nx = -(ry - ly), ny = rx - lx;
            if (Math.Abs(nx) > Math.Abs(ny)) return nx > 0 ? "+x" : "-x";
            return ny > 0 ? "+y" : "-y";
        }

        /// <summary>
        /// Replaces the pod name <paramref name="from"/> by <paramref name="to"/> where it stands
        /// as a whole word ("BP02" is not replaced inside "BP02M"); case-insensitive.
        /// </summary>
        public static string ReplacePod(string text, string from, string to)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(from)) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, "(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(from) + "(?![A-Za-z0-9])",
                                                                to, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>The block of a drawing name: the part before the first dash.</summary>
        public static string BlockOfName(string name)
        {
            string t = (name ?? "").Trim();
            int i = t.IndexOf('-');
            return i > 0 ? t.Substring(0, i) : t;
        }
    }
}
