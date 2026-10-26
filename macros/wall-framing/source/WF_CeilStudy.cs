// WF_CeilStudy.cs - READ-ONLY study of the three plans of every BASE, CEILING,
// WALL drawing (Tekla Structures 2025).  Called by P142_BASE_C_W on a finished
// drawing.  Drawings are read with GetSheet() - never opened, never modified.
// Output: %LOCALAPPDATA%\P142\WallFraming\WF_BCW_study.txt
//
// Per view (all coordinates in the view's display coordinates, mm):
//   VIEW  name, scale, display CS, restriction box
//   PART  model id, name, profile, assembly pos, box, line colour / type,
//         hidden lines on/off, representation
//   DIM   points, up direction, distance (paper mm), type, colour
//   TEXT  string, insertion point, leader start, font height / colour
//   SYM   symbol file / index, insertion point, height, colour
//   SEC   section mark label, left / right points
//   MARK  how many part marks (and their first content)
//   OTHER any other drawing object type, counted
// Every model object is read inside a TransformationPlane restored in finally.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DPart = Tekla.Structures.Drawing.Part;
using DText = Tekla.Structures.Drawing.Text;
using DSymbol = Tekla.Structures.Drawing.Symbol;
using DMark = Tekla.Structures.Drawing.Mark;

namespace P142.WallFraming
{
    /// <summary>Read-only study of the BASE, CEILING, WALL drawings.</summary>
    public static class WfCeilStudy
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double d) { return d.ToString("F1", Inv); }
        static string P(Point p) { return p == null ? "-" : F(p.X) + "," + F(p.Y); }

        static bool IsBcw(Drawing d)
        {
            foreach (string t in new[] { d.Title1, d.Title2, d.Title3, d.Name })
                if (!string.IsNullOrEmpty(t) && t.IndexOf("BASE, CEILING, WALL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void Collect(ContainerView c, List<View> res, int depth)
        {
            if (c == null || depth > 4) return;
            var subs = new List<ContainerView>();
            DrawingObjectEnumerator e = c.GetViews();
            while (e.MoveNext())
            {
                View v = e.Current as View;
                if (v != null) { if (!v.IsSheet) res.Add(v); continue; }
                ContainerView cv = e.Current as ContainerView;
                if (cv != null) subs.Add(cv);
            }
            foreach (var cv in subs) Collect(cv, res, depth + 1);
        }

        /// <summary>Writes the study; returns the file path.</summary>
        public static string Run(TSM.Model model, StringBuilder log)
        {
            string dir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "P142"), "WallFraming");
            string file = Path.Combine(dir, "WF_BCW_study.txt");
            var o = new StringBuilder();
            int nd = 0, nv = 0;
            DrawingEnumerator de = new DrawingHandler().GetDrawings();
            var list = new List<Drawing>();
            while (de.MoveNext()) if (de.Current != null && IsBcw(de.Current)) list.Add(de.Current);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (Drawing d in list)
            {
                nd++;
                var views = new List<View>();
                try { Collect(d.GetSheet(), views, 0); } catch (Exception ex) { o.AppendLine("DRAWING " + d.Mark + " sheet not read: " + ex.Message); continue; }
                o.AppendLine("######## DRAWING " + d.Mark + " " + d.Name + " | " + d.Title1 + " | " + d.Title2 + " | " + d.Title3 +
                             (d.IsFrozen ? " | FROZEN" : "") + (d.IsIssued ? " | ISSUED" : "") + " | " + views.Count + " view(s)");
                foreach (View v in views)
                {
                    nv++;
                    try { StudyView(model, v, o); }
                    catch (Exception ex) { o.AppendLine("  view not read: " + ex.Message); }
                }
            }
            o.Insert(0, "WF_BCW_study " + DateTime.Now.ToString("s") + " : " + nd + " BCW drawing(s), " + nv + " view(s)\r\n");
            File.WriteAllText(file, o.ToString());
            log.AppendLine("BCW study: " + nd + " drawing(s), " + nv + " view(s) -> " + file);
            return file;
        }

        static void StudyView(TSM.Model model, View v, StringBuilder o)
        {
            CoordinateSystem vcs = v.ViewCoordinateSystem, dcs = v.DisplayCoordinateSystem;
            AABB b = v.RestrictionBox;
            o.AppendLine("==== VIEW '" + v.Name + "' scale " + v.Attributes.Scale + "  vcs " + F(vcs.Origin.X) + "," + F(vcs.Origin.Y) + "," + F(vcs.Origin.Z) +
                         " X " + F(vcs.AxisX.X) + "," + F(vcs.AxisX.Y) + "," + F(vcs.AxisX.Z) +
                         "  dcs " + F(dcs.Origin.X) + "," + F(dcs.Origin.Y) + "," + F(dcs.Origin.Z) + "  box " +
                         (b == null ? "-" : F(b.MinPoint.X) + "," + F(b.MinPoint.Y) + "," + F(b.MinPoint.Z) + " - " + F(b.MaxPoint.X) + "," + F(b.MaxPoint.Y) + "," + F(b.MaxPoint.Z)));

            // ---- parts, in display coordinates
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane orig = wph.GetCurrentTransformationPlane();
            int np = 0;
            try
            {
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(dcs));
                DrawingObjectEnumerator pe = v.GetAllObjects(typeof(DPart));
                while (pe.MoveNext())
                {
                    DPart dp = pe.Current as DPart;
                    if (dp == null) continue;
                    TSM.Part mp = model.SelectModelObject(new Identifier(dp.ModelIdentifier.ID)) as TSM.Part;
                    if (mp == null) continue;
                    TSM.Solid so; try { so = mp.GetSolid(); } catch (Exception) { continue; }
                    string prof = "", apos = "", ph = "";
                    mp.GetReportProperty("PROFILE", ref prof); mp.GetReportProperty("ASSEMBLY_POS", ref apos); mp.GetReportProperty("PHASE", ref ph);
                    string look = "";
                    try
                    {
                        var a = dp.Attributes;
                        look = " | line " + a.VisibleLines.Color + " " + a.VisibleLines.Type + " hidden " + a.DrawHiddenLines + "/" + a.DrawOwnHiddenLines +
                               " hiddenLine " + a.HiddenLines.Color + " " + a.HiddenLines.Type + " repr " + a.Representation;
                    }
                    catch (Exception) { }
                    o.AppendLine("  PART " + mp.Identifier.ID + " '" + mp.Name + "' " + prof + " " + apos + " ph" + ph + " box " +
                                 F(so.MinimumPoint.X) + "," + F(so.MinimumPoint.Y) + "," + F(so.MinimumPoint.Z) + " - " +
                                 F(so.MaximumPoint.X) + "," + F(so.MaximumPoint.Y) + "," + F(so.MaximumPoint.Z) + look);
                    np++;
                }
            }
            finally { wph.SetCurrentTransformationPlane(orig); }

            // ---- annotations
            var other = new SortedDictionary<string, int>();
            int ns = 0, nt = 0, nm = 0;
            string firstMark = "";
            DrawingObjectEnumerator ae = v.GetAllObjects();
            while (ae.MoveNext())
            {
                DrawingObject x = ae.Current;
                if (x == null || x is DPart) continue;
                try
                {
                    StraightDimensionSet ds = x as StraightDimensionSet;
                    if (ds != null)
                    {
                        var pts = new List<string>(); Vector up = null;
                        DrawingObjectEnumerator ce = ds.GetObjects();
                        while (ce.MoveNext())
                        {
                            StraightDimension sd = ce.Current as StraightDimension;
                            if (sd == null) continue;
                            if (up == null) up = sd.UpDirection;
                            pts.Add(P(sd.StartPoint) + ">" + P(sd.EndPoint));
                        }
                        string typ = "", col = "";
                        var da = ds.Attributes as StraightDimensionSet.StraightDimensionSetAttributes;
                        if (da != null) { typ = da.DimensionType.ToString(); col = da.Color.ToString(); }
                        o.AppendLine("  DIM up " + (up == null ? "-" : F(up.X) + "," + F(up.Y)) + " dist " + F(ds.Distance / v.Attributes.Scale) + "mm " + typ + " " + col + " : " + string.Join(" ", pts.ToArray()));
                        ns++; continue;
                    }
                    if (x is StraightDimension) continue;
                    DText t = x as DText;
                    if (t != null)
                    {
                        var ta = t.Attributes as DText.TextAttributes;
                        LeaderLinePlacing lp = t.Placing as LeaderLinePlacing;
                        o.AppendLine("  TEXT '" + (t.TextString ?? "").Replace("\n", "\\n").Replace("\r", "") + "' at " + P(t.InsertionPoint) +
                                     (lp != null ? " leader " + P(lp.StartPoint) : "") +
                                     (ta != null ? " h " + F(ta.Font.Height) + " " + ta.Font.Color + " " + ta.Font.Name + " frame " + ta.Frame.Type : ""));
                        nt++; continue;
                    }
                    DSymbol sy = x as DSymbol;
                    if (sy != null)
                    {
                        var sa = sy.Attributes as DSymbol.SymbolAttributes;
                        o.AppendLine("  SYM " + sy.SymbolInfo.SymbolFile + "#" + sy.SymbolInfo.SymbolIndex + " at " + P(sy.InsertionPoint) +
                                     (sa != null ? " h " + F(sa.Height) + " " + sa.Color + " angle " + F(sa.Angle) : ""));
                        continue;
                    }
                    SectionMark sm = x as SectionMark;
                    if (sm != null)
                    {
                        o.AppendLine("  SEC '" + sm.Attributes.MarkName + "' " + P(sm.LeftPoint) + " > " + P(sm.RightPoint));
                        continue;
                    }
                    DMark mk = x as DMark;
                    if (mk != null)
                    {
                        nm++;
                        if (firstMark == "") { var c = mk.Attributes.Content; firstMark = c == null ? "" : c.Count + " element(s)"; }
                        continue;
                    }
                }
                catch (Exception) { }
                string k = x.GetType().Name; int n0; other.TryGetValue(k, out n0); other[k] = n0 + 1;
            }
            var ol = new List<string>(); foreach (var kv in other) ol.Add(kv.Key + " " + kv.Value);
            o.AppendLine("  (" + np + " parts, " + ns + " dimension sets, " + nt + " texts, " + nm + " part marks" + (firstMark == "" ? "" : " [" + firstMark + "]") +
                         (ol.Count > 0 ? "; other: " + string.Join(", ", ol.ToArray()) : "") + ")");
        }
    }
}
