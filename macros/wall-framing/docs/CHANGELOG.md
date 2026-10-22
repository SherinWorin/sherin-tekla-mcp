# Changelog

All notable changes to the wall-framing auto-dimensioner.

The format follows [Keep a Changelog](https://keepachangelog.com/), and
the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

* **The two editions.** The macro is sold as a **free** edition and a
  **Pro** edition:
  * the **free** edition draws the dimension chains — the stud chain of
    every wall panel and the band chains of the layout (14 dimension sets);
  * the **Pro** edition draws everything — the chains, the top outline,
    the overall dimensions, the north run, the 11 wall-panel marks, the
    5 section marks and the 3 notes (37 objects).

  The cut is at the **assembly boundary**, not with an `if` in one file:
  the Pro stages live in `WF_Pro.dll` and the Pro rules in
  `WF_Rules_Pro.dll`, and `WfCore.Execute` ends with
  `if (edition == 1) RunPro(result);`.  The Pro module is loaded by
  reflection, so the free edition works without it, and a Pro licence on
  a computer that has no `WF_Pro.dll` still runs — it just does not get
  the Pro annotations.
* **The licence engine** (`source/WF_License.cs`, no Tekla reference):
  * a licence key is one line, `WF1.<payload>.<signature>`, signed with
    the owner's **RSA-2048** private key over **SHA-256** of the
    payload.  The private key is not in the repository and never will
    be;
  * a key can be tied to one computer: three hashes (the machine GUID,
    the volume serial, the processor id) are taken, and the key is
    accepted when **at least two of the three** match, so that a
    replaced disk or a new processor does not break the licence;
  * without a key there is a **three day trial**, counted in UTC, that
    starts at the **first run** of the macro on that computer;
  * a clock that is moved back by more than an hour **ends** the trial;
    an hour of difference is tolerated;
  * the state is kept in `%LOCALAPPDATA%\P142\WallFraming\state.txt`
    **and** in `HKCU\Software\P142\WallFraming`, so deleting the file
    alone does not give a new trial.
* **The owner's key tool** `WF_KeyGen.exe`: `newkey`, `issue`, `sign`,
  `machine`, and — new — `install` (put a key into this computer and take
  it into use), `status` (the edition, the customer, the expiry and the
  trial of this computer) and `remove`.
* **A thin loader.** The file the Tekla macro catalog compiles
  (`P142_WF_AutoDimension.cs`) holds **no rule and no drawing logic** any
  more: it shows the message box, loads `WF_Core.dll` and hands the work
  over.  That is what keeps the Pro stages out of the published
  repository.
* **`source/WF_Core.cs`** — the shared drawing machinery: the target
  selection, the plan view, the reading of the model, the writers and the
  licence check.
* **`tests/test_license.py`** — 42 checks of the licence engine, with no
  Tekla running.  The tests make a throwaway key pair **in memory** and
  never write a private key to the disk.
* **`tests/test_loader.py`** — checks that the **deployment** is complete:
  the core and the Pro module load out of `%LOCALAPPDATA%`, the types
  resolve, the licence answers and the Pro module has its entry point.
  The Tekla assemblies are loaded, but no Tekla has to be running.
* **`docs/LICENSING.md`** — the two editions, the trial, the key
  workflow, the machine binding and the activation step the owner has to
  do once.
* **The macro works on any wall layout drawing, not on a fixed mark.**
  A drawing is recognised by its **title** (`WALL FRAMING`, `WALL LAYOUT`
  or `WALL FRAMING LAYOUT` in `Title1`/`Title2`/`Title3`/`Name`), so the
  same file works on every computer of the project:
  * the **active** drawing is used when it is a wall layout drawing;
  * otherwise the wall layout drawing of the model is looked up and
    **activated**;
  * a drawing that is **issued or frozen is never written to** — the
    macro stops and says so. This is what keeps it away from [50], the
    issued reference drawing, which is frozen.
* **R9 — the automatic panel detection.** `WfRules.BuildPanelsAuto` finds
  the wall panels of a model without knowing the panel prefix: a wall
  assembly is a group of parts with a bottom track, a top track, a stud
  and a gypsum board, and no ceiling part. The macro falls back to it
  when the configured prefix (`BP4-WP`) matches nothing, and the log
  says so. Both ways find the same 11 panels in this model.
* **The deployment packages** `package/WallFramingAutoDimension.zip`
  (the free edition) and `package/WallFramingAutoDimensionPro.zip`:
  the loader, the icon, the modules, the sources, the build script, an
  installer and the documentation. `install.ps1` finds the Tekla
  installation of the target computer by itself, copies the loader and
  the icon into the macro catalog and the modules into
  `%LOCALAPPDATA%\P142\WallFraming\`.
* `source/build.ps1` grew a `-Package` switch that writes the package,
  and it now **finds the Tekla installation and the Visual Studio
  compiler by itself** (`-TeklaPath` and `-CscPath` override the search).
  The path is no longer hard coded, so the same command works on every
  computer.  A `-ProPath` switch points the build at the **private** Pro
  sources, which are not in this repository.
* `tests/test_target_selection.py` — proves that the macro works on the
  wall layout drawing that is open, that it activates it when another
  drawing is open, and that it refuses an issued drawing and leaves it
  unchanged.
* `tests/test_rules.py` also checks the automatic panel detection, and
  the Pro chains when the Pro rules have been built.
* **R6 — the north run.** `WfRules.NorthRun` returns the run of the
  northern wall measured to the end of the layout
  (1799.32 – 3110.82 – 3187.32), with the end of the wall as the middle
  station. The macro draws it 30 mm north of the wall it measures, so
  that it does not land on the top outline the way it does in the
  reference drawing. **12 of the 18 chains of [50] are now reproduced
  exactly** (was 11).
* The **overall height and width** moved from the macro into the rules
  assembly (`WfRules.Overall`), so that every rule of the drawing lives
  in the rules and the macro only writes what it is told. The rules
  harness now covers 12 of the 18 sets of [50], the same as the drawing.
* `tests/dump_openings.py` and `tests/dump_panel_parts.py` — the
  per-panel opening stations and the raw parts of one panel, used to
  work out why the chain of `BP4-WP8` differs from the one in [50].
* `tests/asm_versions.py` — prints the version of every Tekla assembly
  the macro binds to.
* The documentation set: `README.md`, `ARCHITECTURE.md`, `API_NOTES.md`,
  `LICENSING.md`, `SETUP.md`, `TESTING.md`, `TODO.md`, `CHANGELOG.md` and
  the rewritten `WALL_FRAMING_RULES.md`.

### Changed

* **The macro is no longer one merged `.cs` file.** The rules, the
  licence engine and the drawing machinery are separate assemblies, and
  the macro catalog gets a thin loader.  The installation is two files in
  the macro catalog and a few modules in `%LOCALAPPDATA%`; there is
  nothing to register.
* `source/build.ps1` grew a `-Deploy` switch that installs the macro
  (loader and icon) into the macro catalog of the Tekla installation it
  found, and the modules into the app folder.
* The log file of the macro is written next to the macro DLL (or in the
  temporary folder) instead of into a hard-coded path, and its path is
  shown in the message box.
* **The macro saves the drawing itself.** `DrawingHandler.SaveActiveDrawing()`
  is called at the end of a run from the ribbon, so the objects do not
  disappear any more (API trap 1).
* The section marks are copied from the **reference drawing object** now,
  not from a mark string: the reference is looked up by the preferred
  mark first and by its title otherwise, and it is never written to.
* The harnesses find the reference dumps through the **`WF_REFERENCE`**
  environment variable when they are not in `tests/reference/`, and say
  clearly when they are missing.

### Fixed

* **The trial could never start.** On the very first run the state holds
  the "first" stamp but not yet the "last" one, and the clock-rollback
  check then subtracted an hour from `DateTime.MinValue`, which throws.
  The macro fell back to the free edition on every run, so the three day
  trial never worked.  The check now compares against the first stamp
  when there is no last one.  Two checks were added to the harness.
* **The two packages were written over each other.** A loop variable in
  `build.ps1` shadowed the `$Name` parameter of the packaging function,
  so both zips were called `INSTALL_NOTES.txt.zip` and the Pro one
  replaced the free one.  Both packages are written now, with the right
  names.
* The macro no longer depends on the mark **[57]**: it works on whatever
  wall layout drawing is open, on any computer.
* **The old merged source is removed from the build output.** Before the
  two editions the build also wrote `P142_WF_AutoDimension_merged.cs` —
  the macro and the rules in one file, so that the catalog could compile
  it without a second DLL.  That file carried the Pro stages in plain
  text.  The loader replaced it, but the leftover stayed in
  `source\bin\` where somebody could have picked it up and installed it.
  The build now deletes the three merged artefacts before it starts, and
  says so.

## 2026-10-22

### Added

* **R4 — the top outline of the layout.** `WfRules.TopOutlinePolyline`
  replaces the old "upper envelope": the profile of the layout is cut
  into intervals, the level of every interval is the highest panel that
  covers it, a wall that runs along Y is **chamfered** over (a
  re-entrant corner) and a narrow notch is **bridged** when the level is
  the same on both sides of it. The polyline of [50] is reproduced
  exactly.
* **R5 — the overall dimensions.** The overall height of the layout
  (on its west edge) and the overall width (between the two ends of the
  top outline, the diagonal of [50]).
* The north run and the top outline replaced the old "top band" chain.
* `tests/trace_outline.py` — a step-by-step replay of the outline rule,
  written to find out why a same-level gap was not skipped.

### Fixed

* The dimension line is no longer moved onto the geometry it measures:
  changing the style of a dimension set resets its position, so the
  `Distance` is written again after the style change (API trap 7).
* A gap in the profile is now handled as a general **dip** (a group of
  runs whose level drops and comes back), not only as an empty gap. The
  two notches of this model are dips to a lower level, and the rule now
  tells them apart: the one across which the level changes is measured
  on both edges, the one with the same level on both sides is bridged.

## 2026-10-21

### Added

* The first working version of the macro: it reads the wall parts of the
  model, groups them into panels by `ASSEMBLY_POS`, and writes the
  dimension sets, the 11 wall-panel marks, the 5 section marks and the
  3 notes into the active drawing.
* **R1/R2 — the bay chain of a panel** and the opening stations.
  11 of the 11 panel chains of [50] are reproduced exactly.
* **R3 — the band chains** of the bottom, left and right edges of the
  layout.
* `WF_Rules.cs` as a **separate assembly with no Tekla reference**, so
  that the rules can be tested without a running Tekla Structures.
* `tests/test_rules.py` (the rules against the oracle of [50]),
  `tests/cmp_chains.py` ([50] against the generated drawing),
  `tests/test_marks.py` (the mark placement), `tests/audit_50_57.py`
  (the state of both drawings).
* The marks: the text angle (11 of 11), the leader line (9 of 11) and
  the label offset (9 of 11) are derived from the model.
* The section marks are copied from the reference drawing — they are
  hand placed and cannot be derived.
* The output is tagged with a user property, so a re-run replaces it
  instead of duplicating it.

### Fixed

* The objects now survive: `Drawing.CommitChanges()` alone is not
  enough, the drawing has to be saved (API trap 1).
* The text angle of a mark is set through `RotationAngle`, the only
  public lever (API trap 2).
