# P142 Tekla 2025 drawing macros – handover (state of 2026-10-26)

Read this first when you continue the work in a new session (any platform).
Repo: github.com/SherinWorin/sherin-tekla-mcp (PUBLIC), branch `feature/wall-framing-dimensioner`,
folder `macros/wall-framing/`. Last commit of this round: see `git log`.

## 1. What the macros do (drawing mode, Applications > Macros)

| Macro (loader .cs) | Core (DLL, `source/`) | Open drawing | Result |
|---|---|---|---|
| P142_POD_DRAWINGS | `WF_PodDrawings.cs` → WF_BaseCW.dll | any drawing of a reference pod | asks ONLY the new pod name; creates `<POD>-10001/-20001/-80001/-CPL/-WPL` (Name, Title1 = pod; Title2/3 + drawing UDAs from the reference pod's same drawing, else defaults; all other settings from `model\attributes\P142_<suffix>.gd`, else `standard`). Saves + closes the open drawing first (Tekla cannot insert a drawing while one is open), reopens it at the end. YES = also copy sheet items (key plan, lines, texts). |
| P142_BASE_C_W | `WF_BaseCW.cs` (+ WF_BfDim, WF_CeilDim, WF_CeilStudy, WF_BcwNotes, WF_PodDrawings) → WF_BaseCW.dll | empty `<POD>-10001` | base HRS plan, ceiling plan, wall (LGS) plan from the pod's OWN parts; colour code; hidden lines on base; grey centre lines on ceiling studs; dims: base = BF engine, wall = AIO engine (dims snap to bottom tracks), ceiling = WF_CeilDim; standard notes copied from a finished BCW drawing; lifting arrows; **section marks S1..Sn** on the base plan (rule below). |
| P142_WF_Section | `WF_Section.cs` → WF_Section.dll (+ WF_SectionRules.dll, uses WF_BaseCW/WF_Isometric) | empty `<POD>-20001` | one section view per S mark of the block's -10001 (or the rule if no marks): view X = mark L→R, Y = global Z, box to the viewed wall +150; house-style view props (BP02M-20001 first); panes S1..S5 (extras at sheet right); colours; LGS hidden lines; notes + level lines copied from the same-named style view; **rule dimensions** (below). Other drawings: READ-ONLY study dump. REBUILD after YES. |
| P142_WF_Isometric | `WF_Isometric.cs` → WF_Isometric.dll | empty `<POD>-80001` | 3 iso views from `WF_Isometric_Reference_v2.txt` (captured from BP02M-80001, R00 model); reference drawing found by NAME, else any finished isometric drawing of the model gives the view properties; label text "ISOMETRIC VIEW n" set per pane. Log: `WF_Isometric_log.txt`. |
| P142_WF_AutoDimension / WallFramingAutoDimensionPro-AIO | WF_Core + WF_Pro (private) | wall framing layout / GA sheet | wall panel dimensioning (unchanged this round; Pro is licensed, NOT in the public repo). |

Order for a new pod: P142_POD_DRAWINGS → -10001 P142_BASE_C_W → -20001 P142_WF_Section → -80001 P142_WF_Isometric.

## 2. Owner rules (must hold for every change)
- Every fix is a GENERAL rule for all pods – never tuned to the pod under test.
- Build each pod independently from its own parts (ASSEMBLY_POS `<BLOCK>-...`); never copy geometry from twin/similar pods. Finished drawings are used only as HOUSE STYLE (view properties, label style, notes text).
- Output must match the reference PDFs/DWGs in `C:\Users\sheri\Sherin_AI\Pods-IFA dwgs` (BP02M-10001/-20001/-80001, BP07 ...).
- Colour code: HRS_GREEN, LGS_GREY, WALL_ORANGE, CEILING_VIOLET (`WF_IsometricRules.cs`). No part marks where not needed.
- Never write to issued/frozen drawings, never change the model, ask before irreversible steps.
- Never invent Tekla API members: verify in `C:\TeklaStructures\2025.0\bin\Tekla.Structures.*.xml` and by compiling against the 2025 DLLs; record in `docs/API_NOTES.md`.
- Repo is public: no client dumps, no WF_Pro sources/DLLs, no credentials.
- Terminal commands for the owner: label **Command Prompt (Windows)** (office PC: no PowerShell) or **Ubuntu (WSL)**.

## 3. Rules learned (code + where)
- **Section cuts** `WfSectionRules.SectionCuts` (tested: `tests/SectionCutsCheck.cs`; 16/19 reference plans exact):
  4 inside-out cuts at the outer wall lines (-x,+x,-y,+y); +1 per full-length partition (inner wall line ≥55 % of the pod, >200 from the outer lines, merged within 300) seen from the larger room; if none, +1 on the ledge wall (-LW) returns along x. Mark line: outer 45 % of room depth clamped 150..900 in front of the wall; partition 20 % clamped 150..300. Names: S1 = outer cut of the extra's axis/direction, S2 opposite, S3 -, S4 + of the other axis, S5+ extras. Look direction = (L→R) turned +90°.
- **Section dims** `WfSectionCore.SecDims`: left outer = SSL (3 mm under base skid, i.e. lowest level line) → T.O.S (2554); left inner = bottom of LGS wall | middle-track MID-heights | top of wall; right inner = SSL | wall bottom | wall top | T.O.S (126/2365/63); right outer = SSL | FFL | T.O.S (55/2499); every opening (LINTEL + JAMB pair in the same depth band, any wall parallel to the view): width between jamb faces, height FFL (or SILL top) → lintel underside, tag OPENING = StraightDimension.Attributes.MiddleLowerTag set on dims READ BACK from the view after a commit, part count off. Viewed wall = depth band of its longest BOTTOM_TRACK near z = -Ahead.
- **BCW plans**: levels from lowest BOTTOM_TRACK / lowest CEILING BEAM; wall plan box z = lowest track −50 … main track (+300); base plan = BASE_FRAME assembly only.
- **Pod drawing set** `WfSectionRules.PodDrawings` (+ `ReplacePod` whole-word).

## 4. Build, test, deploy
- Sources: `macros/wall-framing/source/`. Windows build: `source\build.ps1` (csc). In the Linux container: mono `mcs -sdk:4.5` against the 2025 DLLs, e.g. WF_SectionRules → WF_BaseCW (refs Rules, License, Core, Isometric, IsometricRules, SectionRules) → WF_Section (refs SectionRules, Rules, BaseCW, Isometric, IsometricRules).
- Checks: `tests/LoaderSignatureCheck.cs` (run in the DLL folder: ALL CHECKS PASSED), `tests/SectionCutsCheck.cs`, `tests/ColourCodeCheck.cs`, `tests/SnapPointsCheck.cs`.
- Install on a PC: loaders `.cs/.bmp` → macro folder (`C:\TeklaStructures\2025.0\Environments\common\macros\drawings` or `<model>\macros\drawings`); DLLs → `%LOCALAPPDATA%\P142\WallFraming`. Loaders load DLLs from bytes on every run (no Tekla restart for DLL changes; a NEW macro needs a restart / Macros dialog to appear).
- Public office package in git (no Pro DLLs): `macros/wall-framing/dist/P142_Office_PC_2026-10-26_public.zip`.
- Full packages (not in git, contain WF_Pro): `C:\Users\sheri\projects\sherin-tekla-mcp\macros\wall-framing\release\P142_Tekla_Macros_2026-10-26_v2.zip` (install.cmd) and `P142_Office_Macros_2026-10-26_v2.zip` (copy-only, see `INSTALL_OFFICE_PC.txt`).
- Per model once: save drawing properties of the reference pod's drawings as `P142_10001`, `P142_20001`, `P142_80001`, `P142_CPL`, `P142_WPL` (done in RIV-MAR-3-R01).
- Logs: `%LOCALAPPDATA%\P142\WallFraming\WF_BaseCW_log.txt`, `WF_Section_log.txt`, `WF_Isometric_log.txt`; study dumps `WF_Section_dump_<mark>.txt`, `WF_BCW_survey.txt`.

## 5. API traps found (Tekla 2025)
- `GADrawing.Insert()` throws CannotInsertDrawingException while any drawing is open.
- `Drawing.Layout` has no public setter; `LayoutAttributes.InternalTableLayoutId/InternalLayout` are internal → copy settings via a saved `.gd` + `GADrawing(name, attributeFile)`.
- Dimension tags: changing `StraightDimension.Attributes` on the objects of a freshly created set did not persist; set them on dims read back from `view.GetAllObjects(typeof(StraightDimension))` after `CommitChanges`, assign `Attributes` back, `Modify()`.
- Drawing marks change between models (R00→R01): find reference drawings by NAME.
- Tekla adds part/weld marks in the background after view creation → repeated removal sweeps with sleeps.
- Views of a drawing that is not open: CS/box/attributes/objects readable; Origin/Width/Height are not.
- Switching the active drawing closes the open one without saving → always SaveActiveDrawing first.

## 6. Open items
- Section rule: 3/19 reference plans differ (BP04M extra direction; BP05/BP05M get an extra ledge cut).
- Section mark line position is an approximation (owner may move marks; P142_WF_Section reads them).
- Opening detection needs LINTEL + JAMB parts; other framings are not found.
- Section/detail marks S1–S5 detail (DET-BF) and the pod location image: not done.
- P142_WF_Ceiling.cs still sits in the laptop's macro catalog (old macro, removed from the package).
