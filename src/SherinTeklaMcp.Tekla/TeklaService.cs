using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SherinTeklaMcp.Core;
using SherinTeklaMcp.Core.Geometry;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Settings;
using TS = Tekla.Structures;
using TSC = Tekla.Structures.Catalogs;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using TSMUI = Tekla.Structures.Model.UI;

namespace SherinTeklaMcp.Tekla
{
    /// <summary>
    /// Live Tekla Structures 2025 backend (Open API). Rules followed everywhere:
    /// - geometry is read and written in the GLOBAL work plane; the user's plane is restored in finally;
    /// - nothing here commits on its own - the write pipeline calls <see cref="CommitChanges"/>;
    /// - no safety decisions here - those are in Core (WriteOperations).
    /// </summary>
    public sealed class TeklaService : ITeklaService
    {
        /// <summary>Parts are enumerated by these Open API object kinds.</summary>
        private static readonly TSM.ModelObject.ModelObjectEnum[] PartKinds =
        {
            TSM.ModelObject.ModelObjectEnum.BEAM,
            TSM.ModelObject.ModelObjectEnum.POLYBEAM,
            TSM.ModelObject.ModelObjectEnum.CONTOURPLATE,
            TSM.ModelObject.ModelObjectEnum.BENT_PLATE,
            TSM.ModelObject.ModelObjectEnum.LOFTED_PLATE,
            TSM.ModelObject.ModelObjectEnum.SPIRAL_BEAM,
            TSM.ModelObject.ModelObjectEnum.BREP,
        };

        private readonly object _gate = new object();
        private readonly ServerSettings _settings;
        private TSM.Model? _model;
        private HashSet<string>? _libraryProfiles;
        private List<string>? _parametricPrefixes;
        private HashSet<string>? _materials;

        public TeklaService(ServerSettings settings)
        {
            _settings = settings;
            try { TSM.ModelObjectEnumerator.AutoFetch = true; }
            catch (Exception ex) { Console.Error.WriteLine("[tekla] AutoFetch not available: " + ex.Message); }
        }

        public string BackendName => "tekla2025";

        // ------------------------------------------------------------------ connection

        public ConnectionInfo GetConnectionInfo()
        {
            var info = new ConnectionInfo
            {
                Backend = BackendName,
                TeklaBinDir = TeklaAssemblyResolver.BinDir,
                TeklaModelDllFileVersion = TeklaAssemblyResolver.ModelDllFileVersion,
                CompiledForTeklaMajor = TeklaAssemblyResolver.CompiledForMajor,
            };
            try
            {
                var model = GetModel();
                var mi = model.GetInfo();
                info.Connected = true;
                info.ModelName = mi.ModelName ?? "";
                info.ModelPath = mi.ModelPath ?? "";
                info.Message = "Connected to Tekla Structures.";
            }
            catch (Exception ex)
            {
                info.Connected = false;
                info.Message = ex.Message;
            }
            return info;
        }

        private TSM.Model GetModel()
        {
            lock (_gate)
            {
                if (TeklaAssemblyResolver.Problem.Length > 0)
                    throw new InvalidOperationException(TeklaAssemblyResolver.Problem);

                if (_model == null) _model = new TSM.Model();
                if (!_model.GetConnectionStatus())
                {
                    _model = null;
                    throw new InvalidOperationException("Not connected to Tekla Structures 2025. Start Tekla 2025 and open a model, then try again (if Tekla was started after this server, restart the MCP client).");
                }

                string modelName;
                try { modelName = _model.GetInfo()?.ModelName ?? ""; }
                catch (Exception ex)
                {
                    _model = null;
                    throw new InvalidOperationException("Tekla Structures stopped answering (" + ex.Message + "). If Tekla was restarted, restart the MCP client so the server reconnects.", ex);
                }
                if (string.IsNullOrWhiteSpace(modelName))
                    throw new InvalidOperationException("Tekla Structures is running but no model is open. Open a model and try again.");
                return _model;
            }
        }

        /// <summary>Runs <paramref name="action"/> in the global work plane and always restores the user's plane.</summary>
        private static T InGlobalPlane<T>(TSM.Model model, Func<T> action)
        {
            var handler = model.GetWorkPlaneHandler();
            var previous = handler.GetCurrentTransformationPlane();
            handler.SetCurrentTransformationPlane(new TSM.TransformationPlane());
            try
            {
                return action();
            }
            finally
            {
                handler.SetCurrentTransformationPlane(previous);
            }
        }

        // ------------------------------------------------------------------ reads

        public ObjectQueryResult GetSelectedObjects(bool includeUdas, int limit)
        {
            var model = GetModel();
            return InGlobalPlane(model, () =>
            {
                var result = new ObjectQueryResult();
                var enumerator = new TSMUI.ModelObjectSelector().GetSelectedObjects();
                var total = 0;
                while (enumerator.MoveNext())
                {
                    var mo = enumerator.Current;
                    if (mo == null) continue;
                    total++;
                    if (limit > 0 && result.Objects.Count >= limit) continue;
                    result.Objects.Add(MapFull(mo, includeUdas));
                }
                result.Scanned = total;
                result.Count = result.Objects.Count;
                result.Truncated = total > result.Objects.Count;
                if (total == 0) result.Message = "Nothing is selected in Tekla.";
                else if (result.Truncated) result.Message = $"{total} objects selected, {result.Count} returned (limit).";
                return result;
            });
        }

        public ObjectQueryResult GetObjects(IReadOnlyList<string> guids, bool includeUdas)
        {
            var model = GetModel();
            return InGlobalPlane(model, () =>
            {
                var result = new ObjectQueryResult { Scanned = guids.Count };
                foreach (var g in guids)
                {
                    var mo = FindByGuid(model, g);
                    if (mo == null) result.NotFound.Add(g);
                    else result.Objects.Add(MapFull(mo, includeUdas));
                }
                result.Count = result.Objects.Count;
                return result;
            });
        }

        public ObjectQueryResult FindObjects(ObjectFilter filter, bool includeUdas, int limit, int maxScan)
        {
            var model = GetModel();
            return InGlobalPlane(model, () =>
            {
                var result = new ObjectQueryResult();
                var matched = 0;
                var scanned = 0;
                var stopped = false;
                foreach (var kind in PartKinds)
                {
                    var enumerator = model.GetModelObjectSelector().GetAllObjectsWithType(kind);
                    while (enumerator.MoveNext())
                    {
                        if (scanned >= maxScan) { stopped = true; break; }
                        var mo = enumerator.Current;
                        if (!(mo is TSM.Part)) continue;
                        scanned++;
                        var basic = MapBasic(mo);
                        if (!filter.Matches(basic)) continue;
                        matched++;
                        if (limit > 0 && result.Objects.Count >= limit) continue;
                        Enrich(mo, basic, includeUdas);
                        result.Objects.Add(basic);
                    }
                    if (stopped) break;
                }
                result.Scanned = scanned;
                result.Count = result.Objects.Count;
                result.Truncated = stopped || matched > result.Objects.Count;
                if (stopped) result.Message = $"Scan stopped after {maxScan} parts (MaxObjectsToScan). Narrow the filter or raise the limit.";
                else if (matched > result.Objects.Count) result.Message = $"{matched} parts matched, {result.Count} returned (limit).";
                return result;
            });
        }

        // ------------------------------------------------------------------ UI (selection / highlight)

        public int SelectObjects(IReadOnlyList<string> guids)
        {
            var model = GetModel();
            var list = new ArrayList();
            foreach (var g in guids)
            {
                var mo = FindByGuid(model, g);
                if (mo != null) list.Add(mo);
            }
            new TSMUI.ModelObjectSelector().Select(list);
            return list.Count;
        }

        public int HighlightObjects(IReadOnlyList<string> guids, double red, double green, double blue)
        {
            var model = GetModel();
            var ids = new List<TS.Identifier>();
            foreach (var g in guids)
            {
                var mo = FindByGuid(model, g);
                if (mo != null) ids.Add(mo.Identifier);
            }
            if (ids.Count == 0) return 0;
            return TSMUI.ModelObjectVisualization.SetTemporaryState(ids, new TSMUI.Color(red, green, blue, 1.0)) ? ids.Count : 0;
        }

        // ------------------------------------------------------------------ catalog

        public CatalogCheck CheckCatalog(string profile, string material)
        {
            GetModel();
            var check = new CatalogCheck();
            lock (_gate)
            {
                var catalog = new TSC.CatalogHandler();
                if (!catalog.GetConnectionStatus())
                {
                    check.Note = "Tekla catalog is not connected.";
                    return check;
                }

                if (!string.IsNullOrWhiteSpace(profile))
                {
                    if (_libraryProfiles == null)
                    {
                        var items = catalog.GetLibraryProfileItems();
                        items.SelectInstances = false;
                        _libraryProfiles = new HashSet<string>(Drain<TSC.LibraryProfileItem>(items).Select(p => p.ProfileName ?? ""), StringComparer.OrdinalIgnoreCase);
                        var parametric = catalog.GetParametricProfileItems();
                        parametric.SelectInstances = false;
                        _parametricPrefixes = Drain<TSC.ParametricProfileItem>(parametric)
                            .Select(p => p.ProfilePrefix ?? "").Where(p => p.Length > 0)
                            .OrderByDescending(p => p.Length).ToList();
                    }
                    var name = profile.Trim();
                    if (_libraryProfiles.Contains(name)) check.ProfileExists = true;
                    else if (_parametricPrefixes!.Any(prefix => name.Length > prefix.Length &&
                                                               name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                                               char.IsDigit(name[prefix.Length])))
                    {
                        check.ProfileExists = true;
                        check.Note = "Parametric profile: the prefix exists; Tekla checks the dimensions on insert.";
                    }
                    else check.ProfileExists = false;
                }

                if (!string.IsNullOrWhiteSpace(material))
                {
                    if (_materials == null)
                    {
                        _materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var m in Drain<TSC.MaterialItem>(catalog.GetMaterialItems()))
                        {
                            foreach (var n in new[] { m.MaterialName, m.AliasName1, m.AliasName2, m.AliasName3 })
                                if (!string.IsNullOrWhiteSpace(n)) _materials.Add(n);
                        }
                    }
                    check.MaterialExists = _materials.Contains(material.Trim());
                }
            }
            return check;
        }

        // ------------------------------------------------------------------ writes (no commit here)

        public TeklaObjectInfo InsertBeam(PartSpec spec)
        {
            var start = Point3.TryParse(spec.Start) ?? throw new ArgumentException("Start point must be 'x,y,z'.");
            var end = Point3.TryParse(spec.End) ?? throw new ArgumentException("End point must be 'x,y,z'.");
            var model = GetModel();
            return InGlobalPlane(model, () =>
            {
                var beam = new TSM.Beam(ToTekla(start), ToTekla(end));
                beam.Profile.ProfileString = spec.Profile;
                beam.Material.MaterialString = spec.Material;
                if (!string.IsNullOrWhiteSpace(spec.Class)) beam.Class = spec.Class;
                if (!string.IsNullOrWhiteSpace(spec.Name)) beam.Name = spec.Name;
                if (!string.IsNullOrWhiteSpace(spec.PositionPlane))
                    beam.Position.Plane = (TSM.Position.PlaneEnum)Enum.Parse(typeof(TSM.Position.PlaneEnum), spec.PositionPlane, true);
                if (!string.IsNullOrWhiteSpace(spec.PositionDepth))
                    beam.Position.Depth = (TSM.Position.DepthEnum)Enum.Parse(typeof(TSM.Position.DepthEnum), spec.PositionDepth, true);
                if (!string.IsNullOrWhiteSpace(spec.PositionRotation))
                    beam.Position.Rotation = (TSM.Position.RotationEnum)Enum.Parse(typeof(TSM.Position.RotationEnum), spec.PositionRotation, true);

                if (!beam.Insert())
                    throw new InvalidOperationException($"Tekla refused to insert {spec.Profile} from ({spec.Start}) to ({spec.End}).");
                return MapFull(beam, false);
            });
        }

        public List<FieldChange> ModifyObject(string guid, ModifySpec spec)
        {
            var model = GetModel();
            var mo = FindByGuid(model, guid) ?? throw new InvalidOperationException("Object not found.");
            var part = mo as TSM.Part;
            var changes = new List<FieldChange>();

            void Track(string field, string before, string after)
            {
                if (!string.IsNullOrWhiteSpace(after) && !string.Equals(before ?? "", after, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new FieldChange { Guid = guid, Field = field, Before = before ?? "", After = after });
            }

            var needsPartModify = spec.Profile.Length > 0 || spec.Material.Length > 0 || spec.Class.Length > 0 || spec.Name.Length > 0 || spec.Finish.Length > 0;
            if (needsPartModify)
            {
                if (part == null) throw new InvalidOperationException($"Object is a {mo.GetType().Name}, not a part: only UDAs can be changed on it.");
                Track("Profile", part.Profile.ProfileString, spec.Profile);
                Track("Material", part.Material.MaterialString, spec.Material);
                Track("Class", part.Class, spec.Class);
                Track("Name", part.Name, spec.Name);
                Track("Finish", part.Finish, spec.Finish);
                if (spec.Profile.Length > 0) part.Profile.ProfileString = spec.Profile;
                if (spec.Material.Length > 0) part.Material.MaterialString = spec.Material;
                if (spec.Class.Length > 0) part.Class = spec.Class;
                if (spec.Name.Length > 0) part.Name = spec.Name;
                if (spec.Finish.Length > 0) part.Finish = spec.Finish;
                if (!part.Modify()) throw new InvalidOperationException("Tekla refused the modification.");
            }

            foreach (var uda in spec.Udas)
            {
                var before = "";
                mo.GetUserProperty(uda.Key, ref before);
                // Return value not relied on - the write pipeline reads the value back to verify it.
                mo.SetUserProperty(uda.Key, uda.Value ?? "");
                Track("UDA:" + uda.Key, before, uda.Value ?? "");
            }
            return changes;
        }

        public bool DeleteObject(string guid)
        {
            var model = GetModel();
            var mo = FindByGuid(model, guid);
            return mo != null && mo.Delete();
        }

        public void CommitChanges()
        {
            // Result checked by the read-back verification in the write pipeline.
            GetModel().CommitChanges();
        }

        // ------------------------------------------------------------------ mapping

        private static TSM.ModelObject? FindByGuid(TSM.Model model, string guid)
        {
            if (!Guid.TryParse((guid ?? "").Trim(), out var g)) return null;
            var mo = model.SelectModelObject(new TS.Identifier(g));
            return mo != null && mo.Identifier.ID != 0 ? mo : null;
        }

        /// <summary>Cheap mapping (no report properties): used to filter large scans.</summary>
        private static TeklaObjectInfo MapBasic(TSM.ModelObject mo)
        {
            var info = new TeklaObjectInfo
            {
                Guid = mo.Identifier.GUID.ToString().ToUpperInvariant(),
                Id = mo.Identifier.ID,
                Type = mo.GetType().Name,
            };

            if (mo is TSM.Part part)
            {
                info.IsPart = true;
                info.Name = part.Name ?? "";
                info.Profile = part.Profile?.ProfileString ?? "";
                info.Material = part.Material?.MaterialString ?? "";
                info.Class = part.Class ?? "";
                info.Finish = part.Finish ?? "";
                info.PartPrefix = part.PartNumber?.Prefix ?? "";
                info.PartStartNumber = part.PartNumber?.StartNumber;
                info.AssemblyPrefix = part.AssemblyNumber?.Prefix ?? "";
                info.AssemblyStartNumber = part.AssemblyNumber?.StartNumber;
                if (part.Position != null)
                {
                    info.PositionPlane = part.Position.Plane.ToString();
                    info.PositionDepth = part.Position.Depth.ToString();
                    info.PositionRotation = part.Position.Rotation.ToString();
                }
            }

            if (mo is TSM.Beam beam)
            {
                info.Start = FromTekla(beam.StartPoint);
                info.End = FromTekla(beam.EndPoint);
            }
            else if (mo is TSM.ContourPlate plate)
            {
                info.Contour = ContourOf(plate.Contour);
            }
            else if (mo is TSM.PolyBeam poly)
            {
                info.Contour = ContourOf(poly.Contour);
                if (info.Contour.Count >= 2)
                {
                    info.Start = info.Contour[0];
                    info.End = info.Contour[info.Contour.Count - 1];
                }
            }

            if (info.Start != null && info.End != null)
            {
                info.AxisLengthMm = Math.Round(info.Start.DistanceTo(info.End), 2);
                info.IsVertical = GeometryMath.IsVertical(info.Start, info.End);
            }
            return info;
        }

        private static TeklaObjectInfo MapFull(TSM.ModelObject mo, bool includeUdas)
        {
            var info = MapBasic(mo);
            Enrich(mo, info, includeUdas);
            return info;
        }

        /// <summary>Adds report properties (LENGTH, WEIGHT, PART_POS, ASSEMBLY_POS) and optionally UDAs.</summary>
        private static void Enrich(TSM.ModelObject mo, TeklaObjectInfo info, bool includeUdas)
        {
            double length = 0;
            if (mo.GetReportProperty("LENGTH", ref length)) info.ReportLengthMm = Math.Round(length, 2);
            double weight = 0;
            if (mo.GetReportProperty("WEIGHT", ref weight)) info.WeightKg = Math.Round(weight, 3);
            var partPos = "";
            if (mo.GetReportProperty("PART_POS", ref partPos)) info.PartPosition = partPos ?? "";
            var assemblyPos = "";
            if (mo.GetReportProperty("ASSEMBLY_POS", ref assemblyPos)) info.AssemblyPosition = assemblyPos ?? "";

            if (!includeUdas) return;
            var table = new Hashtable();
            info.Udas = new Dictionary<string, string>();
            mo.GetAllUserProperties(ref table);
            foreach (DictionaryEntry entry in table)
                info.Udas[entry.Key?.ToString() ?? ""] = entry.Value?.ToString() ?? "";
        }

        private static List<Point3> ContourOf(TSM.Contour? contour)
        {
            var points = new List<Point3>();
            if (contour?.ContourPoints == null) return points;
            foreach (var item in contour.ContourPoints)
                if (item is TSG.Point p) points.Add(FromTekla(p));
            return points;
        }

        private static Point3 FromTekla(TSG.Point p) => new Point3(Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3));

        private static TSG.Point ToTekla(Point3 p) => new TSG.Point(p.X, p.Y, p.Z);

        private static IEnumerable<T> Drain<T>(IEnumerator enumerator) where T : class
        {
            while (enumerator.MoveNext())
                if (enumerator.Current is T item) yield return item;
        }
    }
}
