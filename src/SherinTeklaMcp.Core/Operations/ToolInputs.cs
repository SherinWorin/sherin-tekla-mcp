using System;
using System.Collections.Generic;
using System.Linq;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Core.Operations
{
    /// <summary>Parsing of tool arguments. Lives in Core so it is unit-tested.</summary>
    public static class ToolInputs
    {
        public static List<string> ParseGuids(string? text) =>
            (text ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().Trim('"', '\'', '{', '}', '[', ']'))
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Parses "NAME=value; NAME2=value2". Throws on a malformed pair.</summary>
        public static Dictionary<string, string> ParseUdas(string? text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (text ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = pair.Trim();
                if (trimmed.Length == 0) continue;
                var eq = trimmed.IndexOf('=');
                if (eq <= 0) throw new ArgumentException($"UDA '{trimmed}' must be written NAME=value.");
                result[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim();
            }
            return result;
        }

        public static ObjectFilter BuildFilter(string? types, string? profile, string? profileContains, string? material,
                                              string? teklaClass, string? nameContains, string? orientation)
        {
            var o = (orientation ?? "").Trim().ToLowerInvariant();
            if (o.Length > 0 && o != "vertical" && o != "nonvertical")
                throw new ArgumentException($"orientation must be 'vertical', 'nonvertical' or empty, got '{orientation}'.");
            return new ObjectFilter
            {
                Types = (types ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList(),
                ProfileEquals = (profile ?? "").Trim(),
                ProfileContains = (profileContains ?? "").Trim(),
                Material = (material ?? "").Trim(),
                Class = (teklaClass ?? "").Trim(),
                NameContains = (nameContains ?? "").Trim(),
                Orientation = o,
            };
        }

        /// <summary>The parts a report/validation works on: the selection, a GUID list or a filter.</summary>
        public static ObjectQueryResult ResolveSource(ITeklaService tekla, ServerSettings settings, string? source, string? guids, ObjectFilter filter)
        {
            var mode = string.IsNullOrWhiteSpace(source) ? "selected" : source!.Trim().ToLowerInvariant();
            switch (mode)
            {
                case "selected":
                    return tekla.GetSelectedObjects(includeUdas: false, limit: settings.MaxObjectsPerRead);
                case "guids":
                    var ids = ParseGuids(guids);
                    if (ids.Count == 0) throw new ArgumentException("source='guids' needs the guids argument.");
                    if (ids.Count > settings.MaxObjectsPerRead) throw new ArgumentException($"Too many GUIDs ({ids.Count}); the limit is {settings.MaxObjectsPerRead}.");
                    return tekla.GetObjects(ids, includeUdas: false);
                case "filter":
                    if (filter.IsEmpty) throw new ArgumentException("source='filter' needs at least one filter argument (types, profile, material, class, name, orientation).");
                    return tekla.FindObjects(filter, includeUdas: false, limit: settings.MaxObjectsPerRead, maxScan: settings.MaxObjectsToScan);
                default:
                    throw new ArgumentException($"Unknown source '{source}'. Use selected, guids or filter.");
            }
        }

        public static int ClampLimit(int limit, ServerSettings settings) =>
            limit <= 0 ? settings.MaxObjectsPerRead : Math.Min(limit, settings.MaxObjectsPerRead);
    }
}
