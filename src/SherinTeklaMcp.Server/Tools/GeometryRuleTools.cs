using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Rules;

namespace SherinTeklaMcp.Server.Tools
{
    public sealed class DistanceResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public double DistanceMm { get; set; }
        public double DxMm { get; set; }
        public double DyMm { get; set; }
        public double DzMm { get; set; }
        public double HorizontalDistanceMm { get; set; }
        public double AngleToHorizontalDeg { get; set; }
    }

    public sealed class RuleListResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public string RulesFolder { get; set; } = "";
        public List<LinearDivisionRule> Rules { get; set; } = new List<LinearDivisionRule>();
    }

    public sealed class RuleCalculationResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public WallPlan? WallPlan { get; set; }
        public PanelDivision? Division { get; set; }
    }

    /// <summary>LOW risk: engineering calculations done by code, not by the AI. Nothing touches the model.</summary>
    [McpServerToolType]
    public static class GeometryRuleTools
    {
        [McpServerTool(Name = "tekla_calculate_distance")]
        [Description("[LOW risk, calculation] Distance between two points (global mm, 'x,y,z'): 3D distance, dx/dy/dz, horizontal distance and angle to horizontal.")]
        public static DistanceResult CalculateDistance(
            [Description("First point 'x,y,z' in mm.")] string pointA,
            [Description("Second point 'x,y,z' in mm.")] string pointB)
        {
            var a = Point3.TryParse(pointA);
            var b = Point3.TryParse(pointB);
            if (a == null || b == null) return new DistanceResult { Status = "error", Message = "Both points must be 'x,y,z' in mm." };
            return new DistanceResult
            {
                DistanceMm = Math.Round(a.DistanceTo(b), 3),
                DxMm = Math.Round(b.X - a.X, 3),
                DyMm = Math.Round(b.Y - a.Y, 3),
                DzMm = Math.Round(b.Z - a.Z, 3),
                HorizontalDistanceMm = Math.Round(Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)), 3),
                AngleToHorizontalDeg = Math.Round(GeometryMath.AngleToHorizontalDeg(a, b), 3),
            };
        }

        [McpServerTool(Name = "tekla_list_rules")]
        [Description("[LOW risk, read-only] List the modeling rules from the rules folder (for example 'boundary-wall': panel length, gap, column size, column spacing). Use a rule's name in tekla_plan_wall, tekla_divide_opening or tekla_validate_objects.")]
        public static RuleListResult ListRules(RuleRepository rules)
        {
            try
            {
                var all = rules.LoadAll();
                return new RuleListResult
                {
                    Rules = all,
                    RulesFolder = rules.Directory,
                    Message = all.Count == 0 ? "No rule files found in " + rules.Directory : $"{all.Count} rule(s).",
                };
            }
            catch (Exception ex) { return new RuleListResult { Status = "error", Message = ToolHelpers.Describe(ex), RulesFolder = rules.Directory }; }
        }

        [McpServerTool(Name = "tekla_plan_wall")]
        [Description("[LOW risk, calculation] Plan a straight wall/fence with a rule: number of columns and bays, equal column spacing (checked against the rule range), and the standard/cut panels with gaps in every bay. " +
                     "Give wallLengthMm (between the centres of the two END columns), or startPoint and endPoint. Nothing is modelled - use the result to preview creation.")]
        public static RuleCalculationResult PlanWall(
            RuleRepository rules,
            [Description("Rule name from tekla_list_rules, e.g. 'boundary-wall'.")] string rule,
            [Description("Wall length between end column centres, mm. Leave 0 when giving points.")] double wallLengthMm = 0,
            [Description("Optional start column centre 'x,y,z' (mm).")] string startPoint = "",
            [Description("Optional end column centre 'x,y,z' (mm).")] string endPoint = "")
        {
            try
            {
                var length = wallLengthMm;
                if (length <= 0)
                {
                    var a = Point3.TryParse(startPoint);
                    var b = Point3.TryParse(endPoint);
                    if (a == null || b == null) throw new ArgumentException("Give wallLengthMm, or both startPoint and endPoint as 'x,y,z'.");
                    length = a.DistanceTo(b);
                }
                var plan = LinearDivision.PlanWall(length, rules.Get(rule));
                return new RuleCalculationResult
                {
                    WallPlan = plan,
                    Message = $"{plan.Columns} columns, {plan.Bays} bay(s) at {plan.ColumnSpacingMm} mm, {plan.TotalStandardPanels} standard + {plan.TotalCutPanels} cut panel(s)." +
                              (plan.Warnings.Count > 0 ? " See warnings." : ""),
                };
            }
            catch (Exception ex) { return new RuleCalculationResult { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }

        [McpServerTool(Name = "tekla_divide_opening")]
        [Description("[LOW risk, calculation] Divide one clear opening (face to face, mm) into standard panels and at most one cut panel, with the rule's gap at every panel end. Returns each panel's start/end offset.")]
        public static RuleCalculationResult DivideOpening(
            RuleRepository rules,
            [Description("Rule name from tekla_list_rules.")] string rule,
            [Description("Clear opening length, mm.")] double clearLengthMm)
        {
            try
            {
                var division = LinearDivision.DivideOpening(clearLengthMm, rules.Get(rule));
                return new RuleCalculationResult
                {
                    Division = division,
                    Message = $"{division.StandardPanels} standard + {division.CutPanels} cut panel(s)" +
                              (division.CutPanels > 0 ? $" (cut panel {division.CutPanelLengthMm} mm)" : "") +
                              (division.UnfilledMm > 0 ? $", {division.UnfilledMm} mm unfilled" : "") + ".",
                };
            }
            catch (Exception ex) { return new RuleCalculationResult { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }
    }
}
