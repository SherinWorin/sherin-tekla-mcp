// WF_Rules.cs - the FREE annotation rules, with NO Tekla dependency.
//
// This file is deliberately free of any Tekla reference so that it can be
// compiled into a standalone assembly and unit-tested without a running
// Tekla Structures (see tests/test_rules.py).  The macro
// P142_WF_AutoDimension.cs reads the model, converts the parts into
// WfPart objects and asks this class where every annotation goes.
//
// WHAT IS IN HERE: the rules of the FREE edition - the stud-spacing chain
// of every wall panel and the band chains of the bottom / left / right edge
// of the layout.
//
// WHAT IS NOT IN HERE: the rules of the Pro edition (the top outline, the
// two overall dimensions, the north run and the wall-panel mark placement).
// They live in the Pro source tree of the owner and are not published, so
// the Pro edition cannot be rebuilt from this repository alone.
//
// The rules were derived from GA drawing [50] of model
// P142-COREL REEF_BP4_MOCKUP-R00; see docs/WALL_FRAMING_RULES.md.
using System;
using System.Collections.Generic;
using System.Text;

namespace P142.WallFraming
{
    /// <summary>A part of the model, with its plan-view extents.</summary>
    public sealed class WfPart
    {
        /// <summary>Part profile name (STUD, BOTTOM_TRACK, ...).</summary>
        public string Name;
        /// <summary>Model object identifier.</summary>
        public int Id;
        /// <summary>
        /// The assembly position of the part, e.g. BP4-WP3.
        ///
        /// The driver fills this in with the ASSEMBLY_POS of the ASSEMBLY the
        /// part belongs to, because in a model where the position is written
        /// on the assembly and not on its parts, the part carries nothing.
        /// </summary>
        public string AssemblyPos;
        /// <summary>
        /// A key that is unique for the assembly the part belongs to.
        ///
        /// Two different assemblies can carry the same ASSEMBLY_POS, so the
        /// position cannot be used to tell them apart.  The driver fills
        /// this in; when it is empty the ASSEMBLY_POS is used instead.
        /// </summary>
        public string Key;
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
        /// <summary>The unique key of the assembly of the panel.</summary>
        public string Key;
        /// <summary>The BOTTOM_TRACK of the assembly; it defines the panel axis.</summary>
        public WfPart Bottom;
        /// <summary>
        /// Every BOTTOM_TRACK of the assembly.
        ///
        /// A ledge wall (a wall that steps out of the line of the wall behind
        /// it) is built with more than one bottom track, and the panel then
        /// runs from the start of the first track to the end of the last one.
        /// </summary>
        public List<WfPart> BottomTracks = new List<WfPart>();
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
        public double A0;
        /// <summary>End of the panel along its own axis.</summary>
        public double A1;
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

    /// <summary>
    /// One wall framing layout: the wall panels of ONE block.
    ///
    /// A model has one wall framing layout per block, and a GA sheet can
    /// carry the layouts of many blocks at once.  A layout is what the
    /// macro treats as "one framing" when it draws the chains of a sheet:
    /// the chains of one layout must not run into the next one.
    /// </summary>
    public sealed class WfLayout
    {
        /// <summary>The block of the layout, e.g. "BP1".</summary>
        public string Block = "";
        /// <summary>The wall panels of the layout.</summary>
        public List<WfPanel> Panels = new List<WfPanel>();
        /// <summary>Minimum X of the layout.</summary>
        public double X0;
        /// <summary>Minimum Y of the layout.</summary>
        public double Y0;
        /// <summary>Maximum X of the layout.</summary>
        public double X1;
        /// <summary>Maximum Y of the layout.</summary>
        public double Y1;
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
        /// <summary>
        /// The version of the rules interface.
        ///
        /// The Pro module (WF_Pro.dll) is a SEPARATE assembly that is built
        /// against this one, and it is loaded by reflection at run time.
        /// When the two are of different versions the Pro module fails with
        /// a bare "Method not found" - which says nothing about the cause.
        ///
        /// So every assembly that is built against the rules carries the
        /// version it was built for, and the core compares the two before
        /// it calls the Pro module.  A mismatch is then reported as
        /// "the modules are of different versions - install them again".
        ///
        /// Bump this number whenever a member of this assembly changes
        /// shape: a field that becomes a property, a member that is
        /// renamed, a signature that changes.  The number of the rules
        /// that the 2025 build of the Pro module expects is 3.
        /// </summary>
        public const int ApiVersion = 3;

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
                AddToGroup(groups, p);
            }
            return BuildPanelsFromGroups(groups, parts);
        }

        /// <summary>
        /// Groups the parts into wall panels, keeping ONLY the assemblies
        /// whose key is in the given set.
        ///
        /// This is how the macro works on a model that has many wall
        /// assemblies: the drawing says which of them it shows, and only
        /// those are dimensioned.
        /// </summary>
        /// <param name="parts">All parts read from the model.</param>
        /// <param name="keys">The keys of the assemblies to keep.</param>
        /// <param name="tol">Geometric tolerance in millimetres.</param>
        public static List<WfPanel> BuildPanelsForAssemblies(
            List<WfPart> parts, ICollection<string> keys, double tol)
        {
            HashSet<string> keep =
                new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<WfPart>> groups =
                new Dictionary<string, List<WfPart>>();

            foreach (WfPart p in parts)
            {
                if (!keep.Contains(GroupKey(p))) continue;
                AddToGroup(groups, p);
            }

            // an assembly that is shown in the drawing is not always a wall:
            // a ceiling panel is built like a wall, and a vanity post is not
            // a wall at all
            Dictionary<string, List<WfPart>> walls =
                new Dictionary<string, List<WfPart>>();
            foreach (KeyValuePair<string, List<WfPart>> kv in groups)
            {
                if (IsWallAssembly(kv.Value)) walls[kv.Key] = kv.Value;
            }
            return BuildPanelsFromGroups(walls, parts);
        }

        /// <summary>
        /// The key of the assembly a part belongs to: the unique key when the
        /// driver gave one, the assembly position otherwise.
        /// </summary>
        public static string GroupKey(WfPart p)
        {
            if (p == null) return null;
            if (!string.IsNullOrEmpty(p.Key)) return p.Key;
            return p.AssemblyPos;
        }

        /// <summary>Puts the part into the group of its assembly.</summary>
        private static void AddToGroup(Dictionary<string, List<WfPart>> groups, WfPart p)
        {
            string key = GroupKey(p);
            if (key == null) return;
            List<WfPart> list;
            if (!groups.TryGetValue(key, out list))
            {
                list = new List<WfPart>();
                groups[key] = list;
            }
            list.Add(p);
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
                AddToGroup(all, p);
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
                WfPanel pan = new WfPanel();
                pan.Key = kv.Key;
                // the panel is named after the assembly position, which is
                // what a person reads on the drawing
                string apos = null;
                foreach (WfPart p in ps)
                    if (!string.IsNullOrEmpty(p.AssemblyPos)) { apos = p.AssemblyPos; break; }
                pan.Name = apos ?? kv.Key;

                // every bottom track of the assembly
                foreach (WfPart p in ps)
                    if (string.Equals(p.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase))
                        pan.BottomTracks.Add(p);
                if (pan.BottomTracks.Count == 0) continue;

                // the panel axis is the direction of the LONGEST bottom track
                WfPart longest = pan.BottomTracks[0];
                foreach (WfPart p in pan.BottomTracks)
                    if (TrackLength(p) > TrackLength(longest)) longest = p;
                pan.Bottom = longest;
                pan.Horiz = longest.Horiz;

                // the panel runs from the start of the first track that is
                // parallel to the axis to the end of the last one
                double a0 = 0, a1 = 0;
                bool have = false;
                foreach (WfPart p in pan.BottomTracks)
                {
                    if (p.Horiz != pan.Horiz) continue;      // not parallel
                    double lo = pan.Horiz ? p.X0 : p.Y0;
                    double hi = pan.Horiz ? p.X1 : p.Y1;
                    if (!have) { a0 = lo; a1 = hi; have = true; continue; }
                    a0 = Math.Min(a0, lo);
                    a1 = Math.Max(a1, hi);
                }
                pan.A0 = a0;
                pan.A1 = a1;

                foreach (WfPart p in ps)
                {
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

        /// <summary>The length of a track along its own long axis.</summary>
        private static double TrackLength(WfPart p)
        {
            return p.Horiz ? (p.X1 - p.X0) : (p.Y1 - p.Y0);
        }

        /// <summary>
        /// Computes the extent of every panel's whole assembly (track +
        /// plasterboard), both along the panel and across it.  The band chains
        /// are measured on these extents, so the plasterboard thickness shows
        /// up as a real gap between adjacent walls.
        ///
        /// The parts are indexed by their assembly position first, so that
        /// the cost does not grow with the number of panels times the number
        /// of parts: a model with a thousand wall assemblies would otherwise
        /// take a million comparisons.
        /// </summary>
        private static void ComputeAssemblyExtents(List<WfPanel> panels, List<WfPart> parts)
        {
            Dictionary<string, List<WfPart>> index =
                new Dictionary<string, List<WfPart>>(StringComparer.Ordinal);
            foreach (WfPart q in parts)
            {
                string key = GroupKey(q);
                if (key == null) continue;
                List<WfPart> list;
                if (!index.TryGetValue(key, out list))
                {
                    list = new List<WfPart>();
                    index[key] = list;
                }
                list.Add(q);
            }

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

                List<WfPart> own;
                if (!index.TryGetValue(p.Key, out own)) continue;
                foreach (WfPart q in own)
                {
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
        /// The one exception is a stud inside an opening that has NO door
        /// frame (see <see cref="IsOpenGap"/>): there the stud is a mullion
        /// of the wall and it is measured like any other member.
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

            // the lintels mark the openings: studs inside one of them are
            // not measured
            List<double> vals = new List<double>();
            int i = 0;
            while (i < studs.Count)
            {
                WfPart s = studs[i];
                double lo = s.PanelLo(h), hi = s.PanelHi(h);

                // flush with the panel end?  the panel end already measures it
                if (Near(lo, p.A0, tol) || Near(hi, p.A1, tol)) { i++; continue; }

                // inside a lintel (an opening)?  not measured - unless the
                // opening has no door frame, then the stud is a mullion of
                // the wall and it is measured like any other member
                if (InsideOpening(p, lo, hi, tol)) { i++; continue; }

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
        /// True when the part lies inside one of the lintels of the panel,
        /// that is, inside an opening.
        ///
        /// This is the plain geometric test.  The rules do NOT use it: a
        /// stud inside an opening that has **no door frame** is a mullion
        /// of the wall and IS measured.  Use
        /// <see cref="InsideOpening"/>, which knows about the door frame.
        /// </summary>
        public static bool InsideLintel(WfPanel p, double lo, double hi, double tol)
        {
            bool h = p.Horiz;
            foreach (WfPart lintel in p.Lintels)
            {
                if (lo >= lintel.PanelLo(h) - tol && hi <= lintel.PanelHi(h) + tol)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when the part lies inside an opening that has NO door frame.
        ///
        /// A door frame is a PAIR of jambs that touch: the lintel bears on
        /// the inner one of the pair, and the opening is measured at the
        /// clear edge (see <see cref="OpeningStations"/>).  When the jamb
        /// that holds the lintel has NO touching partner, the opening is a
        /// plain GAP in the wall, and the members that stand in it (the
        /// jambs at their ends and the mullion between them) are ordinary
        /// members of the wall: they are measured like studs, at their
        /// centre.
        ///
        /// The old model has no opening of this kind, so this changes
        /// nothing there.  It comes from the new model (MODEL-2), where
        /// the wall framing layout drawing of the BP*-WP13 panels measures
        /// the jamb centres and the mullion of such a gap.
        /// </summary>
        public static bool IsOpenGap(WfPanel p, WfPart lintel, double tol)
        {
            bool h = p.Horiz;
            double ll = lintel.PanelLo(h), lh = lintel.PanelHi(h);

            List<WfPart> jambs = new List<WfPart>(p.Openings);
            foreach (WfPart j in jambs)
            {
                double a1 = j.PanelLo(h), a2 = j.PanelHi(h);
                bool holds = (ll >= a1 - tol && ll <= a2 + tol) ||
                             (lh >= a1 - tol && lh <= a2 + tol);
                if (!holds) continue;

                // a touching partner means a door frame
                bool framed = false;
                foreach (WfPart m in jambs)
                {
                    if (object.ReferenceEquals(m, j)) continue;
                    if (Near(m.PanelLo(h), a2, tol) || Near(m.PanelHi(h), a1, tol))
                    {
                        framed = true;
                        break;
                    }
                }
                if (!framed) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the part lies inside an opening that hides it: an
        /// opening with a door frame.  A stud inside such an opening is the
        /// frame of the opening and it is not measured.
        /// </summary>
        public static bool InsideOpening(WfPanel p, double lo, double hi, double tol)
        {
            bool h = p.Horiz;
            foreach (WfPart lintel in p.Lintels)
            {
                if (!(lo >= lintel.PanelLo(h) - tol && hi <= lintel.PanelHi(h) + tol))
                    continue;
                if (IsOpenGap(p, lintel, tol)) return false;   // a gap: measured
                return true;
            }
            return false;
        }

        /// <summary>
        /// The opening stations of a panel.
        ///
        /// An opening with a DOOR FRAME is a pair of ADJACENT jambs.  The
        /// lintel bears on one of them, and the opening is measured at the
        /// OUTER EDGE of that jamb - the edge the lintel bears on.  The
        /// other jamb of the pair is not measured.
        ///
        /// An opening WITHOUT a door frame (see <see cref="IsOpenGap"/>) is
        /// a gap in the wall: the jambs that hold the lintel are ordinary
        /// members of the wall, and they are measured at their CENTRE.
        ///
        /// A panel can have more than one opening, so every lintel is looked
        /// at.
        /// </summary>
        public static List<double> OpeningStations(WfPanel p, double tol)
        {
            bool h = p.Horiz;
            List<double> vals = new List<double>();
            if (p.Lintels.Count == 0) return vals;

            List<WfPart> jambs = new List<WfPart>(p.Openings);
            jambs.Sort(delegate (WfPart a, WfPart b)
            {
                return a.PanelLo(h).CompareTo(b.PanelLo(h));
            });

            foreach (WfPart lintel in p.Lintels)
            {
                double ll = lintel.PanelLo(h), lh = lintel.PanelHi(h);

                // an opening with a door frame: the clear edge of the opening
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

                // an opening without a door frame: the jambs are ordinary
                // members of the wall and they are measured at their centre
                if (!IsOpenGap(p, lintel, tol)) continue;
                foreach (WfPart j in jambs)
                {
                    double a1 = j.PanelLo(h), a2 = j.PanelHi(h);
                    bool holds = (ll >= a1 - tol && ll <= a2 + tol) ||
                                 (lh >= a1 - tol && lh <= a2 + tol);
                    if (holds) vals.Add((a1 + a2) / 2.0);
                }
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

        // ------------------------------------------------------------- layouts
        /// <summary>
        /// The block a wall panel belongs to.
        ///
        /// The block is the part of the assembly position of the panel
        /// before the first dash: "BP1-WP3" is a panel of the block "BP1",
        /// and "BP1M-WP12" is a panel of the block "BP1M".  The two are
        /// different blocks, and the dash is what tells them apart.
        ///
        /// A panel whose name carries no dash is a layout of its own.
        /// </summary>
        public static string BlockOf(WfPanel p)
        {
            if (p == null) return "";
            string name = p.Name ?? "";
            int i = name.IndexOf('-');
            return i > 0 ? name.Substring(0, i) : name;
        }

        /// <summary>
        /// The name of a block, in a form that can be compared.
        ///
        /// A block is written in two ways in a project: the assembly
        /// positions of the model say "BP7M" and the title of the drawing of
        /// that block says "BP07M".  They are the same block, so the
        /// leading zeros of every run of digits are dropped, the name is put
        /// in upper case, and the spaces and the punctuation are removed:
        /// "BP07M", "BP7M" and "bp7-m" all become "BP7M".
        ///
        /// The zeros that are NOT leading are kept, so "BP10" stays "BP10"
        /// and does not become "BP1" - that would be another block.
        /// </summary>
        public static string NormaliseBlock(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            StringBuilder sb = new StringBuilder();
            bool inRun = false;      // inside a run of digits
            bool dropping = false;   // the run has not started with a non-zero yet
            foreach (char c in name)
            {
                if (c >= '0' && c <= '9')
                {
                    if (!inRun) { inRun = true; dropping = true; }
                    if (dropping)
                    {
                        if (c != '0') { dropping = false; sb.Append(c); }
                        continue;
                    }
                    sb.Append(c);
                    continue;
                }
                inRun = false;
                dropping = false;
                if (char.IsLetter(c)) sb.Append(char.ToUpperInvariant(c));
                // the spaces, the dashes and the other punctuation are dropped
            }
            return sb.ToString();
        }

        /// <summary>
        /// Are two block names the name of the same block?
        ///
        /// The names are compared in their normalised form, so that the
        /// "BP7M" of the model and the "BP07M" of the drawing title are
        /// told to be the same block.
        /// </summary>
        public static bool SameBlock(string a, string b)
        {
            string x = NormaliseBlock(a);
            string y = NormaliseBlock(b);
            return x.Length > 0 && x == y;
        }

        /// <summary>
        /// The wall framing layouts of a sheet: the wall panels grouped by
        /// the block they belong to, from left to right.
        ///
        /// A GA sheet can carry the wall framing layout of many blocks, and
        /// the chains of one block must not run into the next one: the
        /// bottom band chain and the top outline of the whole sheet would
        /// run through every other layout.  The blocks are told from the
        /// assembly position of the panels, and every layout carries the
        /// box of its own panels.
        /// </summary>
        /// <param name="panels">The wall panels of the sheet.</param>
        public static List<WfLayout> LayoutGroups(List<WfPanel> panels)
        {
            List<WfLayout> res = new List<WfLayout>();
            if (panels == null) return res;

            Dictionary<string, WfLayout> byBlock =
                new Dictionary<string, WfLayout>(StringComparer.OrdinalIgnoreCase);
            foreach (WfPanel p in panels)
            {
                if (p == null) continue;
                string block = BlockOf(p);
                WfLayout lay;
                if (!byBlock.TryGetValue(block, out lay))
                {
                    lay = new WfLayout();
                    lay.Block = block;
                    byBlock[block] = lay;
                    res.Add(lay);
                }
                lay.Panels.Add(p);
            }

            // the box of every layout
            foreach (WfLayout lay in res)
            {
                lay.X0 = double.MaxValue; lay.Y0 = double.MaxValue;
                lay.X1 = double.MinValue; lay.Y1 = double.MinValue;
                foreach (WfPanel p in lay.Panels)
                {
                    lay.X0 = Math.Min(lay.X0, p.XLo);
                    lay.Y0 = Math.Min(lay.Y0, p.YLo);
                    lay.X1 = Math.Max(lay.X1, p.XHi);
                    lay.Y1 = Math.Max(lay.Y1, p.YHi);
                }
            }

            // from left to right, the way a person reads a sheet
            res.Sort(delegate (WfLayout a, WfLayout b)
            {
                return a.X0.CompareTo(b.X0);
            });
            return res;
        }
    }
}
