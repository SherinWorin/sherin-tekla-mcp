// WF_BcwNotes.cs - the standard notes and the lifting arrows of the
// BASE, CEILING, WALL plans (Tekla Structures 2025).  Part of WF_BaseCW.dll.
//
// STANDARD NOTES (2026-10-26, owner: "keep the ref sheet as same - its standards
// for each plan"): the texts, the profile labels (marks with content), the
// legend symbols and frames of the same plan in a finished BASE, CEILING, WALL
// drawing of the model are re-created on the new plan:
//   * position: relative to the outline of the parts of the plan - left of /
//     right of / below / above the outline keeps its distance, inside it is
//     proportional;
//   * a leader is snapped onto a part of the same name and profile in the new
//     plan (the nearest one), the text keeps its offset from the leader start;
//   * a profile label (mark) that appears more than once on the reference is
//     put on EVERY part of that name and profile; once -> on one part.
// LIFTING ARROWS (rule): at every BASE_FRAME tube end that is open - no cap
// plate and no other tube against it - an arrow (symbol of the reference, else
// 'concrete' #9, NewLine3, height 6) pointing into the tube.
// API (compiled against the 2025 DLLs): Text(ViewBase, Point, string,
// PlacingBase, TextAttributes); LeaderLinePlacing(Point); Mark(ModelObject);
// Symbol(ViewBase, Point, SymbolInfo, SymbolAttributes); Rectangle(...);
// DrawingObject.GetRelatedObjects(); Part.GetSolid().
using System;
using System.Collections.Generic;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DPart = Tekla.Structures.Drawing.Part;
using DText = Tekla.Structures.Drawing.Text;
using DSymbol = Tekla.Structures.Drawing.Symbol;
using DRect = Tekla.Structures.Drawing.Rectangle;
using DMark = Tekla.Structures.Drawing.Mark;
using DLine = Tekla.Structures.Drawing.Line;
using DModelObject = Tekla.Structures.Drawing.ModelObject;

namespace P142.WallFraming
{
    /// <summary>Standard notes and lifting arrows of the BASE, CEILING, WALL plans.</summary>
    public static class WfBcwNotes
    {
        sealed class PB
        {
            public int Id; public string Key = "", Name = "";
            public double X0, Y0, X1, Y1;
            public DPart Drawn;
        }

        /// <summary>The parts of a view with their extents in its display coordinates.</summary>
        static List<PB> Parts(View v, TSM.Model model)
        {
            var res = new List<PB>();
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(v.DisplayCoordinateSystem));
                DrawingObjectEnumerator en = v.GetAllObjects(typeof(DPart));
                while (en.MoveNext())
                {
                    DPart dp = en.Current as DPart;
                    if (dp == null) continue;
                    TSM.Part mp = model.SelectModelObject(new Identifier(dp.ModelIdentifier.ID)) as TSM.Part;
                    if (mp == null) continue;
                    TSM.Solid so; try { so = mp.GetSolid(); } catch (Exception) { continue; }
                    string prof = ""; mp.GetReportProperty("PROFILE", ref prof);
                    string name = (mp.Name ?? "").ToUpperInvariant();
                    res.Add(new PB { Id = mp.Identifier.ID, Name = name, Key = name + "|" + (prof ?? ""), Drawn = dp,
                        X0 = so.MinimumPoint.X, Y0 = so.MinimumPoint.Y, X1 = so.MaximumPoint.X, Y1 = so.MaximumPoint.Y });
                }
            }
            finally { wph.SetCurrentTransformationPlane(original); }
            return res;
        }

        static string P(Point p) { return "(" + p.X.ToString("F0") + "," + p.Y.ToString("F0") + ")"; }
        static string B(double[] b) { return "[" + b[0].ToString("F0") + "," + b[1].ToString("F0") + " .. " + b[2].ToString("F0") + "," + b[3].ToString("F0") + "]"; }

        static double[] Box(List<PB> ps)
        {
            var b = new[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
            foreach (var p in ps) { b[0] = Math.Min(b[0], p.X0); b[1] = Math.Min(b[1], p.Y0); b[2] = Math.Max(b[2], p.X1); b[3] = Math.Max(b[3], p.Y1); }
            return b;
        }

        static double Map1(double v, double a0, double a1, double b0, double b1)
        {
            if (v <= a0) return b0 - (a0 - v);
            if (v >= a1) return b1 + (v - a1);
            return a1 - a0 < 1 ? b0 : b0 + (v - a0) / (a1 - a0) * (b1 - b0);
        }

        static Point Map(Point p, double[] a, double[] b)
        {
            return new Point(Map1(p.X, a[0], a[2], b[0], b[2]), Map1(p.Y, a[1], a[3], b[1], b[3]), 0);
        }

        static double Dist(PB p, Point q)
        {
            double dx = Math.Max(0, Math.Max(p.X0 - q.X, q.X - p.X1)), dy = Math.Max(0, Math.Max(p.Y0 - q.Y, q.Y - p.Y1));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static PB Nearest(List<PB> ps, Point q, string key, double max)
        {
            PB best = null; double bd = max;
            foreach (var p in ps)
            {
                if (key != null && p.Key != key) continue;
                double d = Dist(p, q);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        static Point Clamp(PB p, Point q)
        {
            return new Point(Math.Max(p.X0, Math.Min(p.X1, q.X)), Math.Max(p.Y0, Math.Min(p.Y1, q.Y)), 0);
        }

        static Point Mid(PB p) { return new Point((p.X0 + p.X1) / 2, (p.Y0 + p.Y1) / 2, 0); }

        /// <summary>
        /// Re-creates the standard notes of the style view <paramref name="sv"/> on
        /// <paramref name="v"/> (see the header).  Returns the number made.
        /// </summary>
        public static int CopyStandard(View v, View sv, TSM.Model model, StringBuilder log)
        {
            if (sv == null) { log.AppendLine("     notes: no finished plan of this kind in the model - none copied"); return 0; }
            var src = Parts(sv, model); var dst = Parts(v, model);
            if (src.Count == 0 || dst.Count == 0) { log.AppendLine("     notes: no parts to anchor to"); return 0; }
            double[] a = Box(src), b = Box(dst);
            log.AppendLine("     outline: reference '" + sv.Name + "' " + B(a) + " (" + src.Count + " parts, display CS origin " + P(sv.DisplayCoordinateSystem.Origin) +
                           ", view CS origin " + P(sv.ViewCoordinateSystem.Origin) + ") -> new " + B(b) + " (" + dst.Count + " parts, display CS origin " +
                           P(v.DisplayCoordinateSystem.Origin) + ", view CS origin " + P(v.ViewCoordinateSystem.Origin) + ")");
            var objs = new List<DrawingObject>();
            DrawingObjectEnumerator en = sv.GetAllObjects();
            while (en.MoveNext()) if (en.Current != null) objs.Add(en.Current);

            // profile labels: how often each owner key is labelled on the reference
            var markOwner = new Dictionary<DMark, PB>();
            var keyCount = new Dictionary<string, int>();
            foreach (var o in objs)
            {
                DMark mk = o as DMark;
                if (mk == null) continue;
                var ma = mk.Attributes as DMark.MarkAttributes;
                if (ma == null || ma.Content == null || ma.Content.Count == 0) continue;
                PB owner = null;
                try
                {
                    DrawingObjectEnumerator re = mk.GetRelatedObjects();
                    while (re.MoveNext() && owner == null)
                    {
                        DModelObject dm = re.Current as DModelObject;
                        if (dm != null) owner = src.Find(p => p.Id == dm.ModelIdentifier.ID);
                    }
                }
                catch (Exception) { }
                if (owner == null) continue;
                markOwner[mk] = owner;
                int c; keyCount.TryGetValue(owner.Key, out c); keyCount[owner.Key] = c + 1;
            }

            int made = 0, texts = 0, marks = 0, syms = 0, rects = 0;
            var marked = new HashSet<string>();
            foreach (var o in objs)
            {
                try
                {
                    DText t = o as DText;
                    if (t != null)
                    {
                        var ta = t.Attributes as DText.TextAttributes;
                        LeaderLinePlacing lp = t.Placing as LeaderLinePlacing;
                        DText nt;
                        if (lp != null)
                        {
                            PB so = Nearest(src, lp.StartPoint, null, 60);
                            Point ms = Map(lp.StartPoint, a, b);
                            PB to = so == null ? null : Nearest(dst, ms, so.Key, double.MaxValue);
                            Point ns = to == null ? ms : Clamp(to, ms);
                            Point ni = new Point(ns.X + t.InsertionPoint.X - lp.StartPoint.X, ns.Y + t.InsertionPoint.Y - lp.StartPoint.Y, 0);
                            nt = new DText(v, ni, t.TextString, new LeaderLinePlacing(ns), ta);
                        }
                        else nt = new DText(v, Map(t.InsertionPoint, a, b), t.TextString, ta);
                        if (nt.Insert()) { made++; texts++; }
                        log.AppendLine("       text '" + t.TextString.Replace("\n", " ") + "' " + P(t.InsertionPoint) + " -> " + P(nt.InsertionPoint));
                        continue;
                    }
                    DMark mk = o as DMark;
                    if (mk != null)
                    {
                        PB owner;
                        if (!markOwner.TryGetValue(mk, out owner)) continue;
                        LeaderLinePlacing mlp = mk.Placing as LeaderLinePlacing;
                        Point s0 = mlp != null ? mlp.StartPoint : Mid(owner);
                        Point ms = Map(s0, a, b);
                        var targets = keyCount[owner.Key] > 1 ? dst.FindAll(p => p.Key == owner.Key)
                                                              : new List<PB> { Nearest(dst, ms, owner.Key, double.MaxValue) };
                        foreach (var to in targets)
                        {
                            if (to == null || to.Drawn == null || !marked.Add(to.Id + "|" + owner.Key)) continue;
                            var nm = new DMark(to.Drawn);
                            nm.Attributes = mk.Attributes as DMark.MarkAttributes;
                            if (mlp == null)
                            {
                                // a profile label: along the member (AlongLinePlacing, as on the
                                // reference), on its centre line, offset as on the reference
                                bool vert = (to.Y1 - to.Y0) > (to.X1 - to.X0);
                                bool overt = (owner.Y1 - owner.Y0) > (owner.X1 - owner.X0);
                                Point om = Mid(owner), nmid = Mid(to);
                                double off = overt ? mk.InsertionPoint.X - om.X : mk.InsertionPoint.Y - om.Y;
                                Point a0 = vert ? new Point(nmid.X, to.Y0, 0) : new Point(to.X0, nmid.Y, 0);
                                Point a1 = vert ? new Point(nmid.X, to.Y1, 0) : new Point(to.X1, nmid.Y, 0);
                                nm.Placing = new AlongLinePlacing(a0, a1);
                                nm.InsertionPoint = vert ? new Point(nmid.X + off, nmid.Y, 0) : new Point(nmid.X, nmid.Y + off, 0);
                            }
                            else
                            {
                                Point ns = keyCount[owner.Key] > 1 ? Mid(to) : Clamp(to, ms);
                                nm.InsertionPoint = new Point(ns.X + mk.InsertionPoint.X - s0.X, ns.Y + mk.InsertionPoint.Y - s0.Y, 0);
                                nm.Placing = new LeaderLinePlacing(ns);
                            }
                            if (nm.Insert()) { made++; marks++; }
                        }
                        continue;
                    }
                    // legend symbols and frames: only those outside the outline (the
                    // arrows on the frame are made by rule, see LiftingArrows)
                    DSymbol sy = o as DSymbol;
                    if (sy != null)
                    {
                        Point p = sy.InsertionPoint;
                        if (p.X > a[0] - 150 && p.X < a[2] + 150 && p.Y > a[1] - 150 && p.Y < a[3] + 150) continue;
                        var ns = new DSymbol(v, Map(p, a, b), new SymbolInfo(sy.SymbolInfo.SymbolFile, sy.SymbolInfo.SymbolIndex),
                                             sy.Attributes as DSymbol.SymbolAttributes);
                        if (ns.Insert()) { made++; syms++; }
                        continue;
                    }
                    DRect re2 = o as DRect;
                    if (re2 != null)
                    {
                        var nr = new DRect(v, Map(re2.StartPoint, a, b), re2.Width, re2.Height, re2.Attributes as DRect.RectangleAttributes);
                        nr.Angle = re2.Angle;
                        if (nr.Insert()) { made++; rects++; }
                    }
                }
                catch (Exception e) { log.AppendLine("     note not copied (" + o.GetType().Name + "): " + e.Message); }
            }
            log.AppendLine("     standard notes from '" + sv.Name + "': " + texts + " text(s), " + marks + " profile label(s), " + syms + " legend symbol(s), " + rects + " frame(s)");
            return made;
        }

        /// <summary>
        /// Sections: re-creates the level lines and the dimension chains of the style
        /// view on <paramref name="v"/> (positions mapped as in CopyStandard).  A chain
        /// point is snapped to the nearest part edge of the new view (40 mm) along the
        /// measured direction, so the values are measured, not copied.  Returns the number made.
        /// </summary>
        public static int CopyLinesAndDims(View v, View sv, TSM.Model model, StringBuilder log)
        {
            if (sv == null) return 0;
            var src = Parts(sv, model); var dst = Parts(v, model);
            if (src.Count == 0 || dst.Count == 0) return 0;
            double[] a = Box(src), b = Box(dst);
            var xs = new List<double>(); var ys = new List<double>();
            foreach (var p in dst) { xs.Add(p.X0); xs.Add(p.X1); ys.Add(p.Y0); ys.Add(p.Y1); }
            int lines = 0, sets = 0, failed = 0;
            var h = new StraightDimensionSetHandler();
            DrawingObjectEnumerator en = sv.GetAllObjects(new[] { typeof(DLine), typeof(StraightDimensionSet) });
            var objs = new List<DrawingObject>();
            while (en.MoveNext()) if (en.Current != null) objs.Add(en.Current);
            foreach (var o in objs)
            {
                try
                {
                    DLine l = o as DLine;
                    if (l != null)
                    {
                        if (new DLine(v, Map(l.StartPoint, a, b), Map(l.EndPoint, a, b), l.Attributes as DLine.LineAttributes).Insert()) lines++;
                        continue;
                    }
                    var set = (StraightDimensionSet)o;
                    var pts = new List<Point>(); Vector up = null; double dist = 0;
                    DrawingObjectEnumerator ce = set.GetObjects();
                    while (ce.MoveNext())
                    {
                        StraightDimension d = ce.Current as StraightDimension;
                        if (d == null) continue;
                        if (up == null) { up = d.UpDirection; dist = d.Distance; }
                        pts.Add(d.StartPoint); pts.Add(d.EndPoint);
                    }
                    if (up == null || pts.Count < 2) continue;
                    bool alongY = Math.Abs(up.X) > Math.Abs(up.Y);       // up = +-x: the chain measures heights
                    var pl = new PointList();
                    var seen = new List<double>();
                    foreach (Point q in pts)
                    {
                        Point m = Map(q, a, b);
                        if (alongY) m.Y = WfSectionRules.Snap(m.Y, ys, 40); else m.X = WfSectionRules.Snap(m.X, xs, 40);
                        double k = alongY ? m.Y : m.X;
                        if (seen.Exists(s0 => Math.Abs(s0 - k) < 1)) continue;
                        seen.Add(k); pl.Add(m);
                    }
                    if (pl.Count < 2) continue;
                    StraightDimensionSet ns = h.CreateDimensionSet(v, pl, up, dist, set.Attributes as StraightDimensionSet.StraightDimensionSetAttributes);
                    if (ns != null) sets++; else failed++;
                }
                catch (Exception e) { failed++; log.AppendLine("     not copied (" + o.GetType().Name + "): " + e.Message); }
            }
            log.AppendLine("     from '" + sv.Name + "': " + lines + " level line(s), " + sets + " dimension chain(s)" + (failed > 0 ? ", " + failed + " failed" : ""));
            return lines + sets;
        }

        /// <summary>The lifting arrows of the base plan (see the header).  Returns the number made.</summary>
        public static int LiftingArrows(View v, View sv, TSM.Model model, StringBuilder log)
        {
            var ps = Parts(v, model);
            var tubes = ps.FindAll(p => p.Name == "BASE_FRAME");
            // the arrow of the reference: symbol, colour, height
            SymbolInfo info = new SymbolInfo("concrete", 9);
            DSymbol.SymbolAttributes sa = null;
            if (sv != null)
            {
                DrawingObjectEnumerator en = sv.GetAllObjects(typeof(DSymbol));
                while (en.MoveNext() && sa == null)
                {
                    DSymbol s = en.Current as DSymbol;
                    if (s == null) continue;
                    info = new SymbolInfo(s.SymbolInfo.SymbolFile, s.SymbolInfo.SymbolIndex);
                    sa = s.Attributes as DSymbol.SymbolAttributes;
                }
            }
            if (sa == null) { sa = new DSymbol.SymbolAttributes(); sa.Height = 6; sa.Color = DrawingColors.NewLine3; }
            int n = 0;
            foreach (var t in tubes)
            {
                bool vert = (t.Y1 - t.Y0) > (t.X1 - t.X0);
                for (int e = 0; e < 2; e++)
                {
                    // end face, outward direction
                    double c = vert ? (t.X0 + t.X1) / 2 : (t.Y0 + t.Y1) / 2;
                    double f = vert ? (e == 0 ? t.Y0 : t.Y1) : (e == 0 ? t.X0 : t.X1);
                    double dir = e == 0 ? -1 : 1;
                    Point probe = vert ? new Point(c, f + dir * 2, 0) : new Point(f + dir * 2, c, 0);
                    // closed: a cap plate or another part right at the end face
                    bool closed = ps.Exists(o => o != t && Dist(o, probe) < 1.0);
                    if (closed) continue;
                    // the arrow points INTO the tube: direction = -dir along the axis
                    double dx = vert ? 0 : -dir, dy = vert ? -dir : 0;
                    double ang = Math.Atan2(-dx, dy) * 180 / Math.PI;      // 0 = up, 90 = left, 180 = down, 270 = right
                    if (ang < 0) ang += 360;
                    var a = new DSymbol.SymbolAttributes();
                    a.Height = sa.Height; a.Color = sa.Color; a.Frame = sa.Frame; a.Angle = ang;
                    Point at = vert ? new Point(c, f, 0) : new Point(f, c, 0);
                    var s = new DSymbol(v, at, info, a);
                    if (s.Insert()) n++;
                }
            }
            log.AppendLine("     lifting arrows: " + n + " open BASE_FRAME tube end(s) of " + tubes.Count + " tube(s)");
            return n;
        }
    }
}
