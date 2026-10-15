using System;
using System.Collections.Generic;
using System.Linq;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Rules;

namespace SherinTeklaMcp.Core.Validation
{
    public sealed class ValidationIssue
    {
        /// <summary>error | warning</summary>
        public string Severity { get; set; } = "warning";
        public string Check { get; set; } = "";
        public string Guid { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class ValidationReport
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int Checked { get; set; }
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public bool SourceTruncated { get; set; }
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
        /// <summary>GUIDs with at least one issue - pass to tekla_select_objects / tekla_highlight_objects.</summary>
        public List<string> ProblemGuids { get; set; } = new List<string>();
    }

    /// <summary>Read-only checks on part data. Pure logic, unit-tested.</summary>
    public static class ModelValidator
    {
        public static ValidationReport Validate(IReadOnlyList<TeklaObjectInfo> parts, double minLengthMm, LinearDivisionRule? panelRule = null)
        {
            var report = new ValidationReport { Checked = parts.Count };
            void Add(string severity, string check, string guid, string message) =>
                report.Issues.Add(new ValidationIssue { Severity = severity, Check = check, Guid = guid, Message = message });

            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p.Profile)) Add("error", "missing-profile", p.Guid, "Part has no profile.");
                if (string.IsNullOrWhiteSpace(p.Material)) Add("error", "missing-material", p.Guid, "Part has no material.");
                if (string.IsNullOrWhiteSpace(p.Class)) Add("warning", "missing-class", p.Guid, "Part has no class.");
                if (string.IsNullOrWhiteSpace(p.Name)) Add("warning", "missing-name", p.Guid, "Part has no name.");
                var len = p.EffectiveLengthMm;
                if (len.HasValue && len.Value < minLengthMm)
                    Add("error", "short-member", p.Guid, $"Length {Math.Round(len.Value, 1)} mm is below the minimum {minLengthMm} mm.");
                if (p.Start != null && p.End != null && !p.Start.IsFinite())
                    Add("error", "bad-geometry", p.Guid, "Start point is not a valid number.");
            }

            // Duplicates: same type and profile on the same line (either direction).
            var lined = parts.Where(p => p.Start != null && p.End != null).ToList();
            for (var i = 0; i < lined.Count; i++)
            {
                for (var j = i + 1; j < lined.Count; j++)
                {
                    var a = lined[i];
                    var b = lined[j];
                    if (!string.Equals(a.Type, b.Type, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(a.Profile, b.Profile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (GeometryMath.SameLine(a.Start!, a.End!, b.Start!, b.End!))
                        Add("error", "duplicate", b.Guid, $"Duplicate of {a.Guid}: same type, profile and position.");
                }
            }

            // Optional rule check: panel lengths (parts named like the rule's panels are not assumed;
            // the caller passes only the panels it wants checked).
            if (panelRule != null)
            {
                foreach (var p in parts)
                {
                    var len = p.EffectiveLengthMm;
                    if (!len.HasValue) continue;
                    if (len.Value > panelRule.StandardPanelLengthMm + 0.5)
                        Add("error", "panel-too-long", p.Guid, $"Panel length {Math.Round(len.Value, 1)} mm exceeds the standard {panelRule.StandardPanelLengthMm} mm ({panelRule.Name}).");
                    else if (len.Value < panelRule.MinPanelLengthMm - 0.5)
                        Add("error", "panel-too-short", p.Guid, $"Panel length {Math.Round(len.Value, 1)} mm is below the minimum {panelRule.MinPanelLengthMm} mm ({panelRule.Name}).");
                }
            }

            report.Errors = report.Issues.Count(x => x.Severity == "error");
            report.Warnings = report.Issues.Count(x => x.Severity == "warning");
            report.ProblemGuids = report.Issues.Select(x => x.Guid).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            if (report.Errors > 0) report.Status = "issues-found";
            return report;
        }
    }
}
