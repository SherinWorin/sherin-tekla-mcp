// MemberImport.cs  -  Tekla Structures 2025 modeling macro (classic Akit format)
// ---------------------------------------------------------------------------------
// Generic member importer: inserts straight members from a CSV member list.
//   CSV header: name,profile,material,class,x1,y1,z1,x2,y2,z2   (mm, GLOBAL coordinates)
// The CSV path is read from the first line of
//   C:\Users\sheri\Sherin_AI\macros\MemberImport_source.txt
// so the same macro imports any generated geometry (stadium, trusses, frames...).
//
// Safety: runs silently (no dialogs, works with the MCP run_macro tool), refuses to run
// when parts with the same name prefix already exist, works in global coordinates and
// restores the user's work plane, commits in batches, writes a trace + log.
// Disposable / test models only.
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript {
    public class Script {
        const string SOURCE_POINTER = @"C:\Users\sheri\Sherin_AI\macros\MemberImport_source.txt";
        const string TRACE = @"C:\Users\sheri\Sherin_AI\macros\MemberImport_trace.txt";
        const int COMMIT_EVERY = 500;
        const double MIN_LENGTH = 1.0;   // mm

        static void Trace(string t) { try { File.AppendAllText(TRACE, DateTime.Now.ToString("HH:mm:ss ") + t + "\r\n"); } catch { } }

        public static void Run(Tekla.Technology.Akit.IScript akit) {
            Trace("start");
            try { Main(); } catch (Exception ex) { Trace("error " + ex); }
        }

        static void Main() {
            var model = new TSM.Model();
            if (!model.GetConnectionStatus()) { Trace("ABORT: no model open"); return; }
            if (!File.Exists(SOURCE_POINTER)) { Trace("ABORT: missing " + SOURCE_POINTER); return; }
            string csvPath = File.ReadAllLines(SOURCE_POINTER)[0].Trim().Trim('"');
            if (!File.Exists(csvPath)) { Trace("ABORT: CSV not found: " + csvPath); return; }

            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) { Trace("ABORT: CSV has no members"); return; }
            string[] head = lines[0].Split(',');
            if (head.Length < 10 || head[0].Trim() != "name" || head[4].Trim() != "x1") {
                Trace("ABORT: unexpected CSV header: " + lines[0]); return;
            }

            // prefix guard: the part of the first name up to and including the first '_'
            string first = lines[1].Split(',')[0];
            int us = first.IndexOf('_');
            string prefix = us > 0 ? first.Substring(0, us + 1) : first;
            int existing = CountExisting(model, prefix);
            if (existing > 0) { Trace("ABORT: " + existing + " parts named " + prefix + "* already exist - delete them first"); return; }

            var wp = model.GetWorkPlaneHandler();
            var saved = wp.GetCurrentTransformationPlane();
            wp.SetCurrentTransformationPlane(new TSM.TransformationPlane());
            int ok = 0, fail = 0, skipped = 0;
            var failures = new StringBuilder();
            var byName = new SortedDictionary<string, int>();
            var ci = CultureInfo.InvariantCulture;
            try {
                for (int i = 1; i < lines.Length; i++) {
                    if (lines[i].Trim().Length == 0) continue;
                    string[] c = lines[i].Split(',');
                    if (c.Length < 10) { skipped++; continue; }
                    var p1 = new Point(double.Parse(c[4], ci), double.Parse(c[5], ci), double.Parse(c[6], ci));
                    var p2 = new Point(double.Parse(c[7], ci), double.Parse(c[8], ci), double.Parse(c[9], ci));
                    if (Distance.PointToPoint(p1, p2) < MIN_LENGTH) { skipped++; continue; }
                    var b = new TSM.Beam(p1, p2);
                    b.Name = c[0];
                    b.Profile.ProfileString = c[1];
                    b.Material.MaterialString = c[2];
                    b.Class = c[3];
                    b.Position.Plane = TSM.Position.PlaneEnum.MIDDLE;
                    b.Position.Rotation = TSM.Position.RotationEnum.TOP;
                    b.Position.Depth = TSM.Position.DepthEnum.MIDDLE;
                    if (b.Insert()) {
                        ok++;
                        int n; byName.TryGetValue(c[0], out n); byName[c[0]] = n + 1;
                    } else {
                        fail++;
                        if (fail <= 20) failures.AppendLine("FAILED line " + (i + 1) + ": " + lines[i]);
                    }
                    if ((ok + fail) % COMMIT_EVERY == 0) { model.CommitChanges(); Trace("progress " + (ok + fail) + "/" + (lines.Length - 1)); }
                }
            } finally { wp.SetCurrentTransformationPlane(saved); }
            model.CommitChanges();

            var sb = new StringBuilder();
            sb.AppendLine("MemberImport  " + DateTime.Now + "  source: " + csvPath);
            sb.AppendLine("Inserted: " + ok + "   Failed: " + fail + "   Skipped: " + skipped);
            foreach (var kv in byName) sb.AppendLine("  " + kv.Key.PadRight(28) + kv.Value);
            sb.Append(failures);
            try { File.WriteAllText(Path.Combine(model.GetInfo().ModelPath, "MemberImport_log.txt"), sb.ToString()); } catch { }
            Trace(sb.ToString());
        }

        static int CountExisting(TSM.Model model, string prefix) {
            int n = 0;
            var e = model.GetModelObjectSelector().GetAllObjectsWithType(TSM.ModelObject.ModelObjectEnum.BEAM);
            while (e.MoveNext()) {
                var b = e.Current as TSM.Beam;
                if (b != null && b.Name != null && b.Name.StartsWith(prefix)) n++;
            }
            return n;
        }
    }
}
