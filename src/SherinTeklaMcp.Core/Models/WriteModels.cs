using System;
using System.Collections.Generic;
using SherinTeklaMcp.Core.Safety;

namespace SherinTeklaMcp.Core.Models
{
    /// <summary>One beam (or column) to create. Points are "x,y,z" in mm, GLOBAL coordinates.</summary>
    public sealed class PartSpec
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Material { get; set; } = "";
        /// <summary>Tekla class (a number written as text, e.g. "3").</summary>
        public string Class { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Optional Position.Plane: LEFT, MIDDLE, RIGHT.</summary>
        public string PositionPlane { get; set; } = "";
        /// <summary>Optional Position.Depth: FRONT, MIDDLE, BEHIND.</summary>
        public string PositionDepth { get; set; } = "";
        /// <summary>Optional Position.Rotation: FRONT, TOP, BACK, BELOW.</summary>
        public string PositionRotation { get; set; } = "";
    }

    /// <summary>Property changes for existing parts. Empty fields are left unchanged.</summary>
    public sealed class ModifySpec
    {
        public string Profile { get; set; } = "";
        public string Material { get; set; } = "";
        public string Class { get; set; } = "";
        public string Name { get; set; } = "";
        public string Finish { get; set; } = "";
        /// <summary>String UDAs to set, name to value.</summary>
        public Dictionary<string, string> Udas { get; set; } = new Dictionary<string, string>();

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Profile) && string.IsNullOrWhiteSpace(Material) &&
            string.IsNullOrWhiteSpace(Class) && string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(Finish) && (Udas == null || Udas.Count == 0);
    }

    /// <summary>One changed field on one object (for the plan, the result and the log).</summary>
    public sealed class FieldChange
    {
        public string Guid { get; set; } = "";
        public string Field { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
    }

    public static class WriteStatus
    {
        /// <summary>Nothing written. The plan and a confirm token are returned.</summary>
        public const string Preview = "preview";
        /// <summary>Written and committed, read back and checked.</summary>
        public const string Applied = "applied";
        /// <summary>Written, but some items failed - see errors.</summary>
        public const string Partial = "partial";
        /// <summary>Not written: safety rule, read-only mode, bad token or invalid input.</summary>
        public const string Refused = "refused";
        /// <summary>Not written: Tekla error before anything was changed.</summary>
        public const string Failed = "failed";
    }

    /// <summary>The answer of every model-changing tool, preview or apply.</summary>
    public sealed class WriteResult
    {
        public string Status { get; set; } = WriteStatus.Preview;
        public string Operation { get; set; } = "";
        public string OperationId { get; set; } = "";
        public RiskLevel Risk { get; set; }
        public string Summary { get; set; } = "";

        /// <summary>Human-readable plan: what will be (or was) done.</summary>
        public List<string> Plan { get; set; } = new List<string>();
        public List<FieldChange> Changes { get; set; } = new List<FieldChange>();

        /// <summary>Returned by a preview. Pass it back with apply=true and the SAME arguments.</summary>
        public string ConfirmToken { get; set; } = "";
        public DateTime? ConfirmTokenExpiresUtc { get; set; }
        /// <summary>What the AI must do next, in plain words.</summary>
        public string NextStep { get; set; } = "";

        public List<string> AffectedGuids { get; set; } = new List<string>();
        public List<TeklaObjectInfo> Created { get; set; } = new List<TeklaObjectInfo>();
        public List<string> Validation { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
