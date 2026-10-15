using System;
using System.Collections.Generic;
using System.Linq;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Models;

namespace SherinTeklaMcp.Core.Mock
{
    /// <summary>
    /// In-memory stand-in for Tekla: a small sample frame (columns + beams). Used by the unit
    /// tests, by CI, and by the server when SHERIN_TEKLA_MCP_MOCK=1 so the tools can be tried
    /// without Tekla. It never pretends to be Tekla: Backend is always "mock".
    /// </summary>
    public sealed class MockTeklaService : ITeklaService
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, TeklaObjectInfo> _objects = new Dictionary<string, TeklaObjectInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _selected = new List<string>();
        private int _nextId = 1000;

        public static readonly string[] Profiles = { "HEA200", "HEB200", "IPE200", "IPE300", "RHS100*50*5", "SHS100*100*5", "400*200", "PL10*200" };
        public static readonly string[] Materials = { "S235JR", "S275JR", "S355JR", "C30/37" };

        /// <summary>Number of pending (uncommitted) changes - for tests.</summary>
        public int PendingChanges { get; private set; }
        public int Commits { get; private set; }

        public string BackendName => "mock";

        public MockTeklaService(bool withSampleModel = true)
        {
            if (!withSampleModel) return;
            // 2 x 2 bays, 6 m x 5 m grid, 4 m high: 9 columns + 12 beams.
            for (var ix = 0; ix < 3; ix++)
            for (var iy = 0; iy < 3; iy++)
                AddPart(new Point3(ix * 6000, iy * 5000, 0), new Point3(ix * 6000, iy * 5000, 4000), "HEA200", "S355JR", "2", "COLUMN");
            for (var iy = 0; iy < 3; iy++)
            for (var ix = 0; ix < 2; ix++)
                AddPart(new Point3(ix * 6000, iy * 5000, 4000), new Point3((ix + 1) * 6000, iy * 5000, 4000), "IPE300", "S355JR", "3", "BEAM");
            for (var ix = 0; ix < 3; ix++)
            for (var iy = 0; iy < 2; iy++)
                AddPart(new Point3(ix * 6000, iy * 5000, 4000), new Point3(ix * 6000, (iy + 1) * 5000, 4000), "IPE200", "S275JR", "3", "BEAM");
            _selected.AddRange(_objects.Values.Take(3).Select(o => o.Guid));
            PendingChanges = 0;
        }

        /// <summary>Adds a part directly (test set-up). Returns its GUID.</summary>
        public string AddPart(Point3 start, Point3 end, string profile, string material, string cls, string name)
        {
            lock (_gate)
            {
                var info = Build(start, end, profile, material, cls, name);
                _objects[info.Guid] = info;
                return info.Guid;
            }
        }

        public void SetSelection(IEnumerable<string> guids)
        {
            lock (_gate)
            {
                _selected.Clear();
                _selected.AddRange(guids);
            }
        }

        public ConnectionInfo GetConnectionInfo() => new ConnectionInfo
        {
            Connected = true,
            Backend = BackendName,
            Message = "MOCK backend: a built-in sample model, not Tekla. Unset SHERIN_TEKLA_MCP_MOCK to use Tekla.",
            ModelName = "MockModel",
            ModelPath = @"C:\Mock\MockModel",
        };

        public ObjectQueryResult GetSelectedObjects(bool includeUdas, int limit)
        {
            lock (_gate)
            {
                var all = _selected.Where(_objects.ContainsKey).Select(g => Copy(_objects[g], includeUdas)).ToList();
                return Page(all, limit, all.Count);
            }
        }

        public ObjectQueryResult GetObjects(IReadOnlyList<string> guids, bool includeUdas)
        {
            lock (_gate)
            {
                var result = new ObjectQueryResult();
                foreach (var g in guids)
                {
                    if (_objects.TryGetValue(g, out var o)) result.Objects.Add(Copy(o, includeUdas));
                    else result.NotFound.Add(g);
                }
                result.Count = result.Objects.Count;
                result.Scanned = guids.Count;
                return result;
            }
        }

        public ObjectQueryResult FindObjects(ObjectFilter filter, bool includeUdas, int limit, int maxScan)
        {
            lock (_gate)
            {
                var scanned = _objects.Values.Take(maxScan).ToList();
                var matches = scanned.Where(filter.Matches).Select(o => Copy(o, includeUdas)).ToList();
                var result = Page(matches, limit, scanned.Count);
                if (_objects.Count > maxScan)
                {
                    result.Truncated = true;
                    result.Message = $"Scan stopped after {maxScan} objects.";
                }
                return result;
            }
        }

        public int SelectObjects(IReadOnlyList<string> guids)
        {
            lock (_gate)
            {
                var found = guids.Where(_objects.ContainsKey).ToList();
                _selected.Clear();
                _selected.AddRange(found);
                return found.Count;
            }
        }

        public int HighlightObjects(IReadOnlyList<string> guids, double red, double green, double blue)
        {
            lock (_gate) return guids.Count(_objects.ContainsKey);
        }

        public CatalogCheck CheckCatalog(string profile, string material) => new CatalogCheck
        {
            ProfileExists = string.IsNullOrWhiteSpace(profile) ? (bool?)null : Profiles.Contains(profile.Trim(), StringComparer.OrdinalIgnoreCase),
            MaterialExists = string.IsNullOrWhiteSpace(material) ? (bool?)null : Materials.Contains(material.Trim(), StringComparer.OrdinalIgnoreCase),
            Note = "mock catalog",
        };

        public TeklaObjectInfo InsertBeam(PartSpec spec)
        {
            var start = Point3.TryParse(spec.Start) ?? throw new ArgumentException("Bad start point.");
            var end = Point3.TryParse(spec.End) ?? throw new ArgumentException("Bad end point.");
            lock (_gate)
            {
                var info = Build(start, end, spec.Profile, spec.Material, spec.Class, spec.Name);
                info.PositionPlane = string.IsNullOrEmpty(spec.PositionPlane) ? "MIDDLE" : spec.PositionPlane;
                info.PositionDepth = string.IsNullOrEmpty(spec.PositionDepth) ? "MIDDLE" : spec.PositionDepth;
                info.PositionRotation = string.IsNullOrEmpty(spec.PositionRotation) ? "FRONT" : spec.PositionRotation;
                _objects[info.Guid] = info;
                PendingChanges++;
                return Copy(info, false);
            }
        }

        public List<FieldChange> ModifyObject(string guid, ModifySpec spec)
        {
            lock (_gate)
            {
                if (!_objects.TryGetValue(guid, out var o)) throw new InvalidOperationException("Object not found.");
                var changes = new List<FieldChange>();
                void Set(string field, string before, string after, Action apply)
                {
                    if (string.IsNullOrWhiteSpace(after) || string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) return;
                    apply();
                    changes.Add(new FieldChange { Guid = guid, Field = field, Before = before, After = after });
                }
                Set("Profile", o.Profile, spec.Profile, () => o.Profile = spec.Profile);
                Set("Material", o.Material, spec.Material, () => o.Material = spec.Material);
                Set("Class", o.Class, spec.Class, () => o.Class = spec.Class);
                Set("Name", o.Name, spec.Name, () => o.Name = spec.Name);
                Set("Finish", o.Finish, spec.Finish, () => o.Finish = spec.Finish);
                o.Udas ??= new Dictionary<string, string>();
                foreach (var kv in spec.Udas)
                {
                    o.Udas.TryGetValue(kv.Key, out var before);
                    Set("UDA:" + kv.Key, before ?? "", kv.Value, () => o.Udas[kv.Key] = kv.Value);
                }
                if (changes.Count > 0) PendingChanges++;
                return changes;
            }
        }

        public bool DeleteObject(string guid)
        {
            lock (_gate)
            {
                var removed = _objects.Remove(guid);
                if (removed) { _selected.Remove(guid); PendingChanges++; }
                return removed;
            }
        }

        public void CommitChanges()
        {
            lock (_gate)
            {
                Commits++;
                PendingChanges = 0;
            }
        }

        private TeklaObjectInfo Build(Point3 start, Point3 end, string profile, string material, string cls, string name)
        {
            var length = start.DistanceTo(end);
            return new TeklaObjectInfo
            {
                Guid = Guid.NewGuid().ToString().ToUpperInvariant(),
                Id = _nextId++,
                Type = "Beam",
                IsPart = true,
                Name = name,
                Profile = profile,
                Material = material,
                Class = cls,
                Start = start,
                End = end,
                AxisLengthMm = Math.Round(length, 2),
                ReportLengthMm = Math.Round(length, 2),
                WeightKg = Math.Round(length / 1000.0 * NominalKgPerM(profile), 2),
                IsVertical = GeometryMath.IsVertical(start, end),
                PositionPlane = "MIDDLE",
                PositionDepth = "MIDDLE",
                PositionRotation = "FRONT",
                Udas = new Dictionary<string, string>(),
            };
        }

        /// <summary>Rough kg/m for the mock only - real weights always come from Tekla.</summary>
        private static double NominalKgPerM(string profile)
        {
            switch ((profile ?? "").ToUpperInvariant())
            {
                case "HEA200": return 42.3;
                case "HEB200": return 61.3;
                case "IPE200": return 22.4;
                case "IPE300": return 42.2;
                default: return 20.0;
            }
        }

        private static ObjectQueryResult Page(List<TeklaObjectInfo> all, int limit, int scanned)
        {
            var take = limit <= 0 ? all.Count : Math.Min(limit, all.Count);
            return new ObjectQueryResult
            {
                Objects = all.Take(take).ToList(),
                Count = take,
                Truncated = all.Count > take,
                Scanned = scanned,
                Message = all.Count > take ? $"{all.Count} matched, {take} returned (limit)." : "",
            };
        }

        private static TeklaObjectInfo Copy(TeklaObjectInfo o, bool includeUdas) => new TeklaObjectInfo
        {
            Guid = o.Guid, Id = o.Id, Type = o.Type, IsPart = o.IsPart, Name = o.Name, Profile = o.Profile, Material = o.Material,
            Class = o.Class, Finish = o.Finish, PartPrefix = o.PartPrefix, PartStartNumber = o.PartStartNumber,
            AssemblyPrefix = o.AssemblyPrefix, AssemblyStartNumber = o.AssemblyStartNumber,
            PartPosition = o.PartPosition, AssemblyPosition = o.AssemblyPosition,
            Start = o.Start == null ? null : new Point3(o.Start.X, o.Start.Y, o.Start.Z),
            End = o.End == null ? null : new Point3(o.End.X, o.End.Y, o.End.Z),
            AxisLengthMm = o.AxisLengthMm, ReportLengthMm = o.ReportLengthMm, WeightKg = o.WeightKg,
            PositionPlane = o.PositionPlane, PositionDepth = o.PositionDepth, PositionRotation = o.PositionRotation,
            IsVertical = o.IsVertical,
            Udas = includeUdas ? new Dictionary<string, string>(o.Udas ?? new Dictionary<string, string>()) : null,
        };
    }
}
