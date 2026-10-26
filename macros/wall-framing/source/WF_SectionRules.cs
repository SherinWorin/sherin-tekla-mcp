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

        /// <summary>The block of a drawing name: the part before the first dash.</summary>
        public static string BlockOfName(string name)
        {
            string t = (name ?? "").Trim();
            int i = t.IndexOf('-');
            return i > 0 ? t.Substring(0, i) : t;
        }
    }
}
