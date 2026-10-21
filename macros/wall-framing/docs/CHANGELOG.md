# Changelog

All notable changes to the wall-framing auto-dimensioner.

The format follows [Keep a Changelog](https://keepachangelog.com/), and
the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

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
* **The deployment package** `package/WallFramingAutoDimension.zip`:
  the merged single-file source, the icon, both sources, the build
  script, an installer and the documentation. `install.ps1` finds
  the Tekla installation of the target computer by itself and copies the
  macro into the macro catalog.
* `source/build.ps1` grew a `-Package` switch that writes the package,
  and it now **finds the Tekla installation and the Visual Studio
  compiler by itself** (`-TeklaPath` and `-CscPath` override the search).
  The path is no longer hard coded, so the same command works on every
  computer.
* `tests/test_target_selection.py` — proves that the macro works on the
  wall layout drawing that is open, that it activates it when another
  drawing is open, and that it refuses an issued drawing and leaves it
  unchanged.
* `tests/test_rules.py` also checks the automatic panel detection.
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
  `SETUP.md`, `TESTING.md`, `TODO.md`, `CHANGELOG.md` and the rewritten
  `WALL_FRAMING_RULES.md`.

### Changed

* `source/build.ps1` generates `source/bin/P142_WF_AutoDimension_merged.cs`
  — the rules and the macro in one file, so that the Tekla macro catalog
  can compile the macro without a second DLL — and compiles that merged
  file as well, so a broken merge can never reach the catalog.
* `source/build.ps1` grew a `-Deploy` switch that installs the macro
  (source and icon) into the macro catalog of the Tekla installation it
  found.
* The log file of the macro is written next to the macro DLL (or in the
  temporary folder) instead of into a hard-coded path, and its path is
  shown in the message box.
* **The macro saves the drawing itself.** `DrawingHandler.SaveActiveDrawing()`
  is called at the end of a run from the ribbon, so the objects do not
  disappear any more (API trap 1).
* The section marks are copied from the **reference drawing object** now,
  not from a mark string: the reference is looked up by the preferred
  mark first and by its title otherwise, and it is never written to.

### Fixed

* The macro no longer depends on the mark **[57]**: it works on whatever
  wall layout drawing is open, on any computer.

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
