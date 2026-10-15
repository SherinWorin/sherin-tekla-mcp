using System;
using System.Collections.Generic;
using System.Linq;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Operations;
using SherinTeklaMcp.Core.Quantities;
using SherinTeklaMcp.Core.Rules;
using SherinTeklaMcp.Core.Validation;
using Xunit;

namespace SherinTeklaMcp.Core.Tests
{
    public class GeometryTests
    {
        [Theory]
        [InlineData("0,0,0", 0, 0, 0)]
        [InlineData(" 1000.5 , -20 ,3e3 ", 1000.5, -20, 3000)]
        public void Point_parses_invariant_text(string text, double x, double y, double z)
        {
            var p = Point3.TryParse(text);
            Assert.NotNull(p);
            Assert.Equal(x, p!.X);
            Assert.Equal(y, p.Y);
            Assert.Equal(z, p.Z);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1,2")]
        [InlineData("1,2,3,4")]
        [InlineData("a,b,c")]
        [InlineData("1;2;3")]
        [InlineData("NaN,0,0")]
        public void Point_rejects_bad_text(string text) => Assert.Null(Point3.TryParse(text));

        [Fact]
        public void Distance_and_angles()
        {
            var a = new Point3(0, 0, 0);
            var b = new Point3(3000, 4000, 0);
            Assert.Equal(5000, GeometryMath.Length(a, b), 6);
            Assert.Equal(0, GeometryMath.AngleToHorizontalDeg(a, b), 6);
            Assert.True(GeometryMath.IsVertical(a, new Point3(0, 0, 4000)));
            Assert.False(GeometryMath.IsVertical(a, b));
            Assert.True(GeometryMath.SameLine(a, b, b, a));
        }
    }

    public class FilterAndInputTests
    {
        private static TeklaObjectInfo Part(string profile, string material = "S355JR", string cls = "3", string name = "BEAM", bool vertical = false) =>
            new TeklaObjectInfo { Guid = Guid.NewGuid().ToString(), Type = "Beam", IsPart = true, Profile = profile, Material = material, Class = cls, Name = name, IsVertical = vertical };

        [Fact]
        public void Filter_matches_case_insensitive_and_combines_fields()
        {
            var f = new ObjectFilter { ProfileContains = "rhs", Material = "s355jr", Orientation = "nonvertical" };
            Assert.True(f.Matches(Part("RHS100*50*5")));
            Assert.False(f.Matches(Part("RHS100*50*5", "S275JR")));
            Assert.False(f.Matches(Part("RHS100*50*5", vertical: true)));
            Assert.False(f.Matches(Part("IPE200")));
        }

        [Fact]
        public void Parse_guids_splits_and_deduplicates()
        {
            var ids = ToolInputs.ParseGuids("A, b ;\n\"C\" a [D]");
            Assert.Equal(new[] { "A", "b", "C", "D" }, ids);
        }

        [Fact]
        public void Parse_udas()
        {
            var udas = ToolInputs.ParseUdas("COMMENT=check this; USER_FIELD_1 = x=y");
            Assert.Equal("check this", udas["COMMENT"]);
            Assert.Equal("x=y", udas["USER_FIELD_1"]);
            Assert.Throws<ArgumentException>(() => ToolInputs.ParseUdas("NOEQUALS"));
        }

        [Fact]
        public void Build_filter_rejects_unknown_orientation() =>
            Assert.Throws<ArgumentException>(() => ToolInputs.BuildFilter("", "", "", "", "", "", "sideways"));
    }

    public class QuantityReportTests
    {
        private static TeklaObjectInfo P(string profile, string material, double? length, double? weight) =>
            new TeklaObjectInfo { IsPart = true, Profile = profile, Material = material, ReportLengthMm = length, WeightKg = weight };

        [Fact]
        public void Groups_by_profile_and_material_with_totals()
        {
            var parts = new[]
            {
                P("IPE200", "S355JR", 6000, 134.4),
                P("IPE200", "S355JR", 4000, 89.6),
                P("IPE200", "S275JR", 1000, 22.4),
                P("HEA200", "S355JR", 4000, 169.2),
            };
            var r = QuantityReport.Build(parts, "profile,material");
            Assert.Equal(3, r.Rows.Count);
            var ipe = r.Rows.Single(x => x.Profile == "IPE200" && x.Material == "S355JR");
            Assert.Equal(2, ipe.Quantity);
            Assert.Equal(10.0, ipe.RunningLengthM, 3);
            Assert.Equal(224.0, ipe.WeightKg, 2);
            Assert.Equal(4, r.TotalQuantity);
            Assert.Equal(15.0, r.TotalRunningLengthM, 3);
            Assert.Equal(415.6, r.TotalWeightKg, 2);
            Assert.Empty(r.Warnings);
        }

        [Fact]
        public void Missing_weight_is_reported_not_hidden()
        {
            var r = QuantityReport.Build(new[] { P("IPE200", "S355JR", 1000, null) }, "profile");
            Assert.Equal(1, r.Rows[0].MissingWeight);
            Assert.Contains(r.Warnings, w => w.Contains("WEIGHT"));
        }

        [Fact]
        public void Unknown_group_field_throws() =>
            Assert.Throws<ArgumentException>(() => QuantityReport.Build(Array.Empty<TeklaObjectInfo>(), "profile,colour"));

        [Fact]
        public void Csv_has_header_rows_total_and_quotes()
        {
            var r = QuantityReport.Build(new[] { P("PL10*200", "S355JR", 500, 7.85), P("A,B", "S355JR", 1000, 1) }, "profile");
            var csv = QuantityReport.ToCsv(r).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Profile,Quantity,Running Length (m),Weight (kg)", csv[0]);
            Assert.Contains("\"A,B\",1,1.000,1.00", csv);
            Assert.StartsWith("TOTAL,2,1.500,", csv.Last());
        }
    }

    public class LinearDivisionTests
    {
        private static LinearDivisionRule Rule(double along = 400, double min = 3500, double max = 3600) => new LinearDivisionRule
        {
            Name = "test",
            StandardPanelLengthMm = 3190,
            GapMm = 10,
            MinPanelLengthMm = 300,
            ColumnWidthAlongWallMm = along,
            ColumnSpacingMinMm = min,
            ColumnSpacingMaxMm = max,
        };

        [Fact]
        public void Exact_fit_has_no_cut_panel()
        {
            // 2 panels: 2*3190 + 3*10 = 6410
            var d = LinearDivision.DivideOpening(6410, Rule());
            Assert.Equal(2, d.StandardPanels);
            Assert.Equal(0, d.CutPanels);
            Assert.Equal(0, d.UnfilledMm);
            Assert.Equal(3, d.Gaps);
            Assert.Equal(10, d.Panels[0].StartOffsetMm);
            Assert.Equal(3210, d.Panels[1].StartOffsetMm);
            Assert.Equal(6400, d.Panels[1].EndOffsetMm);
        }

        [Fact]
        public void Remainder_becomes_one_cut_panel()
        {
            // 6410 + 1000 -> cut panel 1000 - 10 = 990
            var d = LinearDivision.DivideOpening(7410, Rule());
            Assert.Equal(2, d.StandardPanels);
            Assert.Equal(1, d.CutPanels);
            Assert.Equal(990, d.CutPanelLengthMm);
            Assert.Equal(4, d.Gaps);
            Assert.Equal(7400, d.Panels.Last().EndOffsetMm);
        }

        [Fact]
        public void Short_remainder_is_reported_as_unfilled()
        {
            var d = LinearDivision.DivideOpening(6410 + 200, Rule());
            Assert.Equal(0, d.CutPanels);
            Assert.Equal(200, d.UnfilledMm);
            Assert.NotEmpty(d.Warnings);
        }

        [Fact]
        public void Opening_too_short_for_any_panel()
        {
            var d = LinearDivision.DivideOpening(200, Rule());
            Assert.Equal(0, d.StandardPanels);
            Assert.Equal(0, d.CutPanels);
            Assert.Contains(d.Warnings, w => w.Contains("too short"));
        }

        [Fact]
        public void Wall_plan_picks_fewest_bays_within_max_spacing()
        {
            var plan = LinearDivision.PlanWall(35500, Rule(along: 200, min: 3500, max: 3600));
            Assert.Equal(10, plan.Bays);
            Assert.Equal(11, plan.Columns);
            Assert.Equal(3550, plan.ColumnSpacingMm);
            Assert.True(plan.SpacingWithinRule);
            Assert.Equal(35500, plan.ColumnCentresMm.Last());
            // clear 3350: one 3190 panel (3210 with gaps) + 140 left -> too short for a cut panel
            Assert.All(plan.BayDetails, b => Assert.Equal(1, b.Division.StandardPanels));
        }

        [Fact]
        public void Wall_plan_flags_spacing_outside_rule()
        {
            var plan = LinearDivision.PlanWall(5000, Rule());
            Assert.Equal(2, plan.Bays);
            Assert.False(plan.SpacingWithinRule);
            Assert.NotEmpty(plan.Warnings);
        }

        [Fact]
        public void Brief_values_with_400_column_leave_no_room_for_a_standard_panel()
        {
            // 3600 c/c - 400 column = 3200 clear, but 3190 + 2 x 10 = 3210 is needed.
            var d = LinearDivision.DivideOpening(3200, Rule());
            Assert.Equal(0, d.StandardPanels);
            Assert.Equal(1, d.CutPanels);
            Assert.Equal(3180, d.CutPanelLengthMm);
        }

        [Fact]
        public void Spacing_check_lists_each_problem()
        {
            var problems = LinearDivision.CheckSpacings(new[] { 3550.0, 3400, 3700 }, Rule());
            Assert.Equal(2, problems.Count);
            Assert.Contains("Bay 2", problems[0]);
            Assert.Contains("Bay 3", problems[1]);
        }

        [Fact]
        public void Invalid_rule_is_rejected()
        {
            var bad = Rule();
            bad.MinPanelLengthMm = 5000;
            Assert.Throws<ArgumentException>(() => LinearDivision.DivideOpening(1000, bad));
        }
    }

    public class ValidatorTests
    {
        [Fact]
        public void Finds_missing_data_short_members_and_duplicates()
        {
            var a = new TeklaObjectInfo { Guid = "A", Type = "Beam", IsPart = true, Profile = "IPE200", Material = "S355JR", Class = "3", Name = "B", Start = new Point3(0, 0, 0), End = new Point3(6000, 0, 0), AxisLengthMm = 6000 };
            var dup = new TeklaObjectInfo { Guid = "B", Type = "Beam", IsPart = true, Profile = "IPE200", Material = "S355JR", Class = "3", Name = "B", Start = new Point3(6000, 0, 0), End = new Point3(0, 0, 0), AxisLengthMm = 6000 };
            var bad = new TeklaObjectInfo { Guid = "C", Type = "Beam", IsPart = true, Profile = "", Material = "", Start = new Point3(0, 0, 0), End = new Point3(5, 0, 0), AxisLengthMm = 5 };

            var r = ModelValidator.Validate(new List<TeklaObjectInfo> { a, dup, bad }, 10);

            Assert.Contains(r.Issues, i => i.Check == "duplicate" && i.Guid == "B");
            Assert.Contains(r.Issues, i => i.Check == "missing-profile" && i.Guid == "C");
            Assert.Contains(r.Issues, i => i.Check == "missing-material" && i.Guid == "C");
            Assert.Contains(r.Issues, i => i.Check == "short-member" && i.Guid == "C");
            Assert.DoesNotContain(r.Issues, i => i.Guid == "A");
            Assert.Equal(new[] { "B", "C" }, r.ProblemGuids.OrderBy(x => x));
            Assert.Equal("issues-found", r.Status);
        }

        [Fact]
        public void Panel_rule_check()
        {
            var rule = new LinearDivisionRule { Name = "r", StandardPanelLengthMm = 3190, MinPanelLengthMm = 300, GapMm = 10 };
            var longPanel = new TeklaObjectInfo { Guid = "L", IsPart = true, Profile = "X", Material = "Y", Class = "1", Name = "P", ReportLengthMm = 3300 };
            var ok = new TeklaObjectInfo { Guid = "O", IsPart = true, Profile = "X", Material = "Y", Class = "1", Name = "P", ReportLengthMm = 3190 };
            var r = ModelValidator.Validate(new List<TeklaObjectInfo> { longPanel, ok }, 10, rule);
            Assert.Single(r.Issues);
            Assert.Equal("panel-too-long", r.Issues[0].Check);
        }
    }
}
