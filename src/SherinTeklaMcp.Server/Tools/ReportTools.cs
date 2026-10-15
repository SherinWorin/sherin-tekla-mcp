using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;
using SherinTeklaMcp.Core;
using SherinTeklaMcp.Core.Operations;
using SherinTeklaMcp.Core.Quantities;
using SherinTeklaMcp.Core.Rules;
using SherinTeklaMcp.Core.Settings;
using SherinTeklaMcp.Core.Validation;

namespace SherinTeklaMcp.Server.Tools
{
    /// <summary>LOW risk: quantity reports (BOQ) and model checks.</summary>
    [McpServerToolType]
    public static class ReportTools
    {
        [McpServerTool(Name = "tekla_quantity_report")]
        [Description("[LOW risk, read-only] Quantity / BOQ report: groups parts and totals quantity, running length (m, from Tekla LENGTH) and weight (kg, from Tekla WEIGHT). " +
                     "Example: 'total running length of each profile' -> groupBy='profile'. Set exportCsv=true to also save a CSV file (Excel opens it).")]
        public static QuantityReportResult QuantityReport(
            ITeklaService tekla,
            ServerSettings settings,
            [Description(ToolHelpers.Source)] string source = "selected",
            [Description(ToolHelpers.GuidList + " Used when source='guids'.")] string guids = "",
            [Description("Group fields separated by commas: profile, material, name, class. Default 'profile,material'.")] string groupBy = "profile,material",
            [Description("Filter (source='filter'): " + ToolHelpers.Types)] string types = "",
            [Description("Filter: exact profile.")] string profile = "",
            [Description("Filter: part of the profile name.")] string profileContains = "",
            [Description("Filter: exact material.")] string material = "",
            [Description("Filter: exact Tekla class.")] string teklaClass = "",
            [Description("Filter: part of the name.")] string nameContains = "",
            [Description("Filter: " + ToolHelpers.Orientation)] string orientation = "",
            [Description("Also write the report to a CSV file in the export folder.")] bool exportCsv = false)
        {
            try
            {
                var filter = ToolInputs.BuildFilter(types, profile, profileContains, material, teklaClass, nameContains, orientation);
                var parts = ToolInputs.ResolveSource(tekla, settings, source, guids, filter);
                if (parts.Status == "error") return new QuantityReportResult { Status = "error", Message = parts.Message };

                var onlyParts = parts.Objects.Where(o => o.IsPart).ToList();
                var report = Core.Quantities.QuantityReport.Build(onlyParts, groupBy);
                report.SourceTruncated = parts.Truncated;
                if (onlyParts.Count < parts.Objects.Count)
                    report.Warnings.Add($"{parts.Objects.Count - onlyParts.Count} non-part object(s) (bolts, welds, components...) were not counted.");
                if (parts.Truncated) report.Warnings.Add("Not all matching objects were read (" + parts.Message + "). Totals cover only the objects read.");
                if (parts.NotFound.Count > 0) report.Warnings.Add("GUIDs not found: " + string.Join(", ", parts.NotFound));
                report.Message = $"{report.TotalQuantity} part(s) in {report.Rows.Count} group(s).";

                if (exportCsv && report.Rows.Count > 0)
                {
                    var dir = settings.ResolveExportDirectory();
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, "quantity-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
                    // UTF-8 with BOM so Excel shows special characters correctly.
                    File.WriteAllText(file, Core.Quantities.QuantityReport.ToCsv(report), new UTF8Encoding(true));
                    report.CsvFile = file;
                }
                return report;
            }
            catch (Exception ex) { return new QuantityReportResult { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }

        [McpServerTool(Name = "tekla_validate_objects")]
        [Description("[LOW risk, read-only] Check parts for problems: missing profile/material/class/name, members shorter than the minimum, duplicates (same type and profile on the same line). " +
                     "Optionally check panel lengths against a rule (panelRule). Returns problemGuids to pass to tekla_highlight_objects.")]
        public static ValidationReport ValidateObjects(
            ITeklaService tekla,
            ServerSettings settings,
            RuleRepository rules,
            [Description(ToolHelpers.Source)] string source = "selected",
            [Description(ToolHelpers.GuidList + " Used when source='guids'.")] string guids = "",
            [Description("Filter (source='filter'): " + ToolHelpers.Types)] string types = "",
            [Description("Filter: exact profile.")] string profile = "",
            [Description("Filter: part of the profile name.")] string profileContains = "",
            [Description("Filter: exact material.")] string material = "",
            [Description("Filter: exact Tekla class.")] string teklaClass = "",
            [Description("Filter: part of the name.")] string nameContains = "",
            [Description("Filter: " + ToolHelpers.Orientation)] string orientation = "",
            [Description("Optional rule name (see tekla_list_rules) to check these parts as panels against.")] string panelRule = "")
        {
            try
            {
                var filter = ToolInputs.BuildFilter(types, profile, profileContains, material, teklaClass, nameContains, orientation);
                var parts = ToolInputs.ResolveSource(tekla, settings, source, guids, filter);
                if (parts.Status == "error") return new ValidationReport { Status = "error", Message = parts.Message };

                var rule = string.IsNullOrWhiteSpace(panelRule) ? null : rules.Get(panelRule);
                var onlyParts = parts.Objects.Where(o => o.IsPart).ToList();
                var report = ModelValidator.Validate(onlyParts, settings.MinMemberLengthMm, rule);
                report.SourceTruncated = parts.Truncated;
                var skipped = parts.Objects.Count - onlyParts.Count;
                report.Message = report.Errors == 0 && report.Warnings == 0
                    ? $"No problems found in {report.Checked} part(s)."
                    : $"{report.Errors} error(s) and {report.Warnings} warning(s) in {report.Checked} part(s).";
                if (skipped > 0) report.Message += $" {skipped} non-part object(s) (bolts, welds, components...) were skipped.";
                if (parts.Truncated) report.Message += " Not all objects were checked (" + parts.Message + ").";
                return report;
            }
            catch (Exception ex) { return new ValidationReport { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }
    }
}
