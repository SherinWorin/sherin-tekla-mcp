// WF_CeilStudy.cs - READ-ONLY study of the ceiling framing plans of every
// BASE, CEILING, WALL drawing (Tekla Structures 2025).  Called by P142_BASE_C_W
// on a finished drawing.  Drawings are read with GetSheet() - never opened,
// never modified.  Output: %LOCALAPPDATA%\P142\WallFraming\WF_CeilDim_study.txt
//
// For each ceiling view (view plane origin Z > 1000): the parts (model id, name,
// profile, assembly pos, box in the view's display coordinates) and every
// straight dimension set (points in display coordinates, up direction,
// distance in paper mm, dimension type, colour).
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

namespace P142.WallFraming
{
    /// <summary>Read-only study of the ceiling framing plans.</summary>
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
            DrawingObjectEnumerator e = c.GetViews();
            while (e.MoveNext())
            {
                View v = e.Current as View;
                if (v != null && !v.IsSheet) { res.Add(v); continue; }
                ContainerView cv = e.Current as ContainerView;
                if (cv != null) Collect(cv, res, depth + 1);
            }
        }

        /// <summary>Writes the study; returns the file path.</summary>
        public static string Run(TSM.Model model, StringBuilder log)
        {
            string dir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "P142"), "WallFraming");
            string file = Path.Combine(dir, "WF_CeilDim_study.txt");
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
                foreach (View v in views)
                {
                    CoordinateSystem vcs = v.ViewCoordinateSystem;
                    if (vcs == null || vcs.Origin.Z < 1000) continue;
                    nv++;
                    CoordinateSystem dcs = v.DisplayCoordinateSystem;
                    AABB b = v.RestrictionBox;
                    o.AppendLine("==== DRAWING " + d.Mark + " " + d.Name + " | " + d.Title1 + " | " + d.Title2 + " | VIEW '" + v.Name + "' scale " + v.Attributes.Scale);
                    o.AppendLine("  dcs origin " + F(dcs.Origin.X) + "," + F(dcs.Origin.Y) + "," + F(dcs.Origin.Z) + "  box " +
                                 (b == null ? "-" : F(b.MinPoint.X) + "," + F(b.MinPoint.Y) + "," + F(b.MinPoint.Z) + " - " + F(b.MaxPoint.X) + "," + F(b.MaxPoint.Y) + "," + F(b.MaxPoint.Z)));
                    // parts
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
                            string prof = "", apos = "", ppos = "";
                            mp.GetReportProperty("PROFILE", ref prof); mp.GetReportProperty("ASSEMBLY_POS", ref apos); mp.GetReportProperty("PART_POS", ref ppos);
                            o.AppendLine("  PART " + mp.Identifier.ID + " " + mp.Name + " " + prof + " " + apos + " " + ppos + " box " +
                                         F(so.MinimumPoint.X) + "," + F(so.MinimumPoint.Y) + "," + F(so.MinimumPoint.Z) + " - " +
                                         F(so.MaximumPoint.X) + "," + F(so.MaximumPoint.Y) + "," + F(so.MaximumPoint.Z));
                            np++;
                        }
                    }
                    finally { wph.SetCurrentTransformationPlane(orig); }
                    // dimension sets
                    int ns = 0;
                    DrawingObjectEnumerator se = v.GetAllObjects(typeof(StraightDimensionSet));
                    while (se.MoveNext())
                    {
                        StraightDimensionSet ds = se.Current as StraightDimensionSet;
                        if (ds == null) continue;
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
                        try { var a = ds.Attributes as StraightDimensionSet.StraightDimensionSetAttributes; if (a != null) { typ = a.DimensionType.ToString(); col = a.Color.ToString(); } } catch (Exception) { }
                        o.AppendLine("  DIM up " + (up == null ? "-" : F(up.X) + "," + F(up.Y)) + " dist " + F(ds.Distance / v.Attributes.Scale) + "mm " + typ + " " + col + " : " + string.Join(" ", pts.ToArray()));
                        ns++;
                    }
                    o.AppendLine("  (" + np + " parts, " + ns + " dimension sets)");
                }
            }
            o.Insert(0, "WF_CeilDim_study " + DateTime.Now.ToString("s") + " : " + nd + " BCW drawing(s), " + nv + " ceiling view(s)\r\n");
            File.WriteAllText(file, o.ToString());
            log.AppendLine("ceiling study: " + nd + " drawing(s), " + nv + " ceiling view(s) -> " + file);
            return file;
        }
    }
}
