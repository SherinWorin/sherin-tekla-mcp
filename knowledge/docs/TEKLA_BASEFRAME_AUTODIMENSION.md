# Pod base frame auto-dimension macro (Tekla 2025): `P142_BF_AutoDimension`

Location: `C:\TeklaStructures\2025.0\Environments\common\macros\drawings\P142_BF_AutoDimension.cs` (backup copy in `Downloads\Tekla-MCP`).
Use: open the base-frame **assembly drawing**, then Applications & components > P142_BF_AutoDimension. It works on the TopView (plan). If the view already has dimensions it asks before adding more. It writes a log to `Downloads\Tekla-MCP\bfdim_log_<mark>.txt`.

## Verification (2026-09-24)
Reference: [BP4-BF.1] (final IFC drawing, 21 straight dimension sets, all in one style). Test: [BP4-BF.1 - 1] with no dimensions, then the macro run, then a comparison of the two dumps:
- 21/21 sets with identical values, direction and style.
- 20/21 dimension lines at the identical position. FS11's length line is 3.2 mm paper closer to the member (the drafter used 22.97 mm there but 19.75 mm for FS13; the macro uses 19.75 mm for both).
- [BP4-BF.1 - 1] was saved with the generated dimensions. [BP4-BF.1] was not modified.

## Rules (derived from BP4-BF.1)
Part outlines are taken in view coordinates (`TransformationPlane(view.DisplayCoordinateSystem)`, then `GetSolid()` min/max). The perimeter band thickness T is the width of the leftmost post (60).
- **Side chains (4):** every part whose near face lies within T of that side's outline (the silhouette) contributes its two edges. On stepped sides, only outline segments at least 35% of the side span count.
- **Tier 2:** break points where the outline steps (top 1786/1402, right 2401/612).
- **Overall:** bottom, left and top.
- **SHS in the band:** inner face | SHS | outer face (20 | 40).
- **Stub + plate:** one per frame member, choosing the stub nearest a member end; a perpendicular stub must sit within the member span. If the stub is collinear with the member, the member length joins the chain (176 | 4 | 1486); otherwise a total is added (180).
- **Frame length:** each RHS40 frame not already covered by a stub chain.
- **Frame position:** from the parallel perimeter inner face, skipped if the near face is already in a side chain (924 | 40, 180 | 40).
- **Offsets:** paper-mm constants taken from BP4-BF.1, multiplied by the view scale.
- **Style** (applied by reflection): Relative, ExtensionLine No, ShortDimension Outside, NoArrow, Color NewLine6, Whole / NoDecimals, Placing Fixed, Arial narrow 2.75, AboveDimensionLine.

## Tekla 2025 API: confirmed on this install (compiled and run)
- `new StraightDimensionSetHandler().CreateDimensionSet(ViewBase, PointList, Vector up, double distance)`; commit after each call.
- **Gotcha:** `set.Modify()` after changing attributes resets the dimension line position to view coordinate 0. Re-set `set.Distance` and call `Modify()` again.
- `StraightDimensionSet.Distance` is readable and writable. It is measured from the outermost point in the up direction, in view (model) units = paper mm × scale.
- `StraightDimensionSet.Attributes` exists twice (class and base class), so a plain `GetProperty("Attributes")` throws "Ambiguous match found". Pick the property declared on the concrete type.
- Other calls confirmed: `View.DisplayCoordinateSystem`, `View.ViewType`, `View.GetAllObjects(typeof(...))`, `StraightDimensionSet.GetObjects()`, `StraightDimension.StartPoint/EndPoint/UpDirection/Distance`, `DrawingObject.Delete()`, `Part.ModelIdentifier.ID` → `Model.SelectModelObject(new Identifier(id))`, `TSM.Part.GetSolid().MinimumPoint/MaximumPoint`, `GetReportProperty("PART_POS", ref s)`.
- Macro gotchas: `using System.Windows.Forms` makes `View` ambiguous, so fully qualify MessageBox. Tekla caches a compiled macro for the whole session, so an edited macro needs a new file name (or a Tekla restart). New macros appear after ≡ > Catalog management > Reload catalog.

## Helper
`P142_DumpDimensions` (read-only) writes every dimension set (points, distance, all style values) and every part outline of the open drawing to `Downloads\Tekla-MCP\dimdump_<mark>.txt`. Use it to capture a new reference format or to check a result.

## Limits
- Built for base frames modelled like BP4: RHS perimeter band, RHS40 frames, RHS stubs with 4 mm plates, SHS set into the band. Other layouts may need new rules. Check them with the dump helper.
- It creates dimensions only. The "OPEN END" texts, part marks, material list and A-A / B-B details come from the drawing settings and layout.
