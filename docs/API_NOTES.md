# Tekla Open API notes (Tekla Structures 2025)

Every Tekla API member used in `src/SherinTeklaMcp.Tekla` is listed here with how it was checked. **All of them compile against the official Tekla 2025.0.0 NuGet packages (CI build `build-windows-tekla2025`, first green run 2026-10-15).** "Live 2025" means tested in a running Tekla 2025 with this server - fill it in as you test.

Already verified live on Tekla **2025.0.46985.0** (`C:\TeklaStructures\2025.0\bin`) by the AI-TEKLA MVP (commit `76eef39`, same Open API calls):
- report property `LENGTH` is in **mm** (matches point-to-point distance on 5 beams), `WEIGHT` is in **kg** (46.32 kg/m on UB406*140*46);
- points read in the global plane are global even when the user has a local work plane, and restoring the plane in `finally` works;
- `ContourPoint` inherits `Geometry3d.Point` (X/Y/Z) - confirmed from DLL metadata;
- `Beam.Type` (BeamType) is readable - not used yet here (see open questions).

| API | Used for | Source of truth | Live 2025 |
|---|---|---|---|
| `new Model()`, `Model.GetConnectionStatus()`, `Model.GetInfo().ModelName/.ModelPath` | connection | used by tempalg1n & svMCP servers (compiled, 2025 build in their CI) | TODO |
| `Model.GetWorkPlaneHandler()`, `GetCurrentTransformationPlane()`, `SetCurrentTransformationPlane(new TransformationPlane())` | global plane, restore in finally | same | TODO |
| `Model.UI.ModelObjectSelector().GetSelectedObjects()` / `.Select(ArrayList)` | read / set selection | same | TODO |
| `Model.GetModelObjectSelector().GetAllObjectsWithType(ModelObject.ModelObjectEnum.X)` | search parts (BEAM, POLYBEAM, CONTOURPLATE, BENT_PLATE, LOFTED_PLATE, SPIRAL_BEAM, BREP) | same | TODO |
| `Model.SelectModelObject(new Identifier(Guid))` | find by GUID | same | TODO |
| `ModelObjectEnumerator.AutoFetch = true` | faster enumeration | same | TODO |
| `Part.Name/Profile.ProfileString/Material.MaterialString/Class/Finish` | properties | same | TODO |
| `Part.PartNumber.Prefix/StartNumber`, `AssemblyNumber.Prefix/StartNumber` | numbering series | teknovizier server (pythonnet) | TODO |
| `Part.Position.Plane/Depth/Rotation` and `Position.PlaneEnum/DepthEnum/RotationEnum` | position | tempalg1n | TODO |
| `Beam.StartPoint/EndPoint`, `ContourPlate.Contour.ContourPoints`, `PolyBeam.Contour` | geometry | tempalg1n | TODO |
| `ModelObject.GetReportProperty("LENGTH"/"WEIGHT", ref double)`, `("PART_POS"/"ASSEMBLY_POS", ref string)` | quantities | tempalg1n | units verified by the AI-TEKLA MVP (mm, kg) |
| `ModelObject.GetAllUserProperties(ref Hashtable)`, `GetUserProperty(name, ref string)`, `SetUserProperty(name, string)` | UDAs | tempalg1n | TODO |
| `new Beam(Point, Point)`, `.Insert()`, `.Modify()`, `.Delete()`, `Model.CommitChanges()` | writes | tempalg1n | TODO |
| `Model.UI.ModelObjectVisualization.SetTemporaryState(List<Identifier>, Color)` | highlight | tempalg1n | TODO |
| `Catalogs.CatalogHandler`, `GetLibraryProfileItems()` (`LibraryProfileItem.ProfileName`), `GetParametricProfileItems()` (`ParametricProfileItem.ProfilePrefix`), `GetMaterialItems()` (`MaterialItem.MaterialName`, `AliasName1-3`) | catalog check | tempalg1n | TODO |

Return values of `CommitChanges`, `SetUserProperty` and `GetAllUserProperties` are not relied on (the references only call them as statements); the write pipeline verifies by reading the values back.

## Loading the Tekla DLLs

The Tekla projects reference the NuGet packages `Tekla.Structures`, `Tekla.Structures.Model`, `Tekla.Structures.Catalogs` **2025.0.0** with `ExcludeAssets="runtime"`, so no Tekla DLL is copied to the output. `TeklaAssemblyResolver` loads them from the installed Tekla 2025 `bin` (and `bin\Net48Runtime` if present).

Why: Tekla 2024+ connects through Trimble.Remoting and names the channel after the DLL **file version** (for example `...Console:2025.0.52577.0`). The NuGet DLLs carry `2025.0.0.0`, so a program running on the NuGet copies cannot find the running Tekla. (Reported by the svMCP project, which worked around it by patching channel names; loading the installed DLLs avoids the problem.) Assembly identity is `2025.0.0.0` in both (confirmed by the Tekla 2025 Open API release notes), so binding works.

Bin folder search order: `TeklaBinDir` in settings -> `TEKLA_BIN_DIR` -> folder of a running `TeklaStructures.exe` (2025 only) -> `C:\Program Files\Tekla Structures\2025.0\bin` -> `C:\TeklaStructures\2025.0\bin`. A different major version is refused with a clear message.

## 2025 changes to keep in mind (from the 2025 Open API release notes)

- `Tekla.Structures` 2025: matrix multiplication order changed (now associative A*B). Not used yet - check before adding matrix code.
- `GetReportProperty` can read several objects in one call - a future speed-up for big quantity reports.

## Open questions (verify live)

- Columns are created as vertical `Beam` objects (the same Open API class Tekla uses for columns). The AI-TEKLA MVP reads `Beam.Type` (BeamType), so setting `Type = COLUMN` on created columns is a likely v0.2 improvement - live-test `BeamType=COLUMN` first (also open in the MVP).
- Tool annotations (`readOnlyHint`/`destructiveHint`) are written in the tool descriptions for now. The MCP C# SDK attribute properties for them were not used because no reference confirms them for SDK 1.4.0.
- Tekla reconnect after a Tekla restart: the server drops its `Model` and tries again, but the Open API may need a server restart (restart Claude Desktop).
