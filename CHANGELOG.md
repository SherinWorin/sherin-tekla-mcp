# Changelog

## Unreleased

### Added

- 2026-10-26 **Section marks and section views, rule-based**: `WfSectionRules.SectionCuts` - four inside-out cuts (one looking at each outer wall line), plus one per full-length partition (seen from the larger room), plus a ledge-wall cut when there is no partition; learned from the S marks of 19 reference plans (16 exact). P142_BASE_C_W puts S1..Sn on the base plan (house-style mark properties). P142_WF_Section CREATE mode: on an empty SECTIONAL ELEVATION drawing it reads those marks (or the rule) and builds one view per mark (view X = mark, Y = global Z, box to just behind the viewed wall), house-style properties, template panes, colour code, no part marks. Notes and level lines of the style view of the same name are re-created on each section; dimensions by rule (BP02M-20001): overall height, base | wall | ceiling, FFL, middle-track heights of the viewed wall, door / window openings (width and height tagged OPENING); LGS behind the gypsum shown as hidden (dashed) lines in its colour. Self-check `tests/SectionCutsCheck.cs`.
- 2026-10-26 **P142_BASE_C_W is rule-based**: the three plans are built from the pod's own parts (block extent from ASSEMBLY_POS, levels from the lowest wall BOTTOM_TRACK and the lowest CEILING BEAM, part lists by level, boxes by rule) - no twin pod, nothing copied. View properties from any finished BASE, CEILING, WALL drawing of the model (house style), else defaults at 1:15. Not yet rule-based: base-plan S1..S5 section marks, notes, lifting arrows.
- 2026-10-26 **Release package for a second computer**: `macros/wall-framing/install.cmd` (Command Prompt, no PowerShell) and `install.ps1` now install all five macros; `README_INSTALL.txt`; `-MacroFolder` option for a macro folder of your own (XS_MACRO_DIRECTORY). The zip itself (it contains the Pro module) is NOT in this public repository.
- `tests/LoaderSignatureCheck.cs` - checks that every type / method / field each loader finds by reflection exists with the right signature, that WF_Pro matches the rules API version, and that all WF_Pro member references resolve.
- **P142_BASE_C_W** (`WF_BaseCW.cs` + loader `P142_BASE_C_W.cs`): builds the BASE, CEILING, WALL drawing (-10001) of a pod from its twin pod's drawing - base HRS plan, ceiling framing plan (with CEILING_GYPSUM, view 50 mm below the ceiling), wall framing plan; notes, section marks, symbols copied; dimensions by the engines below. Read-only ceiling study mode on a finished drawing.
  - `WF_BfDim.cs` - the P142_BF_AutoDimension engine as a library function (base HRS plan).
  - `WF_CeilDim.cs` - ceiling plan dimensions, rules learned from 19 reference ceiling plans (panel stud chains, frame chains, overall, gypsum offsets, openings).
  - `WF_Core.DimensionViewAioRun` - the AIO wall-framing engine on a given view (wall plan); the Pro module now binds to the calling WF_Core.

- Three new Tekla Structures 2025 drawing macros in `macros/wall-framing/` following the three-tier pattern (Rules + Core + Loader):
  - **ISOMETRIC VIEW** macro (`WF_IsometricRules.cs`, `WF_Isometric.cs`, `P142_WF_Isometric.cs`) targeting GA drawing `[115]` (`BP01-80001`).
  - **SECTIONAL ELEVATION** macro (`WF_SectionRules.cs`, `WF_Section.cs`, `P142_WF_Section.cs`) targeting GA drawing `[95]` (`BP01-20001`).
  - ~~CEILING FRAMING LAYOUT macro~~ - removed 2026-10-24, replaced by P142_BASE_C_W (below).
  - Updated Roslyn build script (`source/build.ps1`) to compile all 14 targets with 0 errors and 0 warnings.
  - Comprehensive studies in `docs/ISOMETRIC-VIEW-STUDY.md` and `docs/SECTIONAL-ELEVATION-AND-CEILING-STUDY.md`.

- `macros/wall-framing/` - a **wall-framing layout auto-dimensioner** for Tekla Structures 2025: a drawing macro that reproduces the annotations of a `WALL FRAMING LAYOUT` GA drawing (stud chains, band chains, top outline, overall dimensions, north run, wall-panel marks, section marks, notes).
  - The drawing is found by its **title**, not by a fixed mark, so the same file works on every computer of a project.
  - An **issued or frozen drawing is never written to** - the macro refuses it and says so.
  - The wall panels are found by their prefix, or **automatically** when the prefix matches nothing.
  - The rules and the licence engine live in their own assemblies with **no Tekla reference**; the Tekla macro catalog gets a **thin loader** that loads the modules at run time.
  - **Two editions**: a free one (the dimension chains) and a Pro one (everything), unlocked by a signed licence key that can be tied to the computer. Without a key the Pro features are free for three days. The Pro stages are **not** in this repository - they are a separate module, loaded only when the licence allows it.
  - Deployment packages (`package/WallFramingAutoDimension.zip` and `package/WallFramingAutoDimensionPro.zip`) for installing on another computer, with `INSTALL_NOTES.txt` and an optional installer script.
  - Test harnesses in `macros/wall-framing/tests/`; the reference dumps of the project model are **not** committed (client data) - see `macros/wall-framing/tests/reference/README.md`.
- `HANDOVER/` - the **hand-over pack**: the state of the work, the hard rules, the Tekla 2025 traps, the environment and its gotchas, the build-and-verify commands, the open questions, and the prompt to paste into a fresh Claude session on another computer.
  - `HANDOVER/03-HARD-RULES.md` - the rules that are not negotiable: the issued reference drawing is never written to, the client data stays out of the public repository, the GPLv3 server stays out of the MIT repository, Tekla 2025 only, no project rules in the app layer.
  - `HANDOVER/04-TEKLA-2025-API-TRAPS.md` - the Tekla 2025 behaviour that cost the most time to find, so it does not have to be found again.
  - `HANDOVER/05-BUILD-AND-VERIFY.md` - every command, labelled **Command Prompt (Windows)** or **Ubuntu (WSL)**, plus fifteen state checks.
  - The reference dumps of the project model are **not** in this pack either - the pack points at where they are and how to regenerate them.

### Fixed

- The hand-over pack said that the two copies of the free sources were the same files.  They are not: the dev tree still holds the **old, pre-tiering** sources, and the tiered free sources exist only in the published tree.  `HANDOVER/00-START-HERE.md` now says so, in a block that is hard to miss.
- The pack pointed at seven of the macro's nine documents but never at `ARCHITECTURE.md` or `SETUP.md` - the two that carry the design and the install steps.  `HANDOVER/00-START-HERE.md` now has a table of all nine, saying which question each one answers, and the pack's own `README.md` says plainly that the pack is the map and the design is in those documents.
- The build left the **old merged source** in `source\bin\`.  That file carries the Pro stages in plain text and must never be installed.  The build now removes the three merged artefacts before it starts.

## 0.1.0 - 2026-10-15

First version.

- MCP server (stdio) for Tekla Structures 2025: .NET Framework 4.8 x64 build for Tekla, .NET 8 build with a mock model for CI and demos.
- 16 tools: connection, read selected/by GUID/by filter, select, highlight, quantity report (BOQ + CSV), validation, distance, rules list, wall plan, panel division, create beams, create columns, modify, delete.
- Safety: read-only by default, delete disabled by default, LOW/MEDIUM/HIGH risk, preview + confirm token bound to the exact arguments, catalog check, read-back verification, JSON-lines operation log.
- Rules engine with `config/rules/boundary-wall.json`.
- Tekla DLLs loaded from the installed Tekla 2025 at run time.
- 48 unit tests, MCP smoke test, GitHub Actions CI.
- Not yet tested against a live Tekla 2025 model.
