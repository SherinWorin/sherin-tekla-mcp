using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Logging;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Safety;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Core.Operations
{
    /// <summary>A column given by its base point and height (global mm).</summary>
    public sealed class ColumnSpec
    {
        /// <summary>Base point "x,y,z" in mm, global.</summary>
        public string Base { get; set; } = "";
        public double HeightMm { get; set; }
        public string Profile { get; set; } = "";
        public string Material { get; set; } = "";
        public string Class { get; set; } = "";
        public string Name { get; set; } = "";
        public string PositionPlane { get; set; } = "";
        public string PositionDepth { get; set; } = "";
        public string PositionRotation { get; set; } = "";
    }

    /// <summary>
    /// The safe write pipeline used by EVERY model-changing tool:
    /// input check -> safety policy (read-only, delete, limits) -> catalog check -> risk level ->
    /// PREVIEW (plan + confirm token, nothing written) -> APPLY (token verified for the same
    /// arguments) -> Tekla write -> commit -> read back and verify -> operation log.
    /// Backend-independent, so it is unit-tested with the mock backend.
    /// </summary>
    public sealed class WriteOperations
    {
        private static readonly JsonSerializerOptions CanonicalJson = new JsonSerializerOptions { WriteIndented = false };

        private readonly ITeklaService _tekla;
        private readonly ServerSettings _settings;
        private readonly ConfirmationService _confirm;
        private readonly OperationLog _log;

        public WriteOperations(ITeklaService tekla, ServerSettings settings, ConfirmationService confirm, OperationLog log)
        {
            _tekla = tekla;
            _settings = settings;
            _confirm = confirm;
            _log = log;
        }

        // ------------------------------------------------------------------ create

        public WriteResult CreateBeams(IReadOnlyList<PartSpec>? specs, bool apply, string? confirmToken, string? intent) =>
            CreateParts("create_beams", Normalize(specs), apply, confirmToken, intent, "beam");

        public WriteResult CreateColumns(IReadOnlyList<ColumnSpec>? columns, bool apply, string? confirmToken, string? intent)
        {
            var result = NewResult("create_columns");
            var specs = new List<PartSpec>();
            var i = 0;
            foreach (var c in columns ?? Array.Empty<ColumnSpec>())
            {
                i++;
                var basePoint = Point3.TryParse(c?.Base);
                if (c == null || basePoint == null)
                {
                    result.Errors.Add($"Column {i}: base point must be 'x,y,z' in mm.");
                    continue;
                }
                if (double.IsNaN(c.HeightMm) || c.HeightMm <= 0)
                {
                    result.Errors.Add($"Column {i}: HeightMm must be > 0.");
                    continue;
                }
                var top = new Point3(basePoint.X, basePoint.Y, basePoint.Z + c.HeightMm);
                specs.Add(new PartSpec
                {
                    Start = basePoint.ToString(),
                    End = top.ToString(),
                    Profile = c.Profile,
                    Material = c.Material,
                    Class = c.Class,
                    Name = c.Name,
                    PositionPlane = c.PositionPlane,
                    PositionDepth = c.PositionDepth,
                    PositionRotation = c.PositionRotation,
                });
            }
            if (result.Errors.Count > 0) return Refuse(result, intent, "Invalid column input. Nothing was changed.");
            return CreateParts("create_columns", Normalize(specs), apply, confirmToken, intent, "column");
        }

        private WriteResult CreateParts(string operation, List<PartSpec> specs, bool apply, string? confirmToken, string? intent, string noun)
        {
            var result = NewResult(operation);
            result.Risk = SafetyPolicy.Classify(WriteKind.Create, specs.Count, _settings);

            var refusal = SafetyPolicy.CheckAllowed(WriteKind.Create, specs.Count, _settings);
            if (refusal != null) return Refuse(result, intent, refusal);

            for (var i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                var start = Point3.TryParse(s.Start);
                var end = Point3.TryParse(s.End);
                var label = $"{Capitalize(noun)} {i + 1}";
                if (start == null || end == null) { result.Errors.Add($"{label}: start and end must be 'x,y,z' in mm."); continue; }
                var length = start.DistanceTo(end);
                if (length < Math.Max(_settings.MinMemberLengthMm, 0.001)) result.Errors.Add($"{label}: length {Math.Round(length, 1)} mm is below the minimum {_settings.MinMemberLengthMm} mm.");
                if (string.IsNullOrWhiteSpace(s.Profile)) result.Errors.Add($"{label}: profile is required.");
                if (string.IsNullOrWhiteSpace(s.Material)) result.Errors.Add($"{label}: material is required.");
                if (!string.IsNullOrWhiteSpace(s.Class) && !int.TryParse(s.Class, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    result.Errors.Add($"{label}: class must be a whole number, got '{s.Class}'.");
                AddPositionErrors(result, label, s);

                result.Plan.Add($"{label}: {s.Profile} {s.Material}" +
                                (string.IsNullOrWhiteSpace(s.Class) ? "" : $" class {s.Class}") +
                                (string.IsNullOrWhiteSpace(s.Name) ? "" : $" name {s.Name}") +
                                $" from ({s.Start}) to ({s.End}), length {Math.Round(length, 1)} mm (global coordinates).");
            }
            if (result.Errors.Count > 0) return Refuse(result, intent, "Invalid input. Nothing was changed.");

            CheckCatalogs(result, specs.Select(s => s.Profile), specs.Select(s => s.Material));
            if (result.Errors.Count > 0) return Refuse(result, intent, "Profile or material not found in the Tekla catalog. Nothing was changed.");

            var canonical = Canonical(new { operation, specs });
            if (!apply) return Preview(result, intent, canonical, $"Will create {specs.Count} {noun}(s).");

            var tokenProblem = _confirm.Verify(confirmToken, operation, canonical);
            if (tokenProblem != null) return Refuse(result, intent, tokenProblem);

            for (var i = 0; i < specs.Count; i++)
            {
                try
                {
                    var created = _tekla.InsertBeam(specs[i]);
                    result.Created.Add(created);
                    result.AffectedGuids.Add(created.Guid);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{Capitalize(noun)} {i + 1}: {ex.Message}");
                }
            }

            CommitIfNeeded(result);
            if (result.Created.Count > 0) VerifyCreated(result, specs);
            return Finish(result, intent, canonical, result.Created.Count, specs.Count, $"Created {result.Created.Count} of {specs.Count} {noun}(s).");
        }

        // ------------------------------------------------------------------ modify

        public WriteResult ModifyObjects(IReadOnlyList<string>? guids, ModifySpec? spec, bool apply, string? confirmToken, string? intent)
        {
            var result = NewResult("modify_objects");
            var ids = NormalizeGuids(guids);
            spec = Normalize(spec ?? new ModifySpec());
            result.Risk = SafetyPolicy.Classify(WriteKind.Modify, ids.Count, _settings);

            var refusal = SafetyPolicy.CheckAllowed(WriteKind.Modify, ids.Count, _settings);
            if (refusal != null) return Refuse(result, intent, refusal);
            if (spec.IsEmpty) return Refuse(result, intent, "No changes given (profile, material, class, name, finish or UDAs).");
            if (!string.IsNullOrWhiteSpace(spec.Class) && !int.TryParse(spec.Class, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return Refuse(result, intent, $"Class must be a whole number, got '{spec.Class}'.");

            var current = _tekla.GetObjects(ids, includeUdas: spec.Udas.Count > 0);
            if (current.NotFound.Count > 0)
            {
                result.Errors.Add("Not found in the model: " + string.Join(", ", current.NotFound));
                return Refuse(result, intent, "Some objects were not found. Nothing was changed.");
            }

            CheckCatalogs(result,
                string.IsNullOrWhiteSpace(spec.Profile) ? Array.Empty<string>() : new[] { spec.Profile },
                string.IsNullOrWhiteSpace(spec.Material) ? Array.Empty<string>() : new[] { spec.Material });
            if (result.Errors.Count > 0) return Refuse(result, intent, "Profile or material not found in the Tekla catalog. Nothing was changed.");

            foreach (var o in current.Objects)
            {
                AddPlannedChange(result, o.Guid, "Profile", o.Profile, spec.Profile);
                AddPlannedChange(result, o.Guid, "Material", o.Material, spec.Material);
                AddPlannedChange(result, o.Guid, "Class", o.Class, spec.Class);
                AddPlannedChange(result, o.Guid, "Name", o.Name, spec.Name);
                AddPlannedChange(result, o.Guid, "Finish", o.Finish, spec.Finish);
                foreach (var uda in spec.Udas)
                {
                    var before = o.Udas != null && o.Udas.TryGetValue(uda.Key, out var v) ? v : "";
                    AddPlannedChange(result, o.Guid, "UDA:" + uda.Key, before, uda.Value);
                }
            }
            result.AffectedGuids = ids.ToList();
            result.Plan.Add($"Modify {ids.Count} object(s): {DescribeSpec(spec)}.");
            var unchanged = ids.Count - result.Changes.Select(c => c.Guid).Distinct().Count();
            if (unchanged > 0) result.Warnings.Add($"{unchanged} object(s) already have these values.");
            if (result.Changes.Count == 0) return Refuse(result, intent, "Every object already has these values. Nothing to change.");

            var canonical = Canonical(new { operation = "modify_objects", guids = ids.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList(), spec = CanonicalSpec(spec) });
            if (!apply) return Preview(result, intent, canonical, $"Will change {result.Changes.Count} field(s) on {ids.Count} object(s).");

            var tokenProblem = _confirm.Verify(confirmToken, "modify_objects", canonical);
            if (tokenProblem != null) return Refuse(result, intent, tokenProblem);

            var planned = result.Changes;
            result.Changes = new List<FieldChange>();
            var done = 0;
            foreach (var id in ids)
            {
                try
                {
                    result.Changes.AddRange(_tekla.ModifyObject(id, spec));
                    done++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{id}: {ex.Message}");
                }
            }

            CommitIfNeeded(result);
            if (done > 0) VerifyModified(result, ids, spec);
            if (result.Changes.Count == 0 && done > 0) result.Changes = planned;
            return Finish(result, intent, canonical, done, ids.Count, $"Modified {done} of {ids.Count} object(s).");
        }

        // ------------------------------------------------------------------ delete

        public WriteResult DeleteObjects(IReadOnlyList<string>? guids, bool apply, string? confirmToken, string? intent)
        {
            var result = NewResult("delete_objects");
            var ids = NormalizeGuids(guids);
            result.Risk = RiskLevel.High;

            var refusal = SafetyPolicy.CheckAllowed(WriteKind.Delete, ids.Count, _settings);
            if (refusal != null) return Refuse(result, intent, refusal);

            var current = _tekla.GetObjects(ids, includeUdas: false);
            if (current.NotFound.Count > 0)
            {
                result.Errors.Add("Not found in the model: " + string.Join(", ", current.NotFound));
                return Refuse(result, intent, "Some objects were not found. Nothing was deleted.");
            }

            foreach (var o in current.Objects)
            {
                result.Plan.Add($"Delete {o.Type} {o.Guid}: {o.Profile} {o.Material} '{o.Name}'" +
                                (o.EffectiveLengthMm.HasValue ? $", length {Math.Round(o.EffectiveLengthMm.Value, 1)} mm" : ""));
                result.Changes.Add(new FieldChange { Guid = o.Guid, Field = "Object", Before = $"{o.Type} {o.Profile} {o.Material}", After = "(deleted)" });
            }
            result.AffectedGuids = ids.ToList();
            result.Warnings.Add("HIGH risk: deleted objects can only be restored with Undo in Tekla (Ctrl+Z) before closing the model, or from a backup.");

            var canonical = Canonical(new { operation = "delete_objects", guids = ids.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList() });
            if (!apply) return Preview(result, intent, canonical, $"Will DELETE {ids.Count} object(s).");

            var tokenProblem = _confirm.Verify(confirmToken, "delete_objects", canonical);
            if (tokenProblem != null) return Refuse(result, intent, tokenProblem);

            var done = 0;
            foreach (var id in ids)
            {
                try
                {
                    if (_tekla.DeleteObject(id)) done++;
                    else result.Errors.Add($"{id}: Tekla did not delete it.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{id}: {ex.Message}");
                }
            }

            CommitIfNeeded(result);
            if (done > 0)
            {
                var after = _tekla.GetObjects(ids, includeUdas: false);
                var stillThere = after.Objects.Select(o => o.Guid).ToList();
                result.Validation.Add(stillThere.Count == 0
                    ? $"Verified: all {done} deleted object(s) are gone from the model."
                    : "NOT deleted (still in model): " + string.Join(", ", stillThere));
            }
            return Finish(result, intent, canonical, done, ids.Count, $"Deleted {done} of {ids.Count} object(s).");
        }

        // ------------------------------------------------------------------ helpers

        private WriteResult NewResult(string operation) => new WriteResult
        {
            Operation = operation,
            OperationId = Guid.NewGuid().ToString("N").Substring(0, 12),
        };

        private WriteResult Preview(WriteResult result, string? intent, string canonical, string summary)
        {
            result.Status = WriteStatus.Preview;
            result.Summary = summary + " PREVIEW ONLY - nothing has been changed.";
            result.ConfirmToken = _confirm.CreateToken(result.Operation, canonical, TimeSpan.FromMinutes(_settings.ConfirmTokenMinutes), out var expires);
            result.ConfirmTokenExpiresUtc = expires;
            result.NextStep = $"Show this plan to the user ({result.Risk} risk). Only if the user agrees, call {ToolName(result.Operation)} again with the SAME arguments plus apply=true and confirmToken. The token expires in {_settings.ConfirmTokenMinutes} minutes.";
            Log(result, intent, canonical);
            return result;
        }

        private WriteResult Refuse(WriteResult result, string? intent, string reason)
        {
            result.Status = WriteStatus.Refused;
            result.Summary = reason;
            result.ConfirmToken = "";
            result.ConfirmTokenExpiresUtc = null;
            result.NextStep = "Tell the user why this was refused. Do not try to work around it.";
            Log(result, intent, "");
            return result;
        }

        private WriteResult Finish(WriteResult result, string? intent, string canonical, int done, int total, string summary)
        {
            result.Status = done == 0 ? WriteStatus.Failed : (done < total || result.Errors.Count > 0 ? WriteStatus.Partial : WriteStatus.Applied);
            result.Summary = summary;
            result.ConfirmToken = "";
            result.ConfirmTokenExpiresUtc = null;
            result.NextStep = result.Status == WriteStatus.Applied
                ? "Report the result to the user. Tekla Undo (Ctrl+Z) can reverse it."
                : "Report exactly what failed. Read the objects back before trying again - do not retry blindly.";
            Log(result, intent, canonical);
            return result;
        }

        private void CommitIfNeeded(WriteResult result)
        {
            try
            {
                _tekla.CommitChanges();
            }
            catch (Exception ex)
            {
                result.Errors.Add("CommitChanges failed: " + ex.Message);
            }
        }

        private void VerifyCreated(WriteResult result, List<PartSpec> specs)
        {
            var back = _tekla.GetObjects(result.Created.Select(c => c.Guid).ToList(), includeUdas: false);
            var problems = 0;
            foreach (var o in back.Objects)
            {
                var spec = specs.FirstOrDefault(s => SameText(s.Profile, o.Profile) && o.Start != null && o.End != null &&
                                                     GeometryMath.SameLine(Point3.TryParse(s.Start)!, Point3.TryParse(s.End)!, o.Start, o.End, 1.0));
                if (spec == null) { problems++; result.Validation.Add($"Check {o.Guid}: read back as {o.Profile} {o.Start}->{o.End}, which does not match the request."); continue; }
                if (!SameText(spec.Material, o.Material)) { problems++; result.Validation.Add($"Check {o.Guid}: material read back as '{o.Material}', requested '{spec.Material}'."); }
            }
            if (back.NotFound.Count > 0) { problems += back.NotFound.Count; result.Validation.Add("Created but not found on read-back: " + string.Join(", ", back.NotFound)); }
            if (problems == 0) result.Validation.Insert(0, $"Verified: {back.Objects.Count} created object(s) read back with the requested profile, material and end points.");
        }

        private void VerifyModified(WriteResult result, List<string> ids, ModifySpec spec)
        {
            var back = _tekla.GetObjects(ids, includeUdas: spec.Udas.Count > 0);
            var problems = 0;
            foreach (var o in back.Objects)
            {
                problems += Expect(result, o.Guid, "Profile", spec.Profile, o.Profile);
                problems += Expect(result, o.Guid, "Material", spec.Material, o.Material);
                problems += Expect(result, o.Guid, "Class", spec.Class, o.Class);
                problems += Expect(result, o.Guid, "Name", spec.Name, o.Name);
                problems += Expect(result, o.Guid, "Finish", spec.Finish, o.Finish);
                foreach (var uda in spec.Udas)
                    problems += Expect(result, o.Guid, "UDA:" + uda.Key, uda.Value, o.Udas != null && o.Udas.TryGetValue(uda.Key, out var v) ? v : "");
            }
            if (problems == 0) result.Validation.Insert(0, $"Verified: {back.Objects.Count} object(s) read back with the new values.");
        }

        private static int Expect(WriteResult result, string guid, string field, string wanted, string actual)
        {
            if (string.IsNullOrWhiteSpace(wanted) || SameText(wanted, actual)) return 0;
            result.Validation.Add($"Check {guid}: {field} reads back as '{actual}', expected '{wanted}'.");
            return 1;
        }

        private void CheckCatalogs(WriteResult result, IEnumerable<string> profiles, IEnumerable<string> materials)
        {
            var ps = profiles.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ms = materials.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var count = Math.Max(ps.Count, ms.Count);
            for (var i = 0; i < count; i++)
            {
                var p = i < ps.Count ? ps[i] : "";
                var m = i < ms.Count ? ms[i] : "";
                CatalogCheck check;
                try { check = _tekla.CheckCatalog(p, m); }
                catch (Exception ex) { result.Warnings.Add("Catalog check failed: " + ex.Message); continue; }

                if (p.Length > 0 && check.ProfileExists == false) result.Errors.Add($"Profile '{p}' is not in the Tekla profile catalog.");
                if (m.Length > 0 && check.MaterialExists == false) result.Errors.Add($"Material '{m}' is not in the Tekla material catalog.");
                if (p.Length > 0 && check.ProfileExists == null) result.Warnings.Add($"Profile '{p}' could not be checked against the catalog. {check.Note}".Trim());
                if (m.Length > 0 && check.MaterialExists == null) result.Warnings.Add($"Material '{m}' could not be checked against the catalog. {check.Note}".Trim());
            }
        }

        private static void AddPositionErrors(WriteResult result, string label, PartSpec s)
        {
            Check(result, label, "PositionPlane", s.PositionPlane, "LEFT", "MIDDLE", "RIGHT");
            Check(result, label, "PositionDepth", s.PositionDepth, "FRONT", "MIDDLE", "BEHIND");
            Check(result, label, "PositionRotation", s.PositionRotation, "FRONT", "TOP", "BACK", "BELOW");
        }

        private static void Check(WriteResult result, string label, string field, string value, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!allowed.Contains(value.Trim().ToUpperInvariant()))
                result.Errors.Add($"{label}: {field} '{value}' is not valid. Use {string.Join(", ", allowed)}.");
        }

        private static void AddPlannedChange(WriteResult result, string guid, string field, string before, string after)
        {
            if (string.IsNullOrWhiteSpace(after) || SameText(before, after)) return;
            result.Changes.Add(new FieldChange { Guid = guid, Field = field, Before = before ?? "", After = after });
        }

        private void Log(WriteResult result, string? intent, string canonical)
        {
            string modelPath = "";
            try { modelPath = _tekla.GetConnectionInfo().ModelPath; } catch { /* logging must not fail the call */ }
            _log.Append(new OperationRecord
            {
                OperationId = result.OperationId,
                Tool = ToolName(result.Operation),
                Intent = intent ?? "",
                Risk = result.Risk.ToString(),
                Status = result.Status,
                ModelPath = modelPath,
                Arguments = canonical,
                ObjectsAffected = result.AffectedGuids.ToList(),
                Changes = result.Changes.ToList(),
                Errors = result.Errors.ToList(),
                Validation = result.Validation.ToList(),
            });
        }

        private static List<PartSpec> Normalize(IReadOnlyList<PartSpec>? specs) =>
            (specs ?? Array.Empty<PartSpec>()).Where(s => s != null).Select(s => new PartSpec
            {
                Start = (s.Start ?? "").Trim(),
                End = (s.End ?? "").Trim(),
                Profile = (s.Profile ?? "").Trim(),
                Material = (s.Material ?? "").Trim(),
                Class = (s.Class ?? "").Trim(),
                Name = (s.Name ?? "").Trim(),
                PositionPlane = (s.PositionPlane ?? "").Trim().ToUpperInvariant(),
                PositionDepth = (s.PositionDepth ?? "").Trim().ToUpperInvariant(),
                PositionRotation = (s.PositionRotation ?? "").Trim().ToUpperInvariant(),
            }).ToList();

        private static ModifySpec Normalize(ModifySpec s) => new ModifySpec
        {
            Profile = (s.Profile ?? "").Trim(),
            Material = (s.Material ?? "").Trim(),
            Class = (s.Class ?? "").Trim(),
            Name = (s.Name ?? "").Trim(),
            Finish = (s.Finish ?? "").Trim(),
            Udas = (s.Udas ?? new Dictionary<string, string>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? "", StringComparer.Ordinal),
        };

        private static object CanonicalSpec(ModifySpec s) => new
        {
            s.Profile,
            s.Material,
            s.Class,
            s.Name,
            s.Finish,
            Udas = new SortedDictionary<string, string>(s.Udas, StringComparer.Ordinal),
        };

        private static List<string> NormalizeGuids(IReadOnlyList<string>? guids) =>
            (guids ?? Array.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string DescribeSpec(ModifySpec s)
        {
            var parts = new List<string>();
            if (s.Profile.Length > 0) parts.Add("profile=" + s.Profile);
            if (s.Material.Length > 0) parts.Add("material=" + s.Material);
            if (s.Class.Length > 0) parts.Add("class=" + s.Class);
            if (s.Name.Length > 0) parts.Add("name=" + s.Name);
            if (s.Finish.Length > 0) parts.Add("finish=" + s.Finish);
            parts.AddRange(s.Udas.Select(kv => $"UDA {kv.Key}={kv.Value}"));
            return string.Join(", ", parts);
        }

        private static string Canonical(object value) => JsonSerializer.Serialize(value, CanonicalJson);

        private static bool SameText(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public static string ToolName(string operation) => "tekla_" + operation;
    }
}
