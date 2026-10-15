using System.Collections.Generic;
using SherinTeklaMcp.Core.Geometry;

namespace SherinTeklaMcp.Core.Models
{
    /// <summary>
    /// What the server reports about one model object. Lengths in mm, weight in kg,
    /// points in GLOBAL coordinates. Fields that Tekla could not provide stay null/empty.
    /// </summary>
    public sealed class TeklaObjectInfo
    {
        public string Guid { get; set; } = "";
        public int Id { get; set; }

        /// <summary>Open API class name: Beam, PolyBeam, ContourPlate, BentPlate, ...</summary>
        public string Type { get; set; } = "";

        /// <summary>True for parts (beams, plates, ...). Bolts, welds, components etc. are false.</summary>
        public bool IsPart { get; set; }

        public string Name { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Material { get; set; } = "";
        public string Class { get; set; } = "";
        public string Finish { get; set; } = "";

        public string PartPrefix { get; set; } = "";
        public int? PartStartNumber { get; set; }
        public string AssemblyPrefix { get; set; } = "";
        public int? AssemblyStartNumber { get; set; }

        /// <summary>Report property PART_POS (empty until numbering has run).</summary>
        public string PartPosition { get; set; } = "";
        /// <summary>Report property ASSEMBLY_POS (empty until numbering has run).</summary>
        public string AssemblyPosition { get; set; } = "";

        public Point3? Start { get; set; }
        public Point3? End { get; set; }
        /// <summary>Contour points for plates (global).</summary>
        public List<Point3>? Contour { get; set; }

        /// <summary>Straight distance start to end (beams) - not reduced by cuts or fittings.</summary>
        public double? AxisLengthMm { get; set; }
        /// <summary>Report property LENGTH - Tekla's own length (includes fittings/cuts).</summary>
        public double? ReportLengthMm { get; set; }
        /// <summary>Report property WEIGHT.</summary>
        public double? WeightKg { get; set; }

        /// <summary>Position.Plane / Depth / Rotation as text, e.g. MIDDLE / MIDDLE / FRONT.</summary>
        public string PositionPlane { get; set; } = "";
        public string PositionDepth { get; set; } = "";
        public string PositionRotation { get; set; } = "";

        /// <summary>True when the member axis is vertical (a column in practice).</summary>
        public bool IsVertical { get; set; }

        /// <summary>User-defined attributes, only filled when asked for.</summary>
        public Dictionary<string, string>? Udas { get; set; }

        /// <summary>Best available length: Tekla's report LENGTH, else the axis length.</summary>
        public double? EffectiveLengthMm => ReportLengthMm ?? AxisLengthMm;
    }

    /// <summary>Result of a read query with paging/limit information.</summary>
    public sealed class ObjectQueryResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int Count { get; set; }
        /// <summary>True when more objects matched than were returned.</summary>
        public bool Truncated { get; set; }
        public int Scanned { get; set; }
        public List<string> NotFound { get; set; } = new List<string>();
        public List<TeklaObjectInfo> Objects { get; set; } = new List<TeklaObjectInfo>();
    }

    public sealed class ConnectionInfo
    {
        public bool Connected { get; set; }
        public string Backend { get; set; } = "";
        public string Message { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string ModelPath { get; set; } = "";
        public string TeklaBinDir { get; set; } = "";
        public string TeklaModelDllFileVersion { get; set; } = "";
        public int CompiledForTeklaMajor { get; set; }
        public string ServerVersion { get; set; } = "";
        public bool ReadOnlyMode { get; set; }
        public bool DeleteAllowed { get; set; }
        public string SettingsFile { get; set; } = "";
        public string OperationLogFolder { get; set; } = "";
    }

    /// <summary>Result of a profile/material catalog check. Null = could not be checked.</summary>
    public sealed class CatalogCheck
    {
        public bool? ProfileExists { get; set; }
        public bool? MaterialExists { get; set; }
        public string Note { get; set; } = "";
    }
}
