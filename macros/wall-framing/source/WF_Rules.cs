// WF_Rules.cs - the wall-framing annotation rules, with NO Tekla dependency.
//
// This file is deliberately free of any Tekla reference so that it can be
// compiled into a standalone assembly and unit-tested without a running
// Tekla Structures (see debug/test_rules.py).  The macro
// P142_WF_AutoDimension.cs reads the model, converts the parts into
// WfPart objects and asks this class where every annotation goes.
//
// The rules were derived from GA drawing [50] of model
// P142-COREL REEF_BP4_MOCKUP-R00; see docs/WALL_FRAMING_RULES.md.
using System;
using System.Collections.Generic;

namespace P142.WallFraming
{
    /// <summary>A part of the model, with its plan-view extents.</summary>
    public sealed class WfPart
    {
        /// <summary>Part profile name (STUD, BOTTOM_TRACK, ...).</summary>
        public string Name;
        /// <summary>Model object identifier.</summary>
        public int Id;
        /// <summary>The assembly position report property of the part.</summary>
        public string AssemblyPos;
        /// <summary>Minimum X of the part's solid.</summary>
        public double X0;
        /// <summary>Minimum Y of the part's solid.</summary>
        public double Y0;
        /// <summary>Maximum X of the part's solid.</summary>
        public double X1;
        /// <summary>Maximum Y of the part's solid.</summary>
        public double Y1;

        /// <summary>True when the part runs along X.</summary>
        public bool Horiz { get { return (X1 - X0) >= (Y1 - Y0); } }
        /// <summary>Centre of the part.</summary>
        public double Cx { get { return (X0 + X1) / 2.0; } }
        /// <summary>Centre of the part.</summary>
        public double Cy { get { return (Y0 + Y1) / 2.0; } }
        /// <summary>Start of the part along the PANEL's axis.</summary>
        public double PanelLo(bool panelHoriz) { return panelHoriz ? X0 : Y0; }
        /// <summary>End of the part along the PANEL's axis.</summary>
        public double PanelHi(bool panelHoriz) { return panelHoriz ? X1 : Y1; }
    }

    /// <summary>A wall panel: one wall assembly of the light-gauge-steel wall.</summary>
    public sealed class WfPanel
    {
        /// <summary>The assembly position of the panel, e.g. BP4-WP3.</summary>
        public string Name;
        /// <summary>The BOTTOM_TRACK of the assembly; it defines the panel axis.</summary>
        public WfPart Bottom;
        /// <summary>True when the panel runs along X.</summary>
        public bool Horiz;
        /// <summary>The studs of the panel.</summary>
        public List<WfPart> Studs = new List<WfPart>();
        /// <summary>The middle tracks of the panel.</summary>
        public List<WfPart> Tracks = new List<WfPart>();
        /// <summary>The jambs of the panel (the opening frames).</summary>
        public List<WfPart> Openings = new List<WfPart>();
        /// <summary>The plasterboards of the panel.</summary>
        public List<WfPart> Boards = new List<WfPart>();
        /// <summary>The lintels of the panel (over the openings).</summary>
        public List<WfPart> Lintels = new List<WfPart>();

        /// <summary>Start of the panel along its own axis.</summary>
        public double A0 { get { return Horiz ? Bottom.X0 : Bottom.Y0; } }
        /// <summary>End of the panel along its own axis.</summary>
        public double A1 { get { return Horiz ? Bottom.X1 : Bottom.Y1; } }
        /// <summary>Coordinate of the track on the inner side of the wall.</summary>
        public double Perp { get { return Horiz ? Bottom.Y0 : Bottom.X0; } }
        /// <summary>Coordinate of the track on the outer side of the wall.</summary>
        public double Outer { get { return Horiz ? Bottom.Y1 : Bottom.X1; } }
        /// <summary>Assembly extent along the panel axis (track + plasterboard).</summary>
        public double Asm0, Asm1;
        /// <summary>Assembly extent across the panel (track + plasterboard).</summary>
        public double AsmPerp0, AsmPerp1;
        /// <summary>Assembly bounding box: minimum X of the whole assembly.</summary>
        public double XLo, XHi;
        /// <summary>Assembly bounding box: minimum Y of the whole assembly.</summary>
        public double YLo, YHi;
    }

    /// <summary>Where a wall-panel mark is placed.</summary>
    public sealed class WfMarkPlacement
    {
        /// <summary>Insertion point of the mark (the label).</summary>
        public double InsX, InsY;
        /// <summary>Start point of the leader line.</summary>
        public double StartX, StartY;
        /// <summary>End point of the leader line.</summary>
        public double EndX, EndY;
        /// <summary>Text angle of the mark (0 / 90 / 180 / 270).</summary>
        public int Angle;
    }

    /// <summary>
    /// The NORTH RUN of the layout: the run of the northernmost wall, and the
    /// piece of the layout that lies east of it.
    /// </summary>
    public sealed class WfNorthRun
    {
        /// <summary>The level (Y) at which the run is measured.</summary>
        public double Level;
        /// <summary>The measured stations along X, in order.</summary>
        public List<double> Stations = new List<double>();
    }

    /// <summary>
    /// The two OVERALL dimensions of the layout: its height and its width.
    /// </summary>
    public sealed class WfOverall
    {
        /// <summary>Start of the height dimension (the south-west corner).</summary>
        public double Hx0, Hy0;
        /// <summary>End of the height dimension (the north-west corner).</summary>
        public double Hx1, Hy1;
        /// <summary>Start of the width dimension (the first end of the outline).</summary>
        public double Wx0, Wy0;
        /// <summary>End of the width dimension (the last end of the outline).</summary>
        public double Wx1, Wy1;
    }

    /// <summary>Which edge of the layout bounding box a band lies on.</summary>
    public enum WfBandEdge
    {
        /// <summary>The edge with the smallest Y.</summary>
        Bottom,
        /// <summary>The edge with the largest Y.</summary>
        Top,
        /// <summary>The edge with the smallest X.</summary>
        Left,
        /// <summary>The edge with the largest X.</summary>
        Right
    }

    /// <summary>
    /// The annotation rules of the WALL FRAMING LAYOUT drawing.
    /// </summary>
    public static class WfRules
    {
        /// <summary>Part names of the light-gauge-steel wall construction.</summary>
        public static readonly string[] WallParts = {
            "STUD", "BOTTOM_TRACK", "TOP_TRACK", "MIDDLE_TRACK",
            "GYPSUM_BOARD", "JAMB", "JAMBS", "LINTEL", "SILL"
        };

        /// <summary>True when the part name belongs to the wall construction.</summary>
        public static bool IsWallPart(string name)
        {
            if (name == null) return false;
            foreach (string w in WallParts)
                if (string.Equals(name, w, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True when the two values are within the tolerance.</summary>
        public static bool Near(double a, double b, double tol)
        {
            return Math.Abs(a - b) <= tol;
        }

        /// <summary>Rounds to two decimals.</summary>
        public static double R(double v)
        {
            return Math.Round(v, 2);
        }

        /// <summary>Removes values that are within the tolerance of each other.</summary>
        public static List<double> Dedupe(List<double> vals, double tol)
        {
            List<double> srt = new List<double>(vals);
            srt.Sort();
            List<double> res = new List<double>();
            foreach (double v in srt)
                if (res.Count == 0 || Math.Abs(v - res[res.Count - 1]) > tol)
                    res.Add(R(v));
            return res;
        }

        /// <summary>
        /// Groups the parts into wall panels, one per wall assembly, and
        /// computes their assembly extents.
        ///
        /// Only the assemblies whose ASSEMBLY_POS starts with
        /// <paramref name="prefix"/> are used.  When that yields nothing,
        /// use <see cref="BuildPanelsAuto"/>, which finds the wall panels
        /// by their construction instead of by their name.
        /// </summary>
        /// <param name="parts">All parts read from the model.</param>
        /// <param name="prefix">Assembly position prefix of the wall panels.</param>
        /// <param name="tol">Geometric tolerance in millimetres.</param>
        public static List<WfPanel> BuildPanels(List<WfPart> parts, string prefix, double tol)
        {
            Dictionary<string, List<WfPart>> groups =
                new Dictionary<string, List<WfPart>>();

            foreach (WfPart p in parts)
            {
                if (p.AssemblyPos == null) continue;
                if (!p.AssemblyPos.StartsWith(prefix)) continue;
                List<WfPart> list;
                if (!groups.TryGetValue(p.AssemblyPos, out list))
                {
                    list = new List<WfPart>();
                    groups[p.AssemblyPos] = list;
                }
                list.Add(p);
            }
            return BuildPanelsFromGroups(groups, parts);
        }

        /// <summary>
        /// Groups the parts into wall panels WITHOUT being told how they are
        /// named, so that the macro works on a model whose assemblies are
        /// called something else.
        ///
        /// An assembly is a wall panel when it is built like a wall:
        ///   * it has a BOTTOM_TRACK and a TOP_TRACK;
        ///   * it has at least one STUD;
        ///   * it has at least one GYPSUM_BOARD;
        ///   * none of its parts is a CEILING part.
        ///
        /// The last two conditions keep the CEILING panels out: they are
        /// built the same way (they have tracks and studs) but they are
        /// ceilings, not walls.
        /// </summary>
        /// <param name="parts">All parts read from the model.</param>
        public static List<WfPanel> BuildPanelsAuto(List<WfPart> parts)
        {
            Dictionary<string, List<WfPart>> all =
                new Dictionary<string, List<WfPart>>();

            foreach (WfPart p in parts)
            {
                if (p.AssemblyPos == null) continue;
                List<WfPart> list;
                if (!all.TryGetValue(p.AssemblyPos, out list))
                {
                    list = new List<WfPart>();
                    all[p.AssemblyPos] = list;
                }
                list.Add(p);
            }

            Dictionary<string, List<WfPart>> walls =
                new Dictionary<string, List<WfPart>>();
            foreach (KeyValuePair<string, List<WfPart>> kv in all)
            {
                if (IsWallAssembly(kv.Value)) walls[kv.Key] = kv.Value;
            }
            return BuildPanelsFromGroups(walls, parts);
        }

        /// <summary>
        /// True when the parts of one assembly are built like a light-gauge
        /// steel WALL panel.
        /// </summary>
        public static bool IsWallAssembly(List<WfPart> ps)
        {
            bool bottom = false, top = false, board = false, stud = false;
            foreach (WfPart p in ps)
            {
                if (p.Name == null) continue;
                if (p.Name.StartsWith("CEILING", StringComparison.OrdinalIgnoreCase))
                    return false;                    // a ceiling, not a wall
                if (string.Equals(p.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase)) bottom = true;
                else if (string.Equals(p.Name, "TOP_TRACK", StringComparison.OrdinalIgnoreCase)) top = true;
                else if (string.Equals(p.Name, "GYPSUM_BOARD", StringComparison.OrdinalIgnoreCase)) board = true;
                else if (string.Equals(p.Name, "STUD", StringComparison.OrdinalIgnoreCase))
                {
                    // the long ceiling members are mis-named STUD
                    if (p.X1 - p.X0 < 100 && p.Y1 - p.Y0 < 100) stud = true;
                }
            }
            return bottom && top && board && stud;
        }

        /// <summary>
        /// Builds the panels of the given groups and computes their assembly
        /// extents.
        /// </summary>
        private static List<WfPanel> BuildPanelsFromGroups(
            Dictionary<string, List<WfPart>> groups, List<WfPart> parts)
        {
            List<WfPanel> panels = new List<WfPanel>();

            foreach (KeyValuePair<string, List<WfPart>> kv in groups)
            {
                List<WfPart> ps = kv.Value;
                WfPart bottom = null;
                foreach (WfPart p in ps)
                {
                    if (string.Equals(p.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase))
                    {
                        bottom = p;
                        break;
                    }
                }
                if (bottom == null) continue;

                WfPanel pan = new WfPanel();
                pan.Name = kv.Key;
                pan.Bottom = bottom;
                pan.Horiz = bottom.Horiz;
                foreach (WfPart p in ps)
                {
                    if (ReferenceEquals(p, bottom)) continue;
                    if (string.Equals(p.Name, "STUD", StringComparison.OrdinalIgnoreCase))
                    {
                        // ignore the long ceiling members that are mis-named STUD
                        if (p.X1 - p.X0 < 100 && p.Y1 - p.Y0 < 100) pan.Studs.Add(p);
                    }
                    else if (string.Equals(p.Name, "MIDDLE_TRACK", StringComparison.OrdinalIgnoreCase))
                        pan.Tracks.Add(p);
                    else if (string.Equals(p.Name, "JAMB", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(p.Name, "JAMBS", StringComparison.OrdinalIgnoreCase))
                        pan.Openings.Add(p);
                    else if (string.Equals(p.Name, "GYPSUM_BOARD", StringComparison.OrdinalIgnoreCase))
                        pan.Boards.Add(p);
                    else if (string.Equals(p.Name, "LINTEL", StringComparison.OrdinalIgnoreCase))
                        pan.Lintels.Add(p);
                }
                panels.Add(pan);
            }

            panels.Sort(delegate (WfPanel a, WfPanel b)
            {
                if (a.Horiz != b.Horiz) return a.Horiz ? 0 : 1;
                return a.A0.CompareTo(b.A0);
            });

            ComputeAssemblyExtents(panels, parts);
            return panels;
        }

        /// <summary>
        /// Computes the extent of every panel's whole assembly (track +
        /// plasterboard), both along the panel and across it.  The band chains
        /// are measured on these extents, so the plasterboard thickness shows
        /// up as a real gap between adjacent walls.
        /// </summary>
        private static void ComputeAssemblyExtents(List<WfPanel> panels, List<WfPart> parts)
        {
            foreach (WfPanel p in panels)
            {
                p.Asm0 = p.A0;
                p.Asm1 = p.A1;
                p.AsmPerp0 = p.Horiz ? p.Bottom.Y0 : p.Bottom.X0;
                p.AsmPerp1 = p.Horiz ? p.Bottom.Y1 : p.Bottom.X1;
                p.XLo = p.Bottom.X0;
                p.XHi = p.Bottom.X1;
                p.YLo = p.Bottom.Y0;
                p.YHi = p.Bottom.Y1;
                foreach (WfPart q in parts)
                {
                    if (!string.Equals(q.AssemblyPos, p.Name, StringComparison.Ordinal)) continue;
                    p.Asm0 = Math.Min(p.Asm0, p.Horiz ? q.X0 : q.Y0);
                    p.Asm1 = Math.Max(p.Asm1, p.Horiz ? q.X1 : q.Y1);
                    p.AsmPerp0 = Math.Min(p.AsmPerp0, p.Horiz ? q.Y0 : q.X0);
                    p.AsmPerp1 = Math.Max(p.AsmPerp1, p.Horiz ? q.Y1 : q.X1);
                    p.XLo = Math.Min(p.XLo, q.X0);
                    p.XHi = Math.Max(p.XHi, q.X1);
                    p.YLo = Math.Min(p.YLo, q.Y0);
                    p.YHi = Math.Max(p.YHi, q.Y1);
                }
            }
        }

        /// <summary>
        /// The stud-spacing chain of a wall panel.
        ///
        /// Derived from the source drawing [50]:
        ///   * the two ends of the panel (its track edges) are always included;
        ///   * a stud contributes its CENTRE;
        ///   * a stud that is flush with a panel end is NOT measured again
        ///     (the panel end already measures that position);
        ///   * a stud that lies inside a LINTEL (an opening) is NOT measured;
        ///   * a run of TOUCHING studs contributes ONE value: the shared face
        ///     between the first and the second stud of the run, because the
        ///     two studs form one wider bay.
        ///
        /// NOTE: a stud runs ACROSS the wall, so its extent along the panel
        /// axis is its SHORT axis - using the part's own long axis would give
        /// the wrong coordinate.
        /// </summary>
        public static List<double> BayStations(WfPanel p, double tol)
        {
            bool h = p.Horiz;
            List<WfPart> studs = new List<WfPart>(p.Studs);
            studs.Sort(delegate (WfPart a, WfPart b)
            {
                return a.PanelLo(h).CompareTo(b.PanelLo(h));
            });

            // the lintel marks the opening: studs inside it are not measured
            double lintLo = 0, lintHi = 0;
            bool hasLintel = p.Lintels.Count > 0;
            if (hasLintel)
            {
                lintLo = p.Lintels[0].PanelLo(h);
                lintHi = p.Lintels[0].PanelHi(h);
            }

            List<double> vals = new List<double>();
            int i = 0;
            while (i < studs.Count)
            {
                WfPart s = studs[i];
                double lo = s.PanelLo(h), hi = s.PanelHi(h);

                // flush with the panel end?  the panel end already measures it
                if (Near(lo, p.A0, tol) || Near(hi, p.A1, tol)) { i++; continue; }

                // inside the lintel (an opening)?  not measured
                if (hasLintel && lo >= lintLo - tol && hi <= lintHi + tol) { i++; continue; }

                // collect the run of touching studs
                int j = i + 1;
                double runEnd = hi;
                while (j < studs.Count)
                {
                    WfPart o = studs[j];
                    if (!Near(runEnd, o.PanelLo(h), tol)) break;
                    runEnd = o.PanelHi(h);
                    j++;
                }

                if (j - i == 1)
                {
                    vals.Add((lo + hi) / 2.0);          // a single stud: its centre
                }
                else
                {
                    vals.Add(studs[i + 1].PanelLo(h));   // the shared face
                }
                i = j;
            }
            vals.Sort();
            return vals;
        }

        /// <summary>
        /// The opening stations of a panel.
        ///
        /// An opening is a pair of ADJACENT jambs.  The lintel bears on one
        /// of them, and the opening is measured at the OUTER EDGE of that
        /// jamb - the edge the lintel bears on.  The other jamb of the pair
        /// is not measured.
        /// </summary>
        public static List<double> OpeningStations(WfPanel p, double tol)
        {
            bool h = p.Horiz;
            List<double> vals = new List<double>();
            if (p.Lintels.Count == 0) return vals;

            WfPart lintel = p.Lintels[0];
            double ll = lintel.PanelLo(h), lh = lintel.PanelHi(h);

            List<WfPart> jambs = new List<WfPart>(p.Openings);
            jambs.Sort(delegate (WfPart a, WfPart b)
            {
                return a.PanelLo(h).CompareTo(b.PanelLo(h));
            });

            for (int i = 0; i < jambs.Count - 1; i++)
            {
                WfPart j1 = jambs[i], j2 = jambs[i + 1];
                double a1 = j1.PanelLo(h), a2 = j1.PanelHi(h);
                double b1 = j2.PanelLo(h), b2 = j2.PanelHi(h);
                if (!Near(a2, b1, tol)) continue;            // not an adjacent pair

                // the lintel bears on the jamb that contains its start or end
                if (ll > a1 - tol && ll <= b2 + tol) vals.Add(b2);   // lintel starts here
                else if (lh < b2 + tol && lh >= a1 - tol) vals.Add(a1); // lintel ends here
            }
            vals.Sort();
            return vals;
        }

        /// <summary>
        /// The chain of one panel: its two ends, its stud stations and its
        /// opening stations, in order along the panel axis.
        ///
        /// The ends are the ASSEMBLY ends (track + plasterboard), not the track
        /// ends: in the source drawing [50] a panel whose plasterboard runs
        /// past the track is measured to the board edge.  The track ends are
        /// measured as well, so the overhang of a board past the track shows
        /// up as its own segment (BP4-WP11 in [50]).
        /// </summary>
        public static List<double> ChainStations(WfPanel p, double tol)
        {
            List<double> chain = new List<double>();
            chain.Add(p.A0);
            chain.Add(p.Asm0);
            chain.AddRange(BayStations(p, tol));
            chain.AddRange(OpeningStations(p, tol));
            chain.Add(p.A1);
            chain.Add(p.Asm1);
            return Dedupe(chain, 0.3);
        }

        /// <summary>
        /// The band chain of one edge of the layout.
        ///
        /// A "band" is a straight line of dimensions that runs along one edge
        /// of the wall layout.  The chain is built from the wall panels whose
        /// ASSEMBLY bounding box touches that edge.  The extent used for a
        /// panel is the absolute bounding box of the whole assembly (track +
        /// plasterboard + everything else), so the gap between a track end
        /// and the next panel's plasterboard appears as its own measured
        /// segment - exactly as in the source drawing [50].
        ///
        /// The chain is: the origin of the layout along the measured axis, the
        /// START of every panel, and the END of every run of overlapping
        /// panels.
        /// </summary>
        public static List<double> BandStations(List<WfPanel> panels, WfBandEdge edge,
                                                 double bx0, double by0,
                                                 double bx1, double by1, double tol)
        {
            bool horizontal = (edge == WfBandEdge.Bottom || edge == WfBandEdge.Top);
            List<double[]> segs = new List<double[]>();

            foreach (WfPanel p in panels)
            {
                // the absolute extent of the panel along the measured axis
                double lo = horizontal ? p.XLo : p.YLo;
                double hi = horizontal ? p.XHi : p.YHi;
                // the extent across the measured axis
                double cross0 = horizontal ? p.YLo : p.XLo;
                double cross1 = horizontal ? p.YHi : p.XHi;

                bool onEdge = false;
                switch (edge)
                {
                    case WfBandEdge.Bottom: onEdge = Near(cross0, by0, tol); break;
                    case WfBandEdge.Top: onEdge = Near(cross1, by1, tol); break;
                    case WfBandEdge.Left: onEdge = Near(cross0, bx0, tol); break;
                    case WfBandEdge.Right: onEdge = Near(cross1, bx1, tol); break;
                }
                if (!onEdge) continue;
                segs.Add(new double[] { lo, hi });
            }

            segs.Sort(delegate (double[] a, double[] b) { return a[0].CompareTo(b[0]); });

            List<double> res = new List<double>();
            res.Add(R(horizontal ? bx0 : by0));      // the chain starts at the origin
            foreach (double[] s in segs) res.Add(R(s[0]));

            // the end of every run of overlapping panels
            double runHi = 0;
            bool have = false;
            foreach (double[] s in segs)
            {
                if (!have) { runHi = s[1]; have = true; continue; }
                if (s[0] <= runHi + tol)
                {
                    if (s[1] > runHi) runHi = s[1];
                }
                else
                {
                    res.Add(R(runHi));
                    runHi = s[1];
                }
            }
            if (have) res.Add(R(runHi));
            return Dedupe(res, 0.3);
        }

        /// <summary>
        /// The elementary intervals of the upper profile of the layout, with
        /// the level of the profile on each of them.
        ///
        /// Every panel is a rectangle from its west edge to its east edge,
        /// with the top of its assembly as the height.  The profile of the
        /// layout is the highest of them on every interval; an interval
        /// that no panel covers is a GAP and its level is double.MinValue.
        ///
        /// A panel covers an interval when it spans the WHOLE interval.
        /// The test is exact, not tolerant: a panel that ends 1 mm before
        /// the interval starts must not raise the level of that interval.
        ///
        /// Every interval carries a fourth value: 1 when the level of the
        /// interval is reached ONLY by panels that run along Y (a wall that
        /// crosses the outline) and 0 otherwise.  Such a level belongs to a
        /// re-entrant corner of the layout, so the top outline is chamfered
        /// across it instead of following it.
        /// </summary>
        private static List<double[]> ProfileIntervals(List<WfPanel> panels, double tol)
        {
            List<double> xs = new List<double>();
            List<double[]> segs = new List<double[]>();
            foreach (WfPanel p in panels)
            {
                segs.Add(new double[] { p.XLo, p.XHi, p.YHi, p.Horiz ? 0 : 1 });
                xs.Add(p.XLo);
                xs.Add(p.XHi);
            }
            xs.Sort();
            List<double> ux = Dedupe(xs, 0.3);
            if (ux.Count < 2) return new List<double[]>();

            List<double[]> res = new List<double[]>();
            for (int i = 0; i + 1 < ux.Count; i++)
            {
                double a = ux[i], b = ux[i + 1];
                double h = double.MinValue;
                int verticalOnly = 1;
                foreach (double[] s in segs)
                {
                    if (s[0] > a + tol) continue;      // starts inside the interval
                    if (s[1] < b - tol) continue;      // ends inside the interval
                    if (s[2] > h + tol) { h = s[2]; verticalOnly = (int)s[3]; }
                    else if (Math.Abs(s[2] - h) <= tol && s[3] == 0) verticalOnly = 0;
                }
                res.Add(new double[] { a, b, h, verticalOnly });
            }
            return res;
        }

        /// <summary>
        /// Merges the elementary intervals of the profile into runs of one
        /// level.  A run holds {x0, x1, level, verticalOnly}.
        /// </summary>
        private static List<double[]> MergeRuns(List<double[]> iv, double tol)
        {
            List<double[]> runs = new List<double[]>();
            foreach (double[] s in iv)
            {
                if (runs.Count > 0 && Math.Abs(runs[runs.Count - 1][2] - s[2]) <= tol)
                {
                    runs[runs.Count - 1][1] = s[1];
                    if (s[3] == 0) runs[runs.Count - 1][3] = 0;
                }
                else
                {
                    runs.Add(new double[] { s[0], s[1], s[2], s[3] });
                }
            }
            return runs;
        }

        /// <summary>
        /// The last run of the DIP that starts at run <paramref name="k"/>,
        /// or -1 when that run does not start a dip.
        ///
        /// A dip is a group of runs whose level drops below the level of the
        /// run before it and comes back up afterwards: a notch in the
        /// profile.  The level of the run after the dip is the level the
        /// profile returns to.
        /// </summary>
        private static int DipEnd(List<double[]> runs, int k, double tol)
        {
            if (k <= 0 || k >= runs.Count) return -1;
            double low = runs[k][2];
            if (low != double.MinValue && low >= runs[k - 1][2] - tol)
                return -1;                       // not lower than the run before
            int j = k;
            double bottom = low;
            while (j + 1 < runs.Count)
            {
                double next = runs[j + 1][2];
                if (next < bottom - tol) { bottom = next; j++; continue; }  // deeper
                if (Math.Abs(next - bottom) <= tol) { j++; continue; }     // same
                break;                                                     // back up
            }
            if (j + 1 >= runs.Count) return -1;      // the level never comes back
            if (runs[j + 1][2] <= bottom + tol) return -1;
            return j;
        }

        /// <summary>Appends a point of the outline, skipping duplicates.</summary>
        private static void AddOutlinePoint(List<double> pts, double x, double y)
        {
            int n = pts.Count;
            if (n >= 2 && Math.Abs(pts[n - 2] - x) < 0.05 &&
                         Math.Abs(pts[n - 1] - y) < 0.05) return;
            pts.Add(R(x));
            pts.Add(R(y));
        }

        /// <summary>
        /// The TOP OUTLINE of the wall layout, as a polyline.
        ///
        /// The outline is the upper boundary of the layout, drawn the way the
        /// source drawing [50] draws it:
        ///
        ///   * it runs along the north face of the walls that run along X;
        ///   * it is CHAMFERED across the walls that run along Y: a wall that
        ///     crosses the outline is a re-entrant corner of the layout, so
        ///     the outline crosses it with a diagonal instead of following
        ///     the end of that wall;
        ///   * a narrow gap between two walls (a NOTCH) is bridged when the
        ///     profile has the same level on both sides of it; when the level
        ///     changes across the gap, both edges of the gap are dimensioned.
        ///
        /// The polyline of the source drawing [50] is
        ///   (0, 3012.59) - (1785.82, 3012.59) - (1798.32, 2401.09)
        ///   - (1799.32, 2401.09) - (3187.32, 2401.09)
        /// The source drawing puts the lower level on the top of the
        /// plasterboard of the west wall (2401.09); the rule uses the level
        /// of the wall itself (2400.59).  The 0.5 mm difference does not
        /// change any dimension value.
        /// </summary>
        /// <param name="panels">The wall panels.</param>
        /// <param name="notch">The widest gap that is bridged, in mm.</param>
        /// <param name="tol">Geometric tolerance in millimetres.</param>
        /// <returns>X/Y pairs (x0, y0, x1, y1, ...).</returns>
        public static List<double> TopOutlinePolyline(List<WfPanel> panels, double notch, double tol)
        {
            List<double[]> runs = MergeRuns(ProfileIntervals(panels, tol), tol);
            List<double> pts = new List<double>();
            if (runs.Count == 0) return pts;

            // the first run that has a real level
            int first = 0;
            while (first < runs.Count && runs[first][2] == double.MinValue) first++;
            if (first >= runs.Count) return pts;

            double level = runs[first][2];
            AddOutlinePoint(pts, runs[first][0], level);

            int i = first;
            while (i < runs.Count - 1)
            {
                int j = i + 1;

                // a wall that runs along Y crosses the outline: the outline
                // is chamfered across it, the level does not change
                if (runs[j][2] != double.MinValue && runs[j][3] == 1)
                {
                    AddOutlinePoint(pts, runs[j][0], level);
                    i = j;
                    continue;
                }

                // a DIP in the profile: a narrow gap between two walls
                int dip = DipEnd(runs, j, tol);
                if (dip >= 0)
                {
                    double after = runs[dip + 1][2];
                    double width = runs[dip][1] - runs[j][0];
                    if (width <= notch && Math.Abs(level - after) <= tol)
                    {
                        // the same level on both sides: the gap is bridged
                        i = dip + 1;
                        continue;
                    }
                    // the level changes across the gap: both edges are drawn
                    AddOutlinePoint(pts, runs[j][0], after);
                    AddOutlinePoint(pts, runs[dip][1], after);
                    level = after;
                    i = dip + 1;
                    continue;
                }

                // a plain step of the profile
                double lv = runs[j][2];
                if (lv == double.MinValue)
                {
                    // a gap with no level on the far side: skip it
                    int k = j + 1;
                    while (k < runs.Count && runs[k][2] == double.MinValue) k++;
                    if (k >= runs.Count) break;
                    i = k - 1;
                    continue;
                }
                AddOutlinePoint(pts, runs[j][0], lv);
                level = lv;
                i = j;
            }

            // the last point: the east end of the outline
            AddOutlinePoint(pts, runs[runs.Count - 1][1], level);
            return pts;
        }

        /// <summary>
        /// The stations of the TOP OUTLINE of the layout: the X coordinates
        /// of its polyline.
        ///
        /// In the source drawing [50] the top outline reads
        /// 0 - 1785.82 - 1798.32 - 1799.32 - 3187.32:
        ///   * 0         the west end of the top wall (BP4-WP8);
        ///   * 1785.82   the track end of BP4-WP8, where the outline is
        ///               chamfered across the west wall (BP4-WP3);
        ///   * 1798.32   the board end of BP4-WP3;
        ///   * 1799.32   the start of the north wall (BP4-WP5);
        ///   * 3187.32   the east end of the layout.
        ///
        /// The 1 mm gap between BP4-WP3 and BP4-WP5 IS measured, because the
        /// level changes across it, while the 1 mm gap between BP4-WP5 and
        /// BP4-WP7 is not, because the level is the same on both sides.
        /// </summary>
        /// <param name="panels">The wall panels.</param>
        /// <param name="notch">The widest gap that is bridged, in mm.</param>
        /// <param name="tol">Geometric tolerance in millimetres.</param>
        /// <returns>The chain stations, in order along X.</returns>
        public static List<double> TopOutlineStations(List<WfPanel> panels, double notch, double tol)
        {
            List<double> poly = TopOutlinePolyline(panels, notch, tol);
            List<double> res = new List<double>();
            for (int i = 0; i + 1 < poly.Count; i += 2) res.Add(poly[i]);
            return res;
        }

        /// <summary>
        /// The NORTH RUN of the layout.
        ///
        /// The north run is the run of the northern wall, dimensioned from
        /// the start of that wall to the end of the LAYOUT, with the end of
        /// the wall itself as the middle station.  In the source drawing [50]
        /// it reads
        ///   1799.32 - 3110.82 - 3187.32
        ///   * 1799.32  the start of the north wall (BP4-WP5), which is also
        ///               the station where the top outline steps down;
        ///   * 3110.82  the end of the north wall, 1 mm short of the inner
        ///               face of the east wall (BP4-WP7);
        ///   * 3187.32  the east end of the layout.
        ///
        /// The rule:
        ///   * the run is the LAST HORIZONTAL segment of the top outline;
        ///   * the wall that forms it is the horizontal panel whose top is on
        ///     that level and whose west end is the start of the segment;
        ///   * the chain is the start of the segment, the end of that wall
        ///     and the end of the segment.
        ///
        /// The rule returns null when the layout has no such wall, e.g. when
        /// the northernmost wall reaches the east end of the layout and there
        /// is nothing left to measure.
        /// </summary>
        /// <param name="panels">The wall panels.</param>
        /// <param name="notch">The widest gap that is bridged, in mm.</param>
        /// <param name="tol">Geometric tolerance in millimetres.</param>
        public static WfNorthRun NorthRun(List<WfPanel> panels, double notch, double tol)
        {
            List<double> poly = TopOutlinePolyline(panels, notch, tol);

            // the last horizontal segment of the outline
            double x0 = 0, x1 = 0, level = 0;
            bool found = false;
            for (int i = 0; i + 3 < poly.Count; i += 2)
            {
                if (Math.Abs(poly[i + 1] - poly[i + 3]) > tol) continue;   // not horizontal
                x0 = poly[i];
                x1 = poly[i + 2];
                level = poly[i + 1];
                found = true;
            }
            if (!found) return null;

            // the wall that forms the segment
            foreach (WfPanel p in panels)
            {
                if (!p.Horiz) continue;
                if (!Near(p.YHi, level, tol)) continue;
                if (!Near(p.XLo, x0, tol)) continue;
                if (p.XHi >= x1 - tol) return null;    // the wall fills the segment
                WfNorthRun res = new WfNorthRun();
                res.Level = level;
                res.Stations.Add(R(x0));
                res.Stations.Add(R(p.XHi));
                res.Stations.Add(R(x1));
                return res;
            }
            return null;
        }

        /// <summary>
        /// The two OVERALL dimensions of the layout.
        ///
        /// Derived from the source drawing [50]:
        ///   * the HEIGHT is measured on the **west edge** of the layout,
        ///     from the south-west corner to the north-west corner;
        ///   * the WIDTH is measured **between the two ends of the top
        ///     outline** - the diagonal of [50] SET 5.
        /// </summary>
        /// <param name="bx0">West edge of the layout.</param>
        /// <param name="by0">South edge of the layout.</param>
        /// <param name="bx1">East edge of the layout.</param>
        /// <param name="by1">North edge of the layout.</param>
        /// <param name="outline">The polyline of the top outline.</param>
        public static WfOverall Overall(double bx0, double by0, double bx1, double by1,
                                        List<double> outline)
        {
            WfOverall o = new WfOverall();
            o.Hx0 = bx0; o.Hy0 = by0;
            o.Hx1 = bx0; o.Hy1 = by1;
            if (outline.Count >= 4)
            {
                o.Wx0 = outline[0];
                o.Wy0 = outline[1];
                o.Wx1 = outline[outline.Count - 2];
                o.Wy1 = outline[outline.Count - 1];
            }
            return o;
        }

        /// <summary>
        /// The placement of the wall-panel mark.
        ///
        /// Derived from the 11 wall-panel marks of the source drawing [50]:
        ///
        ///   * SIDE - the label goes on the side of the wall that carries NO
        ///     plasterboard, so the label never covers the board line.  When
        ///     both sides carry a board the positive side is used.
        ///     (11 of 11 in [50])
        ///
        ///   * LEADER - the leader line runs along the OUTER EDGE of the
        ///     track on the label side and spans the whole panel.
        ///     (9 of 11 in [50]; WP1 and WP10 were nudged by hand to clear
        ///     the dimension sets)
        ///
        ///   * DIRECTION - the leader runs from the panel end that the text
        ///     angle implies:
        ///       label on -X  ->  angle 90  (leader runs +Y, starts low)
        ///       label on +X  ->  angle 270 (leader runs -Y, starts high)
        ///       label on +Y  ->  angle 0   (leader runs +X, starts low)
        ///       label on -Y  ->  angle 180 (leader runs -X, starts high)
        ///     (11 of 11 in [50])
        ///
        ///   * LABEL - the label sits 29.25 mm beyond the leader line.
        ///     (9 of 11 in [50]; WP7 and WP11 were nudged by hand)
        ///
        /// The position ALONG the leader line is a drafter choice in [50] and
        /// is not reproducible; the macro puts the label at the panel centre.
        /// </summary>
        /// <param name="p">The wall panel.</param>
        /// <param name="offset">Distance of the label from the leader line.</param>
        public static WfMarkPlacement MarkPlacement(WfPanel p, double offset)
        {
            WfMarkPlacement mp = new WfMarkPlacement();
            double a0 = p.A0, a1 = p.A1;
            double mid = (a0 + a1) / 2.0;

            // the track edge on each side of the wall
            double lo = p.Horiz ? p.Bottom.Y0 : p.Bottom.X0;
            double hi = p.Horiz ? p.Bottom.Y1 : p.Bottom.X1;

            // does a plasterboard sit on the low or on the high side?
            bool boardLo = false, boardHi = false;
            foreach (WfPart b in p.Boards)
            {
                double bLo = p.Horiz ? b.Y0 : b.X0;
                double bHi = p.Horiz ? b.Y1 : b.X1;
                if (Near(bHi, lo, 0.5)) boardLo = true;
                if (Near(bLo, hi, 0.5)) boardHi = true;
            }

            bool useHigh;                       // the label is on the + side
            if (boardLo && !boardHi) useHigh = true;
            else if (boardHi && !boardLo) useHigh = false;
            else useHigh = true;                // board on both sides: use +

            // the leader runs on the outer edge of the track, on the label side
            double line = useHigh ? hi : lo;
            // the label is offset from the leader, towards the free side
            double label = useHigh ? line + offset : line - offset;

            if (p.Horiz)
            {
                mp.InsX = mid;
                mp.InsY = label;
                if (useHigh)
                {
                    mp.Angle = 0;
                    mp.StartX = a0; mp.StartY = line;
                    mp.EndX = a1; mp.EndY = line;
                }
                else
                {
                    mp.Angle = 180;
                    mp.StartX = a1; mp.StartY = line;
                    mp.EndX = a0; mp.EndY = line;
                }
            }
            else
            {
                mp.InsX = label;
                mp.InsY = mid;
                if (useHigh)
                {
                    mp.Angle = 270;
                    mp.StartX = line; mp.StartY = a1;
                    mp.EndX = line; mp.EndY = a0;
                }
                else
                {
                    mp.Angle = 90;
                    mp.StartX = line; mp.StartY = a0;
                    mp.EndX = line; mp.EndY = a1;
                }
            }
            return mp;
        }
    }
}
