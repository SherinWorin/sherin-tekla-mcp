// WF_Settings.cs - configuration loader for the wall-framing auto-dimensioner.
//
// This reads WF_Settings.json from the same directory as the executing
// assembly (WF_Core.dll in %LOCALAPPDATA%\P142\WallFraming\).  When the file
// is missing or unreadable, the defaults are used.
//
// The settings file holds the project-specific constants: panel prefix, paper
// distances, wall layout title fragments, reference drawing mark.  They can be
// changed without recompiling the macro.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace P142.WallFraming
{
    /// <summary>
    /// Configuration settings for the wall-framing auto-dimensioner.
    /// Loaded from WF_Settings.json in the assembly directory, or defaults
    /// when the file is missing.
    /// </summary>
    public sealed class WfSettings
    {
        /// <summary>Assembly-position prefix of wall panels (e.g. "BP4-WP").</summary>
        public string PanelPrefix = "BP4-WP";

        /// <summary>Paper distances in millimetres.</summary>
        public WfPaperDistances PaperDistances = new WfPaperDistances();

        /// <summary>
        /// Title fragments that identify a wall layout drawing.  The macro
        /// works on any GA drawing whose title holds one of them.
        /// </summary>
        public string[] WallLayoutTitles = {
            "WALL FRAMING", "WALL LAYOUT", "WALL FRAMING LAYOUT"
        };

        /// <summary>The reference drawing (optional).</summary>
        public WfReferenceDrawing ReferenceDrawing = new WfReferenceDrawing();

        /// <summary>Settings for the Pro edition (optional).</summary>
        public WfProSettings ProSettings = new WfProSettings();
    }

    /// <summary>Paper distances in millimetres.</summary>
    public sealed class WfPaperDistances
    {
        /// <summary>Panel chain (bay chain).</summary>
        public double Panel = 8.8;
        /// <summary>Band chain.</summary>
        public double Band = 15.9;
    }

    /// <summary>Reference drawing configuration.</summary>
    public sealed class WfReferenceDrawing
    {
        /// <summary>Mark of the reference drawing (e.g. "[50]").</summary>
        public string Mark = "[50]";
    }

    /// <summary>Settings for the Pro edition.</summary>
    public sealed class WfProSettings
    {
        /// <summary>Width of the notch bridged in top outline (mm).</summary>
        public double NotchWidth = 2.0;

        /// <summary>Paper distances for Pro annotations.</summary>
        public WfProPaperDistances PaperDistances = new WfProPaperDistances();
    }

    /// <summary>Paper distances for Pro annotations.</summary>
    public sealed class WfProPaperDistances
    {
        /// <summary>Top outline.</summary>
        public double Outline = 21.82;
        /// <summary>Overall height dimension.</summary>
        public double OverallHeight = 24.27;
        /// <summary>Overall width dimension.</summary>
        public double OverallWidth = 27.74;
        /// <summary>North run.</summary>
        public double NorthRun = 30.0;
    }

    /// <summary>Settings loader.</summary>
    public static class WfSettingsLoader
    {
        /// <summary>
        /// Loads settings from WF_Settings.json in the assembly directory.
        /// Returns defaults when the file is missing or cannot be read.
        /// </summary>
        public static WfSettings Load()
        {
            WfSettings defaults = new WfSettings();
            try
            {
                // Find WF_Settings.json next to WF_Core.dll
                string asmPath = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(asmPath);
                string settingsPath = Path.Combine(dir, "WF_Settings.json");

                if (!File.Exists(settingsPath))
                {
                    // Settings file not found - use defaults
                    return defaults;
                }

                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                var jss = new JavaScriptSerializer();
                var raw = jss.Deserialize<Dictionary<string, object>>(json);

                WfSettings s = new WfSettings();

                // panelPrefix
                if (raw.ContainsKey("panelPrefix") && raw["panelPrefix"] is string)
                    s.PanelPrefix = (string)raw["panelPrefix"];

                // paperDistances
                if (raw.ContainsKey("paperDistances") && raw["paperDistances"] is Dictionary<string, object>)
                {
                    var pd = (Dictionary<string, object>)raw["paperDistances"];
                    if (pd.ContainsKey("panel")) s.PaperDistances.Panel = Convert.ToDouble(pd["panel"]);
                    if (pd.ContainsKey("band")) s.PaperDistances.Band = Convert.ToDouble(pd["band"]);
                }

                // wallLayoutTitles
                if (raw.ContainsKey("wallLayoutTitles") && raw["wallLayoutTitles"] is object[])
                {
                    var arr = (object[])raw["wallLayoutTitles"];
                    List<string> titles = new List<string>();
                    foreach (object o in arr)
                        if (o is string) titles.Add((string)o);
                    if (titles.Count > 0) s.WallLayoutTitles = titles.ToArray();
                }

                // referenceDrawing
                if (raw.ContainsKey("referenceDrawing") && raw["referenceDrawing"] is Dictionary<string, object>)
                {
                    var rd = (Dictionary<string, object>)raw["referenceDrawing"];
                    if (rd.ContainsKey("mark") && rd["mark"] is string)
                        s.ReferenceDrawing.Mark = (string)rd["mark"];
                }

                // proSettings
                if (raw.ContainsKey("proSettings") && raw["proSettings"] is Dictionary<string, object>)
                {
                    var ps = (Dictionary<string, object>)raw["proSettings"];
                    if (ps.ContainsKey("notchWidth"))
                        s.ProSettings.NotchWidth = Convert.ToDouble(ps["notchWidth"]);

                    if (ps.ContainsKey("paperDistances") && ps["paperDistances"] is Dictionary<string, object>)
                    {
                        var ppd = (Dictionary<string, object>)ps["paperDistances"];
                        if (ppd.ContainsKey("outline"))
                            s.ProSettings.PaperDistances.Outline = Convert.ToDouble(ppd["outline"]);
                        if (ppd.ContainsKey("overallHeight"))
                            s.ProSettings.PaperDistances.OverallHeight = Convert.ToDouble(ppd["overallHeight"]);
                        if (ppd.ContainsKey("overallWidth"))
                            s.ProSettings.PaperDistances.OverallWidth = Convert.ToDouble(ppd["overallWidth"]);
                        if (ppd.ContainsKey("northRun"))
                            s.ProSettings.PaperDistances.NorthRun = Convert.ToDouble(ppd["northRun"]);
                    }
                }

                return s;
            }
            catch
            {
                // Any error reading or parsing - fall back to defaults
                return defaults;
            }
        }
    }
}
