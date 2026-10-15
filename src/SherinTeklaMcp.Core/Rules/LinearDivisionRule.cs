using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Core.Rules
{
    /// <summary>
    /// A rule for walls/fences/grills divided into standard panels between columns.
    /// Loaded from <c>config\rules\*.json</c> - project numbers are never hard-coded.
    /// </summary>
    public sealed class LinearDivisionRule
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Standard (full) panel length, mm.</summary>
        public double StandardPanelLengthMm { get; set; }
        /// <summary>Panel height, mm (reported, not used in the division).</summary>
        public double PanelHeightMm { get; set; }
        /// <summary>Gap at each panel end (column-panel and panel-panel), mm.</summary>
        public double GapMm { get; set; }
        /// <summary>Shortest acceptable cut panel, mm. A shorter remainder is reported, not modelled.</summary>
        public double MinPanelLengthMm { get; set; }

        /// <summary>Column size measured ALONG the wall, mm (e.g. 400 for a 400x200 column set this way).</summary>
        public double ColumnWidthAlongWallMm { get; set; }
        public double ColumnWidthAcrossWallMm { get; set; }
        /// <summary>Allowed column centre-to-centre spacing, mm.</summary>
        public double ColumnSpacingMinMm { get; set; }
        public double ColumnSpacingMaxMm { get; set; }

        public List<string> Notes { get; set; } = new List<string>();

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Name)) errors.Add("Name is empty.");
            if (StandardPanelLengthMm <= 0) errors.Add("StandardPanelLengthMm must be > 0.");
            if (GapMm < 0) errors.Add("GapMm must be >= 0.");
            if (MinPanelLengthMm < 0) errors.Add("MinPanelLengthMm must be >= 0.");
            if (MinPanelLengthMm > StandardPanelLengthMm) errors.Add("MinPanelLengthMm must not exceed StandardPanelLengthMm.");
            if (ColumnWidthAlongWallMm < 0) errors.Add("ColumnWidthAlongWallMm must be >= 0.");
            if (ColumnSpacingMinMm < 0 || ColumnSpacingMaxMm < ColumnSpacingMinMm)
                errors.Add("Column spacing range is invalid (need 0 <= min <= max).");
            return errors;
        }
    }

    public sealed class PanelPiece
    {
        public int Index { get; set; }
        /// <summary>"standard" or "cut".</summary>
        public string Kind { get; set; } = "standard";
        /// <summary>Distance from the start of the clear opening to the panel start, mm.</summary>
        public double StartOffsetMm { get; set; }
        public double EndOffsetMm { get; set; }
        public double LengthMm { get; set; }
    }

    public sealed class PanelDivision
    {
        public double ClearLengthMm { get; set; }
        public int StandardPanels { get; set; }
        public int CutPanels { get; set; }
        public double CutPanelLengthMm { get; set; }
        public int Gaps { get; set; }
        /// <summary>Length left over that is not filled by a panel (0 when the opening is filled exactly).</summary>
        public double UnfilledMm { get; set; }
        public List<PanelPiece> Panels { get; set; } = new List<PanelPiece>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class WallBay
    {
        public int Index { get; set; }
        public double ColumnCentreStartMm { get; set; }
        public double ColumnCentreEndMm { get; set; }
        public double SpacingMm { get; set; }
        public PanelDivision Division { get; set; } = new PanelDivision();
    }

    public sealed class WallPlan
    {
        public string Rule { get; set; } = "";
        public double TotalLengthMm { get; set; }
        public int Columns { get; set; }
        public int Bays { get; set; }
        public double ColumnSpacingMm { get; set; }
        public bool SpacingWithinRule { get; set; }
        public int TotalStandardPanels { get; set; }
        public int TotalCutPanels { get; set; }
        public List<double> ColumnCentresMm { get; set; } = new List<double>();
        public List<WallBay> BayDetails { get; set; } = new List<WallBay>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>The calculation engine for <see cref="LinearDivisionRule"/>. Pure and unit-tested.</summary>
    public static class LinearDivision
    {
        private const double Eps = 1e-6;

        /// <summary>
        /// Fills a clear opening (face of column to face of column) with standard panels, a gap
        /// at every panel end. If the remainder can take a panel of at least MinPanelLengthMm,
        /// one cut panel is added; otherwise the remainder is reported as unfilled.
        /// </summary>
        public static PanelDivision DivideOpening(double clearLengthMm, LinearDivisionRule rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            var ruleErrors = rule.Validate();
            if (ruleErrors.Count > 0) throw new ArgumentException("Invalid rule: " + string.Join(" ", ruleErrors));
            if (double.IsNaN(clearLengthMm) || clearLengthMm <= 0) throw new ArgumentException("Clear length must be > 0 mm.");

            var p = rule.StandardPanelLengthMm;
            var g = rule.GapMm;
            var result = new PanelDivision { ClearLengthMm = clearLengthMm };

            // n panels need n*p + (n+1)*g
            var n = (int)Math.Floor((clearLengthMm - g + Eps) / (p + g));
            if (n < 0) n = 0;
            var used = n * p + (n + 1) * g;
            var remainder = clearLengthMm - used;
            if (remainder < 0) { n = 0; used = g; remainder = clearLengthMm - g; }

            double cursor = g;
            for (var i = 0; i < n; i++)
            {
                result.Panels.Add(new PanelPiece { Index = i + 1, Kind = "standard", StartOffsetMm = Round(cursor), EndOffsetMm = Round(cursor + p), LengthMm = p });
                cursor += p + g;
            }
            result.StandardPanels = n;
            result.Gaps = n + 1;

            // A cut panel needs its own extra gap.
            var cutLength = remainder - g;
            if (remainder > Eps && cutLength >= rule.MinPanelLengthMm - Eps && cutLength > Eps)
            {
                result.Panels.Add(new PanelPiece { Index = n + 1, Kind = "cut", StartOffsetMm = Round(cursor), EndOffsetMm = Round(cursor + cutLength), LengthMm = Round(cutLength) });
                result.CutPanels = 1;
                result.CutPanelLengthMm = Round(cutLength);
                result.Gaps = n + 2;
                result.UnfilledMm = 0;
            }
            else
            {
                result.UnfilledMm = Round(Math.Max(0, remainder));
                if (result.UnfilledMm > Eps)
                    result.Warnings.Add($"{result.UnfilledMm} mm is left unfilled: too short for a panel of at least {rule.MinPanelLengthMm} mm. Adjust the column position or the gaps.");
            }

            if (n == 0 && result.CutPanels == 0)
                result.Warnings.Add("The opening is too short for any panel under this rule.");
            return result;
        }

        /// <summary>
        /// Plans a straight wall of <paramref name="totalLengthMm"/> measured between the centres
        /// of the two END columns: picks the fewest bays whose equal spacing does not exceed the
        /// rule maximum, checks the minimum, and divides each bay's clear opening into panels.
        /// </summary>
        public static WallPlan PlanWall(double totalLengthMm, LinearDivisionRule rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            if (double.IsNaN(totalLengthMm) || totalLengthMm <= 0) throw new ArgumentException("Wall length must be > 0 mm.");
            if (rule.ColumnSpacingMaxMm <= 0) throw new ArgumentException("This rule has no column spacing (ColumnSpacingMaxMm). Use DivideOpening instead.");

            var plan = new WallPlan { Rule = rule.Name, TotalLengthMm = totalLengthMm };
            var bays = (int)Math.Ceiling(totalLengthMm / rule.ColumnSpacingMaxMm - Eps);
            if (bays < 1) bays = 1;
            var spacing = totalLengthMm / bays;

            plan.Bays = bays;
            plan.Columns = bays + 1;
            plan.ColumnSpacingMm = Round(spacing);
            plan.SpacingWithinRule = spacing >= rule.ColumnSpacingMinMm - Eps && spacing <= rule.ColumnSpacingMaxMm + Eps;
            if (!plan.SpacingWithinRule)
                plan.Warnings.Add($"Equal spacing {Round(spacing)} mm is outside the rule range {rule.ColumnSpacingMinMm}-{rule.ColumnSpacingMaxMm} mm for a {totalLengthMm} mm wall. Check the wall length or use unequal bays.");

            for (var i = 0; i <= bays; i++) plan.ColumnCentresMm.Add(Round(i * spacing));

            var clear = spacing - rule.ColumnWidthAlongWallMm;
            for (var i = 0; i < bays; i++)
            {
                var bay = new WallBay
                {
                    Index = i + 1,
                    ColumnCentreStartMm = plan.ColumnCentresMm[i],
                    ColumnCentreEndMm = plan.ColumnCentresMm[i + 1],
                    SpacingMm = Round(spacing),
                    Division = DivideOpening(clear, rule),
                };
                plan.BayDetails.Add(bay);
                plan.TotalStandardPanels += bay.Division.StandardPanels;
                plan.TotalCutPanels += bay.Division.CutPanels;
            }

            foreach (var w in plan.BayDetails.SelectMany(b => b.Division.Warnings).Distinct())
                plan.Warnings.Add("Each bay: " + w);
            return plan;
        }

        /// <summary>Checks measured column centre spacings against the rule. Returns one line per problem.</summary>
        public static List<string> CheckSpacings(IEnumerable<double> spacingsMm, LinearDivisionRule rule)
        {
            var problems = new List<string>();
            var i = 0;
            foreach (var s in spacingsMm)
            {
                i++;
                if (s < rule.ColumnSpacingMinMm - Eps) problems.Add($"Bay {i}: spacing {Round(s)} mm is below the minimum {rule.ColumnSpacingMinMm} mm.");
                else if (s > rule.ColumnSpacingMaxMm + Eps) problems.Add($"Bay {i}: spacing {Round(s)} mm is above the maximum {rule.ColumnSpacingMaxMm} mm.");
            }
            return problems;
        }

        private static double Round(double v) => Math.Round(v, 2);
    }

    /// <summary>Loads rule files from the rules folder.</summary>
    public sealed class RuleRepository
    {
        public string Directory { get; }

        public RuleRepository(string directory)
        {
            Directory = directory;
        }

        public List<LinearDivisionRule> LoadAll()
        {
            var rules = new List<LinearDivisionRule>();
            if (!System.IO.Directory.Exists(Directory)) return rules;
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rule = JsonSerializer.Deserialize<LinearDivisionRule>(File.ReadAllText(file), ServerSettings.JsonOptions);
                if (rule == null) continue;
                if (string.IsNullOrWhiteSpace(rule.Name)) rule.Name = Path.GetFileNameWithoutExtension(file);
                rules.Add(rule);
            }
            return rules;
        }

        public LinearDivisionRule Get(string name)
        {
            var all = LoadAll();
            var rule = all.FirstOrDefault(r => string.Equals(r.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule == null)
                throw new ArgumentException($"Rule '{name}' not found in {Directory}. Available: {(all.Count == 0 ? "(none)" : string.Join(", ", all.Select(r => r.Name)))}.");
            return rule;
        }
    }
}
