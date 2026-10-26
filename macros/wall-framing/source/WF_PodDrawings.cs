// WF_PodDrawings.cs - P142_POD_DRAWINGS: makes the empty drawing set of a new pod
// (Tekla Structures 2025).  Part of WF_BaseCW.dll.
//
// The owner types ONLY the pod name (owner 2026-10-26: "the input from me will update
// on each drawing, all other settings copied from each drawing").  For every drawing
// of the set (WfSectionRules.PodDrawings: -10001, -20001, -80001, -CPL, -WPL):
//   * Name = <POD>-<suffix>, Title1 = <POD> (skipped when the drawing exists);
//   * Title2 / Title3 and the drawing user properties: from the same drawing of the
//     reference pod (the pod of the open drawing), else the defaults of the set;
//   * all other settings (layout = title block, sheet size, views, marks ...): from
//     the GA properties file P142_<suffix>.gd of the model, which the owner saves ONCE
//     per model from the reference drawing (Drawing properties > Get > Save as
//     P142_10001 ...).  The 2025 API cannot read a drawing's properties, so this file
//     is the only way to copy them.  No such file: "standard" at the set's sheet size.
//   * optionally (YES) the sheet-level items of the reference drawing (key plan, frame
//     lines, texts): each new drawing is opened once - the open drawing is SAVED first
//     and reopened at the end.
// No view is made: the views come from the P142 drawing macros afterwards.
// API (2025, Tekla.Structures.Drawing.xml): GADrawing(string name, string AttributeFile),
// GADrawing(string, Size), Title1..3, Insert(); DatabaseObject.Get*UserProperties /
// SetUserProperty; DrawingHandler.SetActiveDrawing / SaveActiveDrawing / CloseActiveDrawing;
// Text / Line / Image(ViewBase, Point, Size, string, EmbeddedObjectAttributes).
// RISK: MEDIUM - adds drawings; never changes or deletes an existing drawing or the model.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Tekla.Structures.Drawing;
using TSM = Tekla.Structures.Model;
using TPoint = Tekla.Structures.Geometry3d.Point;
using DSize = Tekla.Structures.Drawing.Size;
using DText = Tekla.Structures.Drawing.Text;
using DLine = Tekla.Structures.Drawing.Line;
using DImage = Tekla.Structures.Drawing.Image;

namespace P142.WallFraming
{
    /// <summary>The P142_POD_DRAWINGS macro.</summary>
    public static class WfPodDrawingsCore
    {
        /// <summary>The loader asks this; the macro never writes into an existing drawing.</summary>
        public static bool IsProtected(Drawing d) { return false; }

        /// <summary>The macro (see the header).</summary>
        public static void Execute(Drawing open, WfBaseCwResult result, bool save)
        {
            var log = new StringBuilder();
            try
            {
                var dh = new DrawingHandler();
                string src = open == null ? "" : WfSectionRules.BlockOfName(open.Name).Trim().ToUpperInvariant();
                string attr = Path.Combine(new TSM.Model().GetInfo().ModelPath, "attributes");
                string dst = AskPod();
                if (dst == null) { result.Ok = true; result.Message = "Nothing done."; return; }
                dst = dst.Trim().ToUpperInvariant();
                if (dst.Length == 0 || dst == src) { result.Ok = false; result.Message = "Give a new pod name (not the pod of the open drawing)."; return; }

                var byName = new Dictionary<string, Drawing>(StringComparer.OrdinalIgnoreCase);
                DrawingEnumerator de = dh.GetDrawings();
                while (de.MoveNext()) if (de.Current != null) byName[(de.Current.Name ?? "").Trim()] = de.Current;

                // the new pod should exist in the model (warning only)
                ArrayList ids; double[] ext;
                bool inModel = false;
                try { inModel = WfBaseCwCore.PodSections(new TSM.Model(), dst, out ids, out ext) != null; } catch (Exception) { }

                var plan = new StringBuilder();
                foreach (var k in WfSectionRules.PodDrawings)
                {
                    string nn = dst + "-" + k.Suffix;
                    Drawing r; byName.TryGetValue(src + "-" + k.Suffix, out r);
                    bool file = File.Exists(Path.Combine(attr, k.SettingsFile + ".gd"));
                    plan.AppendLine("  " + nn + (byName.ContainsKey(nn) ? "   exists - skipped" :
                        "   settings: " + (file ? k.SettingsFile : "standard (no " + k.SettingsFile + ".gd)") +
                        ", titles: " + (r != null ? r.Name : "default")));
                }
                DialogResult go = MessageBox.Show("Drawings of pod " + dst + ":\n\n" + plan +
                    (inModel ? "" : "\nWARNING: no wall of block " + dst + " (ASSEMBLY_POS " + dst + "-...) was found in the model.\n") +
                    "\nYES = create them and ALSO copy the sheet items of the reference drawings (key plan, frame\n" +
                    "         lines, texts): the open drawing is SAVED, each new drawing is opened once, and the\n" +
                    "         open drawing is reopened at the end.\n" +
                    "NO = create them only (nothing is opened).\nCANCEL = do nothing.",
                    "P142 Pod drawings", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (go == DialogResult.Cancel) { result.Ok = true; result.Message = "Nothing done."; return; }
                bool sheets = go == DialogResult.Yes;

                var made = new List<KeyValuePair<Drawing, Drawing>>();
                int failed = 0;
                foreach (var k in WfSectionRules.PodDrawings)
                {
                    string nn = dst + "-" + k.Suffix;
                    if (byName.ContainsKey(nn)) { log.AppendLine("SKIP " + nn + " (exists)"); continue; }
                    Drawing r; byName.TryGetValue(src + "-" + k.Suffix, out r);
                    try
                    {
                        bool file = File.Exists(Path.Combine(attr, k.SettingsFile + ".gd"));
                        var nd = file ? new GADrawing(nn, k.SettingsFile) : new GADrawing(nn, new DSize(k.Width, k.Height));
                        nd.Title1 = dst;
                        nd.Title2 = r != null ? WfSectionRules.ReplacePod(r.Title2, src, dst) : k.Title2;
                        nd.Title3 = r != null ? WfSectionRules.ReplacePod(r.Title3, src, dst) : k.Title3;
                        if (!nd.Insert()) { failed++; log.AppendLine("FAIL " + nn + ": Insert() returned false"); continue; }
                        int props = r == null ? 0 : CopyUserProperties(r, nd, src, dst);
                        nd.Modify();
                        if (r != null) made.Add(new KeyValuePair<Drawing, Drawing>(r, nd));
                        log.AppendLine("OK   " + nn + "  '" + nd.Title1 + "' / '" + nd.Title2 + "' / '" + nd.Title3 + "', settings " +
                                       (file ? k.SettingsFile : "standard " + k.Width + "x" + k.Height) + ", " + props + " drawing propert(ies)");
                    }
                    catch (Exception e) { failed++; log.AppendLine("FAIL " + nn + ": " + e.GetType().Name + ": " + e.Message); }
                }

                if (sheets && made.Count > 0)
                {
                    if (open != null) { dh.SaveActiveDrawing(); dh.CloseActiveDrawing(true); }
                    foreach (var kv in made)
                    {
                        try
                        {
                            if (!dh.SetActiveDrawing(kv.Value, false)) { failed++; log.AppendLine("FAIL sheet items " + kv.Value.Name + ": could not open"); continue; }
                            Drawing act = dh.GetActiveDrawing();
                            int n = CopySheet(kv.Key, act, src, dst, log);
                            act.CommitChanges("P142 pod drawing sheet items");
                            dh.SaveActiveDrawing();
                            dh.CloseActiveDrawing(true);
                            log.AppendLine("     " + kv.Value.Name + ": " + n + " sheet item(s)");
                        }
                        catch (Exception e) { failed++; log.AppendLine("FAIL sheet items " + kv.Value.Name + ": " + e.Message); }
                    }
                    if (open != null) { try { dh.SetActiveDrawing(open, true); } catch (Exception) { } }
                }
                result.Failed = failed;
                result.Ok = failed == 0;
                result.Message = "pod " + dst + ": " + failed + " failure(s) - see the list.";
            }
            catch (Exception e) { result.Ok = false; result.Failed++; result.Message = "The macro stopped: " + e.GetType().Name + ": " + e.Message; }
            result.Log = log.ToString();
            MessageBox.Show("P142_POD_DRAWINGS: " + result.Message + "\n\n" + result.Log, "P142 Pod drawings",
                            MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        static int CopyUserProperties(Drawing from, Drawing to, string src, string dst)
        {
            int n = 0;
            var s = new Dictionary<string, string>(); var i = new Dictionary<string, int>(); var f = new Dictionary<string, double>();
            from.GetStringUserProperties(out s); from.GetIntegerUserProperties(out i); from.GetDoubleUserProperties(out f);
            if (s != null) foreach (var kv in s) if (to.SetUserProperty(kv.Key, WfSectionRules.ReplacePod(kv.Value, src, dst))) n++;
            if (i != null) foreach (var kv in i) if (to.SetUserProperty(kv.Key, kv.Value)) n++;
            if (f != null) foreach (var kv in f) if (to.SetUserProperty(kv.Key, kv.Value)) n++;
            return n;
        }

        /// <summary>Copies the sheet-level texts, lines and images of the source sheet (read, not opened) onto the open sheet.</summary>
        static int CopySheet(Drawing from, Drawing to, string src, string dst, StringBuilder log)
        {
            ContainerView ss = from.GetSheet(), ts = to.GetSheet();
            var objs = new List<DrawingObject>();
            DrawingObjectEnumerator en = ss.GetObjects();
            while (en.MoveNext()) if (en.Current != null && !(en.Current is ViewBase)) objs.Add(en.Current);
            int n = 0;
            foreach (var o in objs)
            {
                try
                {
                    DText t = o as DText;
                    if (t != null) { if (new DText(ts, t.InsertionPoint, WfSectionRules.ReplacePod(t.TextString, src, dst), t.Placing, t.Attributes as DText.TextAttributes).Insert()) n++; continue; }
                    DLine l = o as DLine;
                    if (l != null) { if (new DLine(ts, l.StartPoint, l.EndPoint, l.Attributes as DLine.LineAttributes).Insert()) n++; continue; }
                    DImage im = o as DImage;
                    if (im != null)
                    {
                        var ni = new DImage(ts, im.InsertionPoint, im.Size, im.FileName, im.Attributes as EmbeddedObjectAttributes);
                        if (ni.Insert()) n++;
                        continue;
                    }
                    log.AppendLine("     sheet item not copied (" + o.GetType().Name + ")");
                }
                catch (Exception e) { log.AppendLine("     sheet item not copied (" + o.GetType().Name + "): " + e.Message); }
            }
            return n;
        }

        /// <summary>Asks the new pod name; null = cancelled.</summary>
        static string AskPod()
        {
            using (var f = new Form())
            {
                f.Text = "P142 Pod drawings"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new System.Drawing.Size(340, 90);
                var l1 = new Label { Text = "New pod name (e.g. BP21M):", Left = 12, Top = 15, Width = 170 };
                var t1 = new TextBox { Left = 190, Top = 12, Width = 135, CharacterCasing = CharacterCasing.Upper };
                var ok = new Button { Text = "OK", Left = 170, Top = 52, Width = 75, DialogResult = DialogResult.OK };
                var ca = new Button { Text = "Cancel", Left = 250, Top = 52, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l1, t1, ok, ca });
                f.AcceptButton = ok; f.CancelButton = ca;
                return f.ShowDialog() == DialogResult.OK ? t1.Text : null;
            }
        }
    }
}
