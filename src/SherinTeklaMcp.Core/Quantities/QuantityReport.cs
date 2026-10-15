using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SherinTeklaMcp.Core.Models;

namespace SherinTeklaMcp.Core.Quantities
{
    public sealed class QuantityRow
    {
        public string Profile { get; set; } = "";
        public string Material { get; set; } = "";
        public string Name { get; set; } = "";
        public string Class { get; set; } = "";
        public int Quantity { get; set; }
        public double RunningLengthMm { get; set; }
        public double WeightKg { get; set; }
        /// <summary>Parts in this group that had no weight from Tekla (weight total is then a lower bound).</summary>
        public int MissingWeight { get; set; }
        public int MissingLength { get; set; }
        public double RunningLengthM => Math.Round(RunningLengthMm / 1000.0, 3);
    }

    public sealed class QuantityReportResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public string GroupBy { get; set; } = "";
        public int TotalQuantity { get; set; }
        public double TotalRunningLengthM { get; set; }
        public double TotalWeightKg { get; set; }
        public bool SourceTruncated { get; set; }
        public List<QuantityRow> Rows { get; set; } = new List<QuantityRow>();
        public string CsvFile { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Groups parts and totals quantity, running length and weight - the
    /// "Profile | Material | Quantity | Running Length | Weight" report. Independent of the AI
    /// and of Tekla: it only works on <see cref="TeklaObjectInfo"/> rows.
    /// </summary>
    public static class QuantityReport
    {
        public static readonly string[] SupportedFields = { "profile", "material", "name", "class" };

        /// <summary>Parses "profile,material" style input. Unknown fields throw.</summary>
        public static List<string> ParseGroupBy(string? groupBy)
        {
            var fields = (string.IsNullOrWhiteSpace(groupBy) ? "profile,material" : groupBy!)
                .Split(new[] { ',', ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
            foreach (var f in fields)
                if (!SupportedFields.Contains(f))
                    throw new ArgumentException($"Unknown groupBy field '{f}'. Use any of: {string.Join(", ", SupportedFields)}.");
            return fields;
        }

        public static QuantityReportResult Build(IEnumerable<TeklaObjectInfo> parts, string? groupBy)
        {
            var fields = ParseGroupBy(groupBy);
            var result = new QuantityReportResult { GroupBy = string.Join(",", fields) };
            var groups = new Dictionary<string, QuantityRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in parts)
            {
                var key = string.Join("|", fields.Select(f => Field(p, f)));
                if (!groups.TryGetValue(key, out var row))
                {
                    row = new QuantityRow();
                    if (fields.Contains("profile")) row.Profile = p.Profile;
                    if (fields.Contains("material")) row.Material = p.Material;
                    if (fields.Contains("name")) row.Name = p.Name;
                    if (fields.Contains("class")) row.Class = p.Class;
                    groups[key] = row;
                }

                row.Quantity++;
                if (p.EffectiveLengthMm.HasValue) row.RunningLengthMm += p.EffectiveLengthMm.Value;
                else row.MissingLength++;
                if (p.WeightKg.HasValue) row.WeightKg += p.WeightKg.Value;
                else row.MissingWeight++;
            }

            result.Rows = groups.Values
                .OrderBy(r => r.Profile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Material, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Class, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var r in result.Rows) r.WeightKg = Math.Round(r.WeightKg, 2);

            result.TotalQuantity = result.Rows.Sum(r => r.Quantity);
            result.TotalRunningLengthM = Math.Round(result.Rows.Sum(r => r.RunningLengthMm) / 1000.0, 3);
            result.TotalWeightKg = Math.Round(result.Rows.Sum(r => r.WeightKg), 2);

            var missingW = result.Rows.Sum(r => r.MissingWeight);
            var missingL = result.Rows.Sum(r => r.MissingLength);
            if (missingW > 0) result.Warnings.Add($"{missingW} part(s) had no WEIGHT from Tekla - weight totals are a lower bound.");
            if (missingL > 0) result.Warnings.Add($"{missingL} part(s) had no length - running length totals are a lower bound.");
            return result;
        }

        public static string ToCsv(QuantityReportResult report)
        {
            var fields = ParseGroupBy(report.GroupBy);
            var sb = new StringBuilder();
            var header = fields.Select(f => char.ToUpperInvariant(f[0]) + f.Substring(1)).ToList();
            header.AddRange(new[] { "Quantity", "Running Length (m)", "Weight (kg)" });
            sb.AppendLine(string.Join(",", header.Select(Csv)));
            foreach (var r in report.Rows)
            {
                var cells = fields.Select(f => f == "profile" ? r.Profile : f == "material" ? r.Material : f == "name" ? r.Name : r.Class).ToList();
                cells.Add(r.Quantity.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.RunningLengthM.ToString("0.000", CultureInfo.InvariantCulture));
                cells.Add(r.WeightKg.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine(string.Join(",", cells.Select(Csv)));
            }
            var total = fields.Select((f, i) => i == 0 ? "TOTAL" : "").ToList();
            total.Add(report.TotalQuantity.ToString(CultureInfo.InvariantCulture));
            total.Add(report.TotalRunningLengthM.ToString("0.000", CultureInfo.InvariantCulture));
            total.Add(report.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine(string.Join(",", total.Select(Csv)));
            return sb.ToString();
        }

        private static string Field(TeklaObjectInfo p, string f) =>
            f == "profile" ? p.Profile : f == "material" ? p.Material : f == "name" ? p.Name : p.Class;

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
