using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SherinTeklaMcp.Core.Logging;
using SherinTeklaMcp.Core.Mock;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Operations;
using SherinTeklaMcp.Core.Safety;
using SherinTeklaMcp.Core.Settings;
using Xunit;

namespace SherinTeklaMcp.Core.Tests
{
    public class ConfirmationTests
    {
        private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        [Fact]
        public void Valid_token_for_same_arguments()
        {
            var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            var svc = new ConfirmationService(Key, () => now);
            var token = svc.CreateToken("op", "{\"a\":1}", TimeSpan.FromMinutes(10), out var expires);
            Assert.Equal(now.AddMinutes(10), expires);
            Assert.Null(svc.Verify(token, "op", "{\"a\":1}"));
        }

        [Fact]
        public void Rejects_changed_arguments_other_operation_and_missing_token()
        {
            var svc = new ConfirmationService(Key, null);
            var token = svc.CreateToken("op", "{\"a\":1}", TimeSpan.FromMinutes(10), out _);
            Assert.NotNull(svc.Verify(token, "op", "{\"a\":2}"));
            Assert.NotNull(svc.Verify(token, "other", "{\"a\":1}"));
            Assert.NotNull(svc.Verify("", "op", "{\"a\":1}"));
            Assert.NotNull(svc.Verify("garbage", "op", "{\"a\":1}"));
            Assert.NotNull(svc.Verify(token.Replace('.', 'x'), "op", "{\"a\":1}"));
        }

        [Fact]
        public void Rejects_expired_token()
        {
            var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            var svc = new ConfirmationService(Key, () => now);
            var token = svc.CreateToken("op", "x", TimeSpan.FromMinutes(1), out _);
            now = now.AddMinutes(2);
            Assert.Contains("expired", svc.Verify(token, "op", "x"));
        }

        [Fact]
        public void Token_from_another_server_run_is_invalid()
        {
            var first = new ConfirmationService();
            var second = new ConfirmationService();
            var token = first.CreateToken("op", "x", TimeSpan.FromMinutes(5), out _);
            Assert.NotNull(second.Verify(token, "op", "x"));
        }
    }

    public class SafetyPolicyTests
    {
        [Fact]
        public void Read_only_refuses_everything()
        {
            var s = new ServerSettings { ReadOnly = true };
            Assert.NotNull(SafetyPolicy.CheckAllowed(WriteKind.Create, 1, s));
            Assert.NotNull(SafetyPolicy.CheckAllowed(WriteKind.Modify, 1, s));
        }

        [Fact]
        public void Delete_needs_allow_delete_and_is_always_high()
        {
            var s = new ServerSettings { ReadOnly = false, AllowDelete = false };
            Assert.Contains("Delete is disabled", SafetyPolicy.CheckAllowed(WriteKind.Delete, 1, s));
            s.AllowDelete = true;
            Assert.Null(SafetyPolicy.CheckAllowed(WriteKind.Delete, 1, s));
            Assert.Equal(RiskLevel.High, SafetyPolicy.Classify(WriteKind.Delete, 1, s));
        }

        [Fact]
        public void Risk_rises_above_threshold_and_limit_is_enforced()
        {
            var s = new ServerSettings { ReadOnly = false, HighRiskObjectCount = 20, MaxObjectsPerWrite = 50 };
            Assert.Equal(RiskLevel.Medium, SafetyPolicy.Classify(WriteKind.Modify, 20, s));
            Assert.Equal(RiskLevel.High, SafetyPolicy.Classify(WriteKind.Modify, 21, s));
            Assert.NotNull(SafetyPolicy.CheckAllowed(WriteKind.Create, 51, s));
            Assert.NotNull(SafetyPolicy.CheckAllowed(WriteKind.Create, 0, s));
        }

        [Fact]
        public void Settings_load_defaults_and_clamp_bad_values()
        {
            Assert.True(ServerSettings.Load(null).ReadOnly);
            var file = Path.GetTempFileName();
            File.WriteAllText(file, "{ // comment\n \"ReadOnly\": false, \"HighRiskObjectCount\": -5, \"ConfirmTokenMinutes\": 999, }");
            var s = ServerSettings.Load(file);
            Assert.False(s.ReadOnly);
            Assert.Equal(1, s.HighRiskObjectCount);
            Assert.Equal(60, s.ConfirmTokenMinutes);
            Assert.False(s.AllowDelete);
            File.Delete(file);
        }
    }

    public class WritePipelineTests : IDisposable
    {
        private readonly string _logDir = Path.Combine(Path.GetTempPath(), "stm-tests-" + Guid.NewGuid().ToString("N"));
        private readonly MockTeklaService _tekla = new MockTeklaService();
        private readonly ServerSettings _settings = new ServerSettings { ReadOnly = false, AllowDelete = false, HighRiskObjectCount = 20 };
        private readonly WriteOperations _writes;

        public WritePipelineTests()
        {
            _writes = new WriteOperations(_tekla, _settings, new ConfirmationService(), new OperationLog(_logDir));
        }

        public void Dispose()
        {
            if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true);
        }

        private static List<PartSpec> OneBeam(string profile = "IPE200", string material = "S355JR") => new List<PartSpec>
        {
            new PartSpec { Start = "0,0,0", End = "6000,0,0", Profile = profile, Material = material, Class = "3", Name = "BEAM" },
        };

        private int Count() => _tekla.FindObjects(new ObjectFilter(), false, 0, int.MaxValue).Objects.Count;

        [Fact]
        public void Preview_changes_nothing_and_returns_token()
        {
            var before = Count();
            var r = _writes.CreateBeams(OneBeam(), apply: false, confirmToken: null, intent: "test");
            Assert.Equal(WriteStatus.Preview, r.Status);
            Assert.False(string.IsNullOrEmpty(r.ConfirmToken));
            Assert.Equal(RiskLevel.Medium, r.Risk);
            Assert.Single(r.Plan);
            Assert.Equal(before, Count());
            Assert.Equal(0, _tekla.Commits);
        }

        [Fact]
        public void Apply_without_token_is_refused()
        {
            var r = _writes.CreateBeams(OneBeam(), apply: true, confirmToken: null, intent: "test");
            Assert.Equal(WriteStatus.Refused, r.Status);
            Assert.Equal(0, _tekla.Commits);
        }

        [Fact]
        public void Apply_with_token_creates_commits_verifies_and_logs()
        {
            var before = Count();
            var preview = _writes.CreateBeams(OneBeam(), false, null, "create a beam");
            var r = _writes.CreateBeams(OneBeam(), true, preview.ConfirmToken, "create a beam");

            Assert.Equal(WriteStatus.Applied, r.Status);
            Assert.Single(r.Created);
            Assert.Equal(before + 1, Count());
            Assert.Equal(1, _tekla.Commits);
            Assert.StartsWith("Verified", r.Validation[0]);

            var log = Directory.GetFiles(_logDir, "operations-*.jsonl").Single();
            var lines = File.ReadAllLines(log);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"Status\":\"preview\"", lines[0]);
            Assert.Contains("\"Status\":\"applied\"", lines[1]);
            Assert.Contains("create a beam", lines[1]);
        }

        [Fact]
        public void Token_does_not_cover_changed_arguments()
        {
            var preview = _writes.CreateBeams(OneBeam("IPE200"), false, null, "");
            var r = _writes.CreateBeams(OneBeam("HEB200"), true, preview.ConfirmToken, "");
            Assert.Equal(WriteStatus.Refused, r.Status);
            Assert.Equal(0, _tekla.Commits);
        }

        [Fact]
        public void Read_only_mode_refuses_even_with_valid_input()
        {
            _settings.ReadOnly = true;
            var r = _writes.CreateBeams(OneBeam(), false, null, "");
            Assert.Equal(WriteStatus.Refused, r.Status);
            Assert.Contains("READ-ONLY", r.Summary);
        }

        [Fact]
        public void Unknown_profile_or_material_is_refused()
        {
            var r = _writes.CreateBeams(OneBeam("IPE2000", "S999"), false, null, "");
            Assert.Equal(WriteStatus.Refused, r.Status);
            Assert.Contains(r.Errors, e => e.Contains("IPE2000"));
            Assert.Contains(r.Errors, e => e.Contains("S999"));
        }

        [Fact]
        public void Invalid_geometry_is_refused()
        {
            var specs = new List<PartSpec> { new PartSpec { Start = "0,0,0", End = "0,0,5", Profile = "IPE200", Material = "S355JR" } };
            var r = _writes.CreateBeams(specs, false, null, "");
            Assert.Equal(WriteStatus.Refused, r.Status);
            Assert.Contains(r.Errors, e => e.Contains("below the minimum"));
        }

        [Fact]
        public void Columns_are_vertical_from_base_and_height()
        {
            var cols = new List<ColumnSpec> { new ColumnSpec { Base = "1000,2000,0", HeightMm = 3500, Profile = "HEA200", Material = "S355JR", Class = "2" } };
            var preview = _writes.CreateColumns(cols, false, null, "");
            var r = _writes.CreateColumns(cols, true, preview.ConfirmToken, "");
            Assert.Equal(WriteStatus.Applied, r.Status);
            var c = r.Created.Single();
            Assert.True(c.IsVertical);
            Assert.Equal(3500, c.AxisLengthMm);
        }

        [Fact]
        public void Modify_shows_before_and_after_and_verifies()
        {
            var guids = _tekla.FindObjects(new ObjectFilter { ProfileEquals = "IPE200" }, false, 0, 1000).Objects.Select(o => o.Guid).ToList();
            var spec = new ModifySpec { Material = "S355JR", Udas = new Dictionary<string, string> { ["COMMENT"] = "AI" } };

            var preview = _writes.ModifyObjects(guids, spec, false, null, "change to S355");
            Assert.Equal(WriteStatus.Preview, preview.Status);
            Assert.Contains(preview.Changes, c => c.Field == "Material" && c.Before == "S275JR" && c.After == "S355JR");

            var r = _writes.ModifyObjects(guids, spec, true, preview.ConfirmToken, "change to S355");
            Assert.Equal(WriteStatus.Applied, r.Status);
            Assert.StartsWith("Verified", r.Validation[0]);
            Assert.All(_tekla.GetObjects(guids, true).Objects, o =>
            {
                Assert.Equal("S355JR", o.Material);
                Assert.Equal("AI", o.Udas!["COMMENT"]);
            });
        }

        [Fact]
        public void Modify_unknown_guid_is_refused_and_nothing_changes()
        {
            var r = _writes.ModifyObjects(new[] { "NOT-A-GUID" }, new ModifySpec { Name = "X" }, false, null, "");
            Assert.Equal(WriteStatus.Refused, r.Status);
        }

        [Fact]
        public void Modify_many_objects_is_high_risk()
        {
            _settings.HighRiskObjectCount = 5;
            var guids = _tekla.FindObjects(new ObjectFilter { Types = { "Beam" } }, false, 0, 1000).Objects.Select(o => o.Guid).ToList();
            var r = _writes.ModifyObjects(guids, new ModifySpec { Name = "RENAMED" }, false, null, "");
            Assert.Equal(RiskLevel.High, r.Risk);
        }

        [Fact]
        public void Delete_is_disabled_by_default_then_works_with_confirmation()
        {
            var guid = _tekla.FindObjects(new ObjectFilter { ProfileEquals = "IPE300" }, false, 1, 1000).Objects[0].Guid;

            var refused = _writes.DeleteObjects(new[] { guid }, false, null, "");
            Assert.Equal(WriteStatus.Refused, refused.Status);

            _settings.AllowDelete = true;
            var preview = _writes.DeleteObjects(new[] { guid }, false, null, "delete one beam");
            Assert.Equal(RiskLevel.High, preview.Risk);
            var r = _writes.DeleteObjects(new[] { guid }, true, preview.ConfirmToken, "delete one beam");
            Assert.Equal(WriteStatus.Applied, r.Status);
            Assert.Contains(guid, _tekla.GetObjects(new[] { guid }, false).NotFound);
        }
    }
}
