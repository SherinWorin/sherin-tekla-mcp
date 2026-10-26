# New Drawing Macros - Implementation Status

Date: 2026-10-24

## ISOMETRIC VIEW macro - how to use it (rewritten 2026-10-24)

The first version could never create a view (`Create3dView` throws on a
GA drawing, see `docs/API_NOTES.md`) and its view directions and colours
were guesses.  It now copies the views of a drawing that is already right:

1. **CAPTURE (read only).** Open an isometric drawing that already has its
   three 3D views - the reference `BP01-80001` - and run
   `P142_WF_Isometric`.  It records each view's view plane, display plane,
   scale, sheet position and name in
   `%LOCALAPPDATA%\P142\WallFraming\WF_Isometric_Reference_v2.txt`.  Nothing
   is written into the drawing, so a frozen or issued reference is fine.
   **You can skip this step**: when no reference has been captured, the
   CREATE run finds one by itself - another isometric drawing of the model
   that already has 3D views (issued/frozen ones first) - and reads it
   through `GetSheet()` without opening it (HANDOVER trap 7).
2. **CREATE.** Open the empty isometric drawing of a block (e.g.
   `BP20M-80001`) and run the macro again.  It creates the same views,
   each showing only the parts of that block (block = drawing name before
   the first dash, matched against `ASSEMBLY_POS` with `WfRules.SameBlock`),
   tags them `P142_WF_ISO=1`, commits and saves.
3. REPAIR: run it again on a drawing that already holds views made by the
   macro and it moves any of them that lie off the sheet back onto it
   (scaled position from the reference, else the standard three-pane
   layout: top-left, top-right, bottom).  Nothing else is changed.
   A reference is only chosen if its views lie on its own sheet; the
   first auto-pick ([74]) had positions far off an A1 sheet.  Issued or frozen
   drawings are never written to.

Update 2026-10-24 (after the first live run, reference BP02M-80001 [118]):
- the reference view's **properties** are copied live from the reference
  drawing (`View.Attributes`, read through `GetSheet()`);
- the reference's **depth cut** (ISOMETRIC VIEW 3 is cut through the walls)
  is recorded as a fraction of the reference block and applied to the
  target block's extent in the view's coordinates (`View.RestrictionBox`);
- the owner's **colour code** (WF-Color-Code: GYPSUM_BOARD orange 158,
  C63/CC61 grey 159, RHS/PL4 blue-green 157, CEILING_GYPSUM dark blue 156)
  is applied to the parts of each view - the rules are data in
  `WfIsoColours` (WF_IsometricRules.cs), tested without Tekla;
- every **part mark** in the views is removed;
- a re-run on a drawing that already has the macro's views asks:
  YES = delete those views and rebuild, NO = only move them onto the sheet.
- `LineTypes` is a class in Tekla 2025, not an enum: the WF-Color-Code
  macro's `Enum.Parse` on it fails silently, so its dashed lines were solid.
Optional: add a line `attributes|<view attribute file>` to the reference
file to create the views with that attribute file.

## Overview

Three new macros have been created following the proven wall-framing three-tier pattern (Rules + Core + Loader):

1. **ISOMETRIC VIEW** macro (`WF_IsometricRules.dll`, `WF_Isometric.dll`, `P142_WF_Isometric.dll`) - Creates 3 isometric views with orientations and color coding based on reference GA drawing `[115]` (`BP01-80001`).
2. **SECTIONAL ELEVATION** macro (`WF_SectionRules.dll`, `WF_Section.dll`, `P142_WF_Section.dll`) - Creates section views from wall framing layout section marks based on reference GA drawing `[95]` (`BP01-20001`).
3. ~~CEILING FRAMING LAYOUT macro~~ - **REMOVED 2026-10-24.** Replaced by **P142_BASE_C_W** (`WF_BaseCW.dll`, loader `P142_BASE_C_W.cs`), which builds the BASE, CEILING, WALL drawing (-10001): base HRS plan (dims: `WF_BfDim`), ceiling framing plan with CEILING_GYPSUM (dims: `WF_CeilDim`, rules learned from 19 reference ceiling plans; read-only study `WF_CeilStudy`), wall framing plan (dims: the AIO engine in `WF_Core`). The old sources are in `C:\Users\sheri\projects\_to_delete\old_ceiling_macro`.

All three macros follow the established architecture:
- **Rules (`WF_*Rules.dll`)**: Zero Tekla dependency, pure C# logic, unit-testable headlessly.
- **Core (`WF_*.dll`)**: Tekla Open API 2025 machinery, model/drawing interaction, dimensions, notes, and tags.
- **Loader (`P142_WF_*.dll`)**: Thin entry point in `UserMacros.Macro` with `[Tekla.Macros.Runtime.MacroEntryPointAttribute()]`, dynamically resolving and invoking Core via reflection.

## Build Status: ALL 14 ARTIFACTS BUILD WITH 0 ERRORS AND 0 WARNINGS

The Roslyn build pipeline (`source/build.ps1`) compiles all 14 targets cleanly:

```
Building WF_Rules.dll ...
  OK  WF_Rules.dll  (16384 bytes)
Building WF_License.dll ...
  OK  WF_License.dll  (15360 bytes)
Building WF_Core.dll ...
  OK  WF_Core.dll  (31744 bytes)
Building WF_KeyGen.exe ...
  OK  WF_KeyGen.exe  (23552 bytes)
Building P142_WF_AutoDimension.dll (the loader) ...
  OK  P142_WF_AutoDimension.dll  (8192 bytes)
Building WallFramingAutoDimensionPro-AIO.dll (the AIO loader) ...
  OK  WallFramingAutoDimensionPro-AIO.dll  (7680 bytes)
Building WF_IsometricRules.dll ...
  OK  WF_IsometricRules.dll  (6656 bytes)
Building WF_Isometric.dll ...
  OK  WF_Isometric.dll  (13312 bytes)
Building P142_WF_Isometric.dll (the ISOMETRIC VIEW loader) ...
  OK  P142_WF_Isometric.dll  (7680 bytes)
Building WF_SectionRules.dll ...
  OK  WF_SectionRules.dll  (6656 bytes)
Building WF_Section.dll ...
  OK  WF_Section.dll  (14336 bytes)
Building P142_WF_Section.dll (the SECTIONAL ELEVATION loader) ...
  OK  P142_WF_Section.dll  (7168 bytes)
Building WF_CeilingRules.dll ...
  OK  WF_CeilingRules.dll  (8192 bytes)
Building WF_Ceiling.dll ...
  OK  WF_Ceiling.dll  (13312 bytes)
Building P142_WF_Ceiling.dll (the CEILING FRAMING loader) ...
  OK  P142_WF_Ceiling.dll  (7168 bytes)
BUILD OK
```

### What Is Verified and Working

✅ Full file structure adheres strictly to the three-tier pattern.
✅ Roslyn build script (`build.ps1`) compiles all tiers targeting .NET Framework 4.8 / Tekla 2025 Open API.
✅ Tekla 2025 API compatibility fixes applied:
  - `CoordinateSystem` instantiated with `(Point origin, Vector axisX, Vector axisY)` (3 arguments).
  - `SectionMark` geometric properties resolved via verified `LeftPoint` and `RightPoint`.
  - `SectionMark` attribute mark name resolved via `Attributes.MarkName`.
  - `DrawingColors` uses direct enum values (e.g. `DrawingColors.Black`).
  - `Mark` instantiated via valid single-argument constructor `new Mark(ModelObject)` with copy-back attributes pattern.
  - Drawing identification relies on title inspection and explicit type patterns rather than inaccessible `Drawing.Type` property.
  - Safe API stubs in Core machinery provide error resilience until drawing view creation signatures are exercised against live drawings.

## Calibration and Field Inspection Checklist

The rules assemblies (`WF_*Rules.cs`) provide default configurations derived from the MCP analysis of drawings `[115]`, `[95]`, and `[BP1-CF.1]`. To fine-tune the layouts to millimeter-precision production standards:

1. **ISOMETRIC VIEW (`WF_IsometricRules.cs`)**:
   - Reference drawing: `[115]` (`BP01-80001`).
   - Extract exact camera vectors (`DisplayCoordinateSystem`) for the three isometric projections.
   - Refine color palette in `WfIsoColorRule` if custom presentation classes differ.

2. **SECTIONAL ELEVATION (`WF_SectionRules.cs`)**:
   - Reference drawing: `[95]` (`BP01-20001`).
   - Confirm paper positions `PaperX, PaperY` for views S1–S6 and notes 1–12.
   - Verify level mark positions (`SSL`, `FFL`, `T.O.S`).

3. **CEILING FRAMING (`WF_CeilingRules.cs`)**:
   - Reference drawing: `[BP1-CF.1]`.
   - Confirm plan view and section view scales and positions on sheet.
   - Validate assembly positioning filters for each block prefix.

## Testing & Deployment

To compile and deploy all macros directly to the Tekla Structures macro directory:

**Command Prompt (Windows)**:
```cmd
powershell -NoProfile -File source\build.ps1 -Deploy
```

Or without deploying (local build only):
```cmd
powershell -NoProfile -File source\build.ps1
```
