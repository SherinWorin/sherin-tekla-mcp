// WF_PodDrawings.cs - P142_POD_DRAWINGS: makes the empty drawing set of a new pod
// from the drawings of an existing pod (Tekla Structures 2025).  Part of WF_BaseCW.dll.
//
// The owner made BP20M-10001 / -20001 / -80001 / -CPL / -WPL by hand: clone or
// copy, rename, type the pod name into the titles.  This macro asks for the new
// pod name and the source pod (default: the pod of the open drawing) and, for
// every drawing <SOURCE>-xxx of the model:
//   * creates the GA drawing <NEW>-xxx (skipped when it exists) with the drawing
//     layout chosen in the dialog (the model's .lay layouts, e.g. RIVERSIDE
//     MARINE-3-GA-A1 = the title block) at the source's sheet size, and the source's
//     titles and drawing user properties, the pod name replaced as a whole word
//     (BP02 is not changed in BP02M).  The 2025 API does not let a program copy a
//     drawing's layout (Drawing.Layout has no public setter, the table layout id
//     is internal), so the layout goes through a GA properties file: the macro
//     writes model\attributes\P142_POD_<width>x<height>.gd from the model's own
//     GA .gd (layout name and fixed sheet size changed) and creates the drawing
//     with GADrawing(name, that file);
//   * optionally (YES to the second question) opens each new drawing once and copies
//     the sheet-level items of the source (frame lines, texts, images such as the key
//     plan) - the open drawing is SAVED and closed first, and reopened at the end.
// No view is made: the views come from the P142 drawing macros afterwards
// (P142_BASE_C_W on -10001, P142_WF_Section on -20001, P142_WF_Isometric on -80001 ...).
// API (2025, checked in Tekla.Structures.Drawing.xml): GADrawing(string, Size), Drawing.Layout,
// Title1..3, Insert(); DatabaseObject.Get*UserProperties / SetUserProperty; DrawingHandler.
// SetActiveDrawing(Drawing, bool) / SaveActiveDrawing / CloseActiveDrawing(bool);
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
                string src0 = open == null ? "" : WfSectionRules.BlockOfName(open.Name);
                string src, dst, layout;
                string attr = Path.Combine(new TSM.Model().GetInfo().ModelPath, "attributes");
                var layouts = new List<string>();
                try { foreach (string f in Directory.GetFiles(attr, "*.lay")) layouts.Add(Path.GetFileNameWithoutExtension(f)); } catch (Exception) { }
                if (!Ask(src0, layouts, out src, out dst, out layout)) { result.Ok = true; result.Message = "Nothing done."; return; }
                src = src.Trim().ToUpperInvariant(); dst = dst.Trim().ToUpperInvariant();
                if (src.Length == 0 || dst.Length == 0 || src == dst) { result.Ok = false; result.Message = "Give a source pod and a different new pod name."; return; }

                var all = new List<Drawing>();
                DrawingEnumerator de = dh.GetDrawings();
                while (de.MoveNext()) if (de.Current != null) all.Add(de.Current);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in all) names.Add((d.Name ?? "").Trim());
                var sources = all.FindAll(d => d is GADrawing && string.Equals(WfSectionRules.BlockOfName(d.Name), src, StringComparison.OrdinalIgnoreCase));
                if (sources.Count == 0) { result.Ok = false; result.Message = "No GA drawing named " + src + "-... in the model."; return; }
                sources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                // the new pod should exist in the model (warning only)
                ArrayList ids; double[] ext;
                bool inModel = false;
                try { inModel = WfBaseCwCore.PodSections(new TSM.Model(), dst, out ids, out ext) != null; } catch (Exception) { }

                var plan = new StringBuilder();
                foreach (var d in sources)
                {
                    string nn = WfSectionRules.ReplacePod(d.Name, src, dst);
                    plan.AppendLine("  " + d.Name + "  ->  " + nn + (names.Contains(nn) ? "   (exists - skipped)" : ""));
                }
                DialogResult go = MessageBox.Show("New drawings for pod " + dst + " from pod " + src + ":\n\n" + plan +
                    (inModel ? "" : "\nWARNING: no wall of block " + dst + " (ASSEMBLY_POS " + dst + "-...) was found in the model.\n") +
                    "\nYES = create them (titles, title block, sheet size, drawing properties of the source)\n" +
                    "         and ALSO copy the sheet items (key plan, frame lines, texts): the open drawing is SAVED,\n" +
                    "         each new drawing is opened once, and the open drawing is reopened at the end.\n" +
                    "NO = create them without the sheet items (nothing is opened).\nCANCEL = do nothing.",
                    "P142 Pod drawings", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (go == DialogResult.Cancel) { result.Ok = true; result.Message = "Nothing done."; return; }
                bool sheets = go == DialogResult.Yes;

                var made = new List<KeyValuePair<Drawing, Drawing>>();
                int failed = 0;
                foreach (var d in sources)
                {
                    string nn = WfSectionRules.ReplacePod(d.Name, src, dst);
                    if (names.Contains(nn)) { log.AppendLine("SKIP " + nn + " (exists)"); continue; }
                    try
                    {
                        double w = d.Layout.SheetSize.Width, hgt = d.Layout.SheetSize.Height;
                        string gd = GaFile(attr, layout, w, hgt, log);
                        var nd = gd == null ? new GADrawing(nn, new DSize(w, hgt)) : new GADrawing(nn, gd);
                        nd.Title1 = WfSectionRules.ReplacePod(d.Title1, src, dst);
                        nd.Title2 = WfSectionRules.ReplacePod(d.Title2, src, dst);
                        nd.Title3 = WfSectionRules.ReplacePod(d.Title3, src, dst);
                        if (!nd.Insert()) { failed++; log.AppendLine("FAIL " + nn + ": Insert() returned false"); continue; }
                        int props = CopyUserProperties(d, nd, src, dst);
                        nd.Modify();
                        made.Add(new KeyValuePair<Drawing, Drawing>(d, nd));
                        log.AppendLine("OK   " + nn + "  '" + nd.Title1 + "' / '" + nd.Title2 + "' / '" + nd.Title3 + "', " +
                                       d.Layout.SheetSize.Width + "x" + d.Layout.SheetSize.Height + ", " + props + " drawing propert(ies)");
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
                result.Made = made.Count; result.Failed = failed;
                result.Ok = failed == 0;
                result.Message = made.Count + " drawing(s) made for pod " + dst + " from " + src + ", " + failed + " failure(s).";
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

        /// <summary>
        /// Writes (once) model\attributes\P142_POD_<w>x<h>.gd: the model's own GA properties
        /// file with the layout and a fixed sheet size set.  Returns its name, or null.
        /// </summary>
        static string GaFile(string attr, string layout, double w, double h, StringBuilder log)
        {
            if (string.IsNullOrEmpty(layout)) return null;
            string name = "P142_POD_" + Math.Round(w) + "x" + Math.Round(h);
            string path = Path.Combine(attr, name + ".gd");
            string basis = null;
            foreach (string f in new[] { "GA_N.gd", "standard.gd" }) if (File.Exists(Path.Combine(attr, f))) { basis = Path.Combine(attr, f); break; }
            if (basis == null) foreach (string f in Directory.GetFiles(attr, "*.gd")) { basis = f; break; }
            if (basis == null) { log.AppendLine("no GA properties file (.gd) in " + attr + " - drawings made without the layout"); return null; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>(File.ReadAllLines(basis, Encoding.Default));
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                int dot = l.IndexOf("dl.", StringComparison.Ordinal);
                if (dot < 0) continue;
                string key = l.Substring(dot).Split(' ')[0];
                string pre = l.Substring(0, dot);
                if (key == "dl.layout") lines[i] = pre + "dl.layout \"" + layout + "\"";
                else if (key == "dl.take_fixed") lines[i] = pre + "dl.take_fixed 1";
                else if (key == "dl.take_calc") lines[i] = pre + "dl.take_calc 0";
                else if (key == "dl.width") lines[i] = pre + "dl.width " + w.ToString("0.000000", inv);
                else if (key == "dl.height") lines[i] = pre + "dl.height " + h.ToString("0.000000", inv);
            }
            File.WriteAllLines(path, lines.ToArray(), Encoding.Default);
            log.AppendLine("properties file " + name + ".gd: layout '" + layout + "', " + w + " x " + h + " (from " + Path.GetFileName(basis) + ")");
            return name;
        }

        /// <summary>Asks the new pod name, the source pod and the drawing layout.</summary>
        static bool Ask(string src0, List<string> layouts, out string src, out string dst, out string layout)
        {
            src = src0; dst = ""; layout = "";
            using (var f = new Form())
            {
                f.Text = "P142 Pod drawings"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new System.Drawing.Size(420, 165);
                var l1 = new Label { Text = "New pod name (e.g. BP21M):", Left = 12, Top = 15, Width = 170 };
                var t1 = new TextBox { Left = 190, Top = 12, Width = 135, CharacterCasing = CharacterCasing.Upper };
                var l2 = new Label { Text = "Copy the drawings of pod:", Left = 12, Top = 50, Width = 170 };
                var t2 = new TextBox { Left = 190, Top = 47, Width = 135, Text = src0, CharacterCasing = CharacterCasing.Upper };
                var l3 = new Label { Text = "Drawing layout (title block):", Left = 12, Top = 85, Width = 170 };
                var c3 = new ComboBox { Left = 190, Top = 82, Width = 215, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string l in layouts) c3.Items.Add(l);
                int pick = layouts.FindIndex(l => l.ToUpperInvariant().Contains("RIVERSIDE MARINE-3"));
                if (c3.Items.Count > 0) c3.SelectedIndex = pick >= 0 ? pick : 0;
                var ok = new Button { Text = "OK", Left = 250, Top = 125, Width = 75, DialogResult = DialogResult.OK };
                var ca = new Button { Text = "Cancel", Left = 330, Top = 125, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l1, t1, l2, t2, l3, c3, ok, ca });
                f.AcceptButton = ok; f.CancelButton = ca;
                if (f.ShowDialog() != DialogResult.OK) return false;
                dst = t1.Text; src = t2.Text; layout = c3.SelectedItem as string ?? "";
                return true;
            }
        }
    }
}
