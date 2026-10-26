// WF_IsometricRules.cs - the rules for ISOMETRIC VIEW creation, with NO Tekla dependency.
//
// This file is deliberately free of any Tekla reference so that it can be
// compiled into a standalone assembly and unit-tested without a running
// Tekla Structures.
//
// The rules were derived from GA drawing [115] of model
// P142-COREL REEF_BP4_MOCKUP-R00; see docs/ISOMETRIC-VIEW-STUDY.md.
using System;
using System.Collections.Generic;

namespace P142.WallFraming
{
    /// <summary>
    /// Configuration for one isometric view: its orientation and position.
    /// </summary>
    public sealed class WfIsometricViewConfig
    {
        /// <summary>View label: "ISOMETRIC VIEW 1", "ISOMETRIC VIEW 2", "ISOMETRIC VIEW 3".</summary>
        public string Label;

        /// <summary>
        /// Viewing direction vector (from model origin to camera).
        /// Standard isometric: (1,1,1) normalized, but each view rotates around Z.
        /// </summary>
        public double DirX, DirY, DirZ;

        /// <summary>Up vector (which way is "up" on the sheet).</summary>
        public double UpX, UpY, UpZ;

        /// <summary>Position on the drawing sheet (paper coordinates in mm).</summary>
        public double PaperX, PaperY;

        /// <summary>Frame size on the drawing sheet (paper coordinates in mm).</summary>
        public double FrameWidth, FrameHeight;

        /// <summary>View scale denominator (1:20 -> 20).</summary>
        public int Scale = 20;

        /// <summary>View name as it appears in Tekla (all views named "3d").</summary>
        public string ViewName = "3d";
    }

    /// <summary>
    /// Color coding rule: maps an element property to a display color.
    /// </summary>
    public sealed class WfColorRule
    {
        /// <summary>Property to match: "Class", "Material", "Name", "Type".</summary>
        public string Property;

        /// <summary>Value to match (case-insensitive).</summary>
        public string Value;

        /// <summary>Tekla color index (1-14) or true color RGB.</summary>
        public int ColorIndex;

        /// <summary>True color RGB when ColorIndex is 0 (custom).</summary>
        public int TrueColorR, TrueColorG, TrueColorB;

        /// <summary>Whether this rule uses true color (ColorIndex=0).</summary>
        public bool UseTrueColor => ColorIndex == 0;
    }

    /// <summary>
    /// The complete configuration for ISOMETRIC VIEW drawings.
    /// </summary>
    public sealed class WfIsometricConfig
    {
        /// <summary>The three isometric view configurations.</summary>
        public List<WfIsometricViewConfig> Views = new List<WfIsometricViewConfig>();

        /// <summary>Color coding rules for element display.</summary>
        public List<WfColorRule> ColorRules = new List<WfColorRule>();

        /// <summary>Element name filter: only these part names appear in isometric views.</summary>
        public string[] ElementNames = {
            "STUD", "BOTTOM_TRACK", "TOP_TRACK", "MIDDLE_TRACK",
            "GYPSUM_BOARD", "JAMB", "JAMBS", "LINTEL", "SILL",
            "CEILING_BEAM", "PLATE", "FRAME", "SHEET"
        };

        /// <summary>Title fragments that identify an ISOMETRIC VIEW drawing.</summary>
        public string[] IsometricTitles = { "ISOMETRIC VIEW", "ISOMETRIC VIEWS" };

        /// <summary>Title3 value for isometric drawings.</summary>
        public string Title3 = "3D";
    }

    /// <summary>
    /// The rules for ISOMETRIC VIEW creation.
    /// </summary>
    public static class WfIsometricRules
    {
        /// <summary>
        /// The version of the rules interface.
        /// </summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// Returns the default isometric configuration.
        ///
        /// THE VIEW ORIENTATIONS AND COLOR RULES BELOW ARE PLACEHOLDERS.
        /// THEY MUST BE REPLACED WITH VALUES FROM MANUAL INSPECTION OF [115].
        /// </summary>
        public static WfIsometricConfig GetDefaultConfig()
        {
            var cfg = new WfIsometricConfig();

            // ============================================================
            // VIEW ORIENTATIONS - PLACEHOLDERS (REQUIRE MANUAL INSPECTION)
            // ============================================================
            //
            // Standard isometric views typically use these directions:
            // View 1: SW isometric  -> Dir = (-1, -1, 1)  looking from SW
            // View 2: NE isometric  -> Dir = (1, 1, 1)    looking from NE
            // View 3: SE isometric  -> Dir = (1, -1, 1)   looking from SE
            //
            // But the ACTUAL values must come from inspecting [115] in Tekla.
            // Use View.DisplayCoordinateSystem to extract the exact vectors.

            // View 1: ISOMETRIC VIEW 1 (bottom left)
            cfg.Views.Add(new WfIsometricViewConfig
            {
                Label = "ISOMETRIC VIEW 1",
                DirX = -1, DirY = -1, DirZ = 1,    // PLACEHOLDER
                UpX = 0, UpY = 0, UpZ = 1,         // PLACEHOLDER
                PaperX = 181.8, PaperY = 337.9,    // from [115]
                FrameWidth = 209.4, FrameHeight = 216.8
            });

            // View 2: ISOMETRIC VIEW 2 (top right)
            cfg.Views.Add(new WfIsometricViewConfig
            {
                Label = "ISOMETRIC VIEW 2",
                DirX = 1, DirY = 1, DirZ = 1,      // PLACEHOLDER
                UpX = 0, UpY = 0, UpZ = 1,         // PLACEHOLDER
                PaperX = 565.8, PaperY = 480.1,    // from [115]
                FrameWidth = 208.7, FrameHeight = 250.8
            });

            // View 3: ISOMETRIC VIEW 3 (top left)
            cfg.Views.Add(new WfIsometricViewConfig
            {
                Label = "ISOMETRIC VIEW 3",
                DirX = 1, DirY = -1, DirZ = 1,     // PLACEHOLDER
                UpX = 0, UpY = 0, UpZ = 1,         // PLACEHOLDER
                PaperX = 383.4, PaperY = 179.7,    // from [115]
                FrameWidth = 209.4, FrameHeight = 195.3
            });

            // ============================================================
            // COLOR CODING SCHEME - PLACEHOLDERS (REQUIRE MANUAL INSPECTION)
            // ============================================================
            //
            // Need to inspect [115] in Tekla and document:
            // - What color is each element type/class/material?
            // - Is it by Class, Material, Name, or combination?
            //
            // Example structure (REPLACE WITH ACTUAL VALUES):
            //
            // Class 7 (STUD, JAMB)       -> ColorIndex 1 (Blue)
            // Class 5 (TRACKS, LINTEL)   -> ColorIndex 2 (Red)
            // Class 1 (GYPSUM_BOARD)     -> ColorIndex 3 (Green)
            // Class 9 (CEILING_BEAM)     -> ColorIndex 4 (Cyan)
            // Class 11 (FRAME)           -> ColorIndex 5 (Yellow)
            // Class 6 (SHEET)            -> ColorIndex 6 (Magenta)
            // Class 99 (PLATE)           -> ColorIndex 7 (Dark Blue)
            // Material Gypsum            -> TrueColor (Light Gray)
            // Material Aluminum          -> TrueColor (Silver)

            // PLACEHOLDER: Class-based coloring
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "7", ColorIndex = 1 });    // STUD, JAMB
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "5", ColorIndex = 2 });    // TRACKS, LINTEL, SILL
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "1", ColorIndex = 3 });    // GYPSUM_BOARD
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "9", ColorIndex = 4 });    // CEILING_BEAM
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "11", ColorIndex = 5 });   // FRAME
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "6", ColorIndex = 6 });    // SHEET
            cfg.ColorRules.Add(new WfColorRule { Property = "Class", Value = "99", ColorIndex = 7 });   // PLATE

            // PLACEHOLDER: Material-based coloring (overrides class)
            cfg.ColorRules.Add(new WfColorRule { Property = "Material", Value = "Gypsum", ColorIndex = 0, TrueColorR = 200, TrueColorG = 200, TrueColorB = 200 });
            cfg.ColorRules.Add(new WfColorRule { Property = "Material", Value = "Aluminum", ColorIndex = 0, TrueColorR = 192, TrueColorG = 192, TrueColorB = 192 });

            return cfg;
        }

        /// <summary>
        /// True when the part name belongs to the isometric view element set.
        /// </summary>
        public static bool IsIsometricElement(string name)
        {
            if (name == null) return false;
            var cfg = GetDefaultConfig();
            foreach (string n in cfg.ElementNames)
                if (string.Equals(name, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Gets the color rule that matches the given element properties.
        /// Returns null if no rule matches.
        /// </summary>
        public static WfColorRule GetColorRule(string elementClass, string elementMaterial, string elementName, string elementType)
        {
            var cfg = GetDefaultConfig();

            // Check in order: Class -> Material -> Name -> Type
            // First match wins (rules are ordered by priority)

            foreach (var rule in cfg.ColorRules)
            {
                switch (rule.Property.ToLowerInvariant())
                {
                    case "class":
                        if (string.Equals(rule.Value, elementClass, StringComparison.OrdinalIgnoreCase))
                            return rule;
                        break;
                    case "material":
                        if (string.Equals(rule.Value, elementMaterial, StringComparison.OrdinalIgnoreCase))
                            return rule;
                        break;
                    case "name":
                        if (string.Equals(rule.Value, elementName, StringComparison.OrdinalIgnoreCase))
                            return rule;
                        break;
                    case "type":
                        if (string.Equals(rule.Value, elementType, StringComparison.OrdinalIgnoreCase))
                            return rule;
                        break;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// One colour-code rule for the parts of an isometric view.  Taken from the
    /// owner's WF-Color-Code macro (2026-10-24).  Colours are Tekla drawing
    /// colour numbers: 156 = NewLine3 (dark blue), 157 = NewLine4 (blue-green),
    /// 158 = NewLine5 (orange), 159 = NewLine6 (grey).
    /// </summary>
    public sealed class WfIsoColourRule
    {
        /// <summary>Name of the rule, for the log.</summary>
        public string Label;
        /// <summary>Part NAME values that match (null = not used).</summary>
        public string[] Names;
        /// <summary>PROFILE values that match; a trailing * is a wildcard (null = not used).</summary>
        public string[] Profiles;
        /// <summary>Colour of the visible, section, hidden and centre lines.</summary>
        public int Color;
        /// <summary>Colour of the reference line (-1 = same as Color).</summary>
        public int RefColor = -1;
        /// <summary>Draw hidden lines.</summary>
        public bool Hidden;
        /// <summary>Draw own hidden lines.</summary>
        public bool OwnHidden;
        /// <summary>Symbol offset.</summary>
        public double SymbolOffset;
    }

    /// <summary>The colour code of the isometric views, and its matching rule.</summary>
    public static class WfIsoColours
    {
        /// <summary>Checked in this order; the first match wins.</summary>
        public static readonly WfIsoColourRule[] Rules =
        {
            new WfIsoColourRule { Label = "WALL_ORANGE", Names = new[] { "GYPSUM_BOARD" },
                                  Color = 158, Hidden = false, OwnHidden = false, SymbolOffset = 10.0 },
            new WfIsoColourRule { Label = "LGS_GREY", Profiles = new[] { "C63*40*0.7*", "CC61-0.7-7-35*" },
                                  Color = 159, Hidden = false, OwnHidden = false, SymbolOffset = 0.0 },
            new WfIsoColourRule { Label = "HRS_GREEN", Profiles = new[] { "RHS40*20*3", "RHS120*60*4.0", "RHS120*60*4", "PL4*", "RHS60*30*4" },
                                  Color = 157, Hidden = false, OwnHidden = false, SymbolOffset = 10.0 },
            new WfIsoColourRule { Label = "CEILING_VIOLET", Names = new[] { "CEILING_GYPSUM" },
                                  Color = 156, RefColor = 158, Hidden = false, OwnHidden = false, SymbolOffset = 10.0 },
        };

        /// <summary>
        /// The first rule that matches the part, or null.  Names and profiles are
        /// compared without spaces and case; a trailing * matches any rest.
        /// </summary>
        public static WfIsoColourRule Match(string name, params string[] profiles)
        {
            foreach (var r in Rules)
            {
                if (r.Names != null && Matches(name, r.Names)) return r;
                if (r.Profiles != null)
                    foreach (string p in profiles)
                        if (Matches(p, r.Profiles)) return r;
            }
            return null;
        }

        /// <summary>True when the value matches one of the patterns.</summary>
        public static bool Matches(string value, string[] patterns)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string x = value.Replace(" ", "").ToUpperInvariant();
            foreach (string t in patterns)
            {
                string n = t.Replace(" ", "").ToUpperInvariant();
                if (n.EndsWith("*")) { if (x.StartsWith(n.TrimEnd('*'))) return true; }
                else if (x == n) return true;
            }
            return false;
        }
    }
}
