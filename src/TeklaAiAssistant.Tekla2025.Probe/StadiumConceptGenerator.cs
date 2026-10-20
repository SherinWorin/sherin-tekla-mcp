namespace TeklaAiAssistant.Tekla2025.Probe;

using System.Globalization;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

internal sealed class StadiumConceptGenerator
{
    private readonly string _configPath;

    public StadiumConceptGenerator(string configPath) => _configPath = configPath;

    public int Run(bool confirmed, Action<string> log)
    {
        using var configStream = File.OpenRead(_configPath);
        var config = (StadiumConceptConfig?)new DataContractJsonSerializer(typeof(StadiumConceptConfig))
            .ReadObject(configStream)
            ?? throw new InvalidOperationException("Stadium concept configuration is empty.");
        var model = new Model();
        if (!model.GetConnectionStatus())
            throw new InvalidOperationException("Tekla is not connected to an open model.");

        var info = model.GetInfo();
        if (string.IsNullOrWhiteSpace(info.ModelPath))
            throw new InvalidOperationException("Tekla reported no model path; open C:\\TeklaModels\\LiveTest_Disposable before creation.");
        if (!string.Equals(Path.GetFullPath(info.ModelPath).TrimEnd('\\'),
                Path.GetFullPath(config.ModelPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable-model guard refused execution. Connected model: " + info.ModelPath);

        var plan = BuildPlan(config);
        log($"Stadium conceptual plan: {plan.Count} MCP_TEST_ beams; profile={config.Profile}; material={config.Material}; model={info.ModelPath}");
        if (!confirmed)
            throw new InvalidOperationException("Creation refused. Re-run with --confirm-create-stadium after reviewing the plan.");

        var inserted = new List<Beam>();
        foreach (var item in plan)
        {
            var beam = new Beam(item.Start, item.End)
            {
                Name = "MCP_TEST_STADIUM",
                Class = config.Class
            };
            beam.Profile.ProfileString = config.Profile;
            beam.Material.MaterialString = config.Material;
            if (!beam.Insert())
                throw new InvalidOperationException("Tekla rejected a conceptual stadium beam insertion.");
            inserted.Add(beam);
        }

        model.CommitChanges("MCP_TEST conceptual stadium roof");
        var verified = 0;
        foreach (var beam in inserted)
        {
            var selected = model.SelectModelObject(beam.Identifier) as Beam;
            if (selected is null || !selected.Identifier.IsValid())
                throw new InvalidOperationException("Inserted stadium beam failed read-back verification.");
            verified++;
        }

        log($"Created and verified {verified} MCP_TEST_ stadium beams; no existing objects were modified.");
        return verified;
    }

    private static List<BeamPlan> BuildPlan(StadiumConceptConfig config)
    {
        var plan = new List<BeamPlan>();
        var sectors = config.RadialSectors;
        var ringCount = config.RingCount;
        var outerA = config.OuterLengthMm / 2.0;
        var outerB = config.OuterWidthMm / 2.0;
        var innerA = config.OpeningLengthMm / 2.0;
        var innerB = config.OpeningWidthMm / 2.0;

        var rings = new List<(double A, double B, double Z)>();
        for (var ring = 0; ring < ringCount; ring++)
        {
            var t = ring / (double)(ringCount - 1);
            rings.Add((outerA + (innerA - outerA) * t,
                outerB + (innerB - outerB) * t,
                config.OuterRoofHeightMm + (config.InnerRoofHeightMm - config.OuterRoofHeightMm) * t));
        }

        for (var ring = 0; ring < rings.Count; ring++)
        {
            for (var sector = 0; sector < sectors; sector++)
            {
                var start = PointOnRing(rings[ring], sector, sectors);
                var end = PointOnRing(rings[ring], (sector + 1) % sectors, sectors);
                plan.Add(new BeamPlan(start, end));
            }
        }

        for (var sector = 0; sector < sectors; sector++)
        {
            for (var ring = 0; ring < rings.Count - 1; ring++)
            {
                plan.Add(new BeamPlan(
                    PointOnRing(rings[ring], sector, sectors),
                    PointOnRing(rings[ring + 1], sector, sectors)));
                plan.Add(new BeamPlan(
                    PointOnRing(rings[ring], sector, sectors),
                    PointOnRing(rings[ring + 1], (sector + 1) % sectors, sectors)));
            }
        }

        return plan;
    }

    private static Point PointOnRing((double A, double B, double Z) ring, int sector, int sectors)
    {
        var angle = 2.0 * Math.PI * sector / sectors;
        return new Point(ring.A * Math.Cos(angle), ring.B * Math.Sin(angle), ring.Z);
    }

    private sealed class BeamPlan
    {
        public BeamPlan(Point start, Point end) { Start = start; End = end; }
        public Point Start { get; }
        public Point End { get; }
    }

    [DataContract]
    private sealed class StadiumConceptConfig
    {
        [DataMember(Name = "modelPath")]
        public string ModelPath { get; set; } = string.Empty;
        [DataMember(Name = "outerLengthMm")]
        public double OuterLengthMm { get; set; }
        [DataMember(Name = "outerWidthMm")]
        public double OuterWidthMm { get; set; }
        [DataMember(Name = "openingLengthMm")]
        public double OpeningLengthMm { get; set; }
        [DataMember(Name = "openingWidthMm")]
        public double OpeningWidthMm { get; set; }
        [DataMember(Name = "outerRoofHeightMm")]
        public double OuterRoofHeightMm { get; set; }
        [DataMember(Name = "innerRoofHeightMm")]
        public double InnerRoofHeightMm { get; set; }
        [DataMember(Name = "radialSectors")]
        public int RadialSectors { get; set; }
        [DataMember(Name = "ringCount")]
        public int RingCount { get; set; }
        [DataMember(Name = "profile")]
        public string Profile { get; set; } = string.Empty;
        [DataMember(Name = "material")]
        public string Material { get; set; } = string.Empty;
        [DataMember(Name = "class")]
        public string Class { get; set; } = string.Empty;
    }
}
