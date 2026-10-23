# Changelog

All notable changes to the wall-framing auto-dimensioner.

The format follows [Keep a Changelog](https://keepachangelog.com/), and
the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

* **The AIO macro** `WallFramingAutoDimensionPro-AIO` — a **second**
  macro, next to the licensed one, that dimensions **every wall framing
  layout of a GA sheet on its own**.

  A GA sheet does not always carry one wall framing layout: the sheet
  **[176]** of the client carries the layout of **19 blocks** side by
  side.  The macro of R8 takes the box of all the panels of the sheet,
  so the top and the bottom chain of the sheet crossed every block,
  while the left and the right chain of each block was right.

  The AIO macro groups the panels of the sheet into their **blocks** —
  the part of the assembly position before the first dash, `BP1-WP3` →
  `BP1` — and dimensions every group with the box of its own panels:
  its band chains, its top outline, its overall dimensions, its north
  run, its wall-panel marks, its section marks and its notes.  The chain
  of one block never runs into the next one.

  * the **AIO macro asks for no licence key**: it reads no licence at
    all, and it draws the **whole** set of annotations — the free stages
    and, when the Pro module is installed, the Pro stages;
  * the rules of the grouping are in the **free** rules
    (`WfRules.BlockOf`, `WfRules.NormaliseBlock`, `WfRules.SameBlock`,
    `WfRules.LayoutGroups`), so they are tested without a running
    Tekla — `tests/test_layout_groups.py`, 26 checks;
  * the version of the free rules is now **3**; the Pro module is built
    against the same version, and the version handshake refuses a
    mixed set of modules;
  * the section marks of the sheet are **kept** (R10.5): they belong to
    the layouts, and the ones of the client carry the tag of the macro
    as well.  The AIO macro copies the section marks of the drawing of
    the block only into a layout that has none of its own.
* **The AIO loader** `source/WallFramingAutoDimensionPro-AIO.cs` — the
  thin file that goes into the macro catalog, next to the loader of the
  licensed macro.  Like the other loader it holds no rule and no drawing
  logic: it loads `WF_Core.dll` and calls the entry point `ExecuteAio`.
* **The package of the AIO macro** — `build.ps1 -AioPackage` writes
  `package/WallFramingAutoDimensionAIO.zip`.  The AIO macro is **free**
  and it asks for **no key**, and it draws the whole set of annotations —
  which it can only do with the Pro modules.  The package therefore
  carries them, like the Pro package, and it is **not published** (rule
  R10): it is built in the dev tree and handed to a customer directly.
  `-AioFree` is the other switch: it puts the Pro modules into the
  **published** free package as well, and that one is the owner's
  commercial decision.
* **The study of the two other drawing kinds** — the **sectional
  elevation** and the **isometric drawing** —
  `docs/SECTION-ELEVATION-AND-ISOMETRIC.md`.  It is the measured ground
  for the two next macros: what a sectional elevation holds (a
  `SectionView` per section line, with the material notes, the level
  marks, the dimension chains and the marks of the assemblies the
  section cuts) and what an isometric drawing holds (three `ModelView`s
  with the marks of the parts and the weld marks, and nothing else), the
  API of Tekla 2025 that is needed to write them
  (`View.CreateSectionView`, `View.Create3dView`), the note catalogue of
  the client, the seven traps and the plan of the seven stages.

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
* **Support for a model with many wall layout drawings.** The rules were
  checked against a second model, `MODEL-2` (4887 parts, 234 wall
  panels, 19 writable wall layout drawings), and the macro was changed
  so that it works on it:
  * **R8.2 — every wall layout drawing of the model is worked on**, one
    after the other, when the drawing that is open is not one of them.
    The macro switches the active drawing itself, because the drawing
    API only writes into the drawing that is open.
  * **R8.5 — the panels of a drawing are the assemblies the drawing
    shows.**  A GA drawing shows the assemblies of its own block and of
    no other, so the macro cannot dimension a wall that is not on the
    drawing it is dimensioning.  The join is made on the model side
    (`Part.GetAssembly()`), because the drawing API has no
    `AssemblyIdentifier`.
  * **R9.3 — the parts are grouped by the id of the assembly**, not by
    `ASSEMBLY_POS`: two different assemblies can carry the same position,
    and a drawing that shows one of them must not dimension the other.
  * **R9.4 — a ledge wall** (a wall built with more than one
    `BOTTOM_TRACK`) runs from the start of the first track to the end of
    the last one, instead of the extent of one track.
  * **R2.2 — an opening without a door frame** (a gap in the wall, where
    the jambs have no touching partner) is measured at the **centre** of
    its members: the jambs are ordinary members of the wall and so is the
    mullion between them.  The old model has no opening of this kind, so
    nothing there changed.
  * The `ASSEMBLY_POS` is read from the **assembly** and not from the
    part, because that model leaves the position off its parts; the
    position of the part is used when the assembly has none.
* **`tests/test_upgrade.py`** — the rules against a model with many wall
  layout drawings.  It **reads only**: it never writes into the model or
  into a drawing.  It reports how many of the chains of the macro are a
  subset of a hand-made chain, how many are the same, and it lists every
  station the macro measures that the drawing does not.  Current result
  on `MODEL-2`: 205 of 291 chains are a subset (70 %), 132 are the
  same, 0 failures.
* **The target selection was measured on the live model.**  Of the 695
  drawings of `MODEL-2`, 21 match the wall-layout title rule; two of
  them ([50] and [72]) are frozen and are refused, and **19** are the
  targets.  No `WALL_PANEL`, no `LEDGE WALL` and no per-block drawing is
  ever picked up.
* **A version handshake between the core and the Pro module.**  The Pro
  module (`WF_Pro.dll`) is a separate assembly that is built against the
  rules assembly (`WF_Rules.dll`) and is loaded by reflection at run
  time.  When the two are of different versions the module calls members
  of the rules that no longer exist, and **every** one of its calls fails
  with a bare `Method not found` — which says nothing about the cause.
  A mixed installation therefore looks like a broken macro.  So:
  * the rules carry `WfRules.ApiVersion`, and the Pro module carries the
    version it was built for as `WfPro.RulesApiVersion` — a constant, so
    it cannot drift away from the rules by accident;
  * the core reads both numbers **at run time** (a constant is fixed
    when the core is compiled, so it would report the version the rules
    had at build time, not the one that is on the disk) and refuses to
    call a module of a different version.  The message says what to do:
    *install the modules again*.
  * `tests/test_version_handshake.py` checks the comparison, and
    `tests/test_loader.py` now checks the **installed** modules against
    each other, so a mixed installation is found before it is run.
* **The Pro module is looked for NEXT TO THE CORE** and only then in the
  app folder.  The modules belong together: a build that is tested from
  its own folder cannot pick up the module of an older installation by
  accident.
* **The Pro rules were run against the new model.**  Every Pro rule —
  the top outline, its stations, the north run and the placement of the
  wall panel marks — was run for every one of the 19 writable wall
  layout drawings of `MODEL-2`: **19 of 19, no failures**, and
  **234** wall panel marks placed.  That is the same 234 that failed
  with `get_A0()` before — so the rebuild really did fix them all.  The
  Pro rules are pure computation, so they can be checked against a live
  model without drawing anything.
* **The client's own work was checked to be safe from the cleanup.**
  The macro deletes the objects of a previous run of *itself* before it
  writes, and it recognises them by a user property it sets on
  everything it creates.  The 19 writable wall layout drawings of
  `MODEL-2` hold between them 502 dimension sets, 28 section
  marks, 69 texts and 2142 marks — **not one of them carries the
  macro's tag**, so the cleanup will never touch them.

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
* **The model is read once for a whole run.** Every drawing of a model
  shares its plan coordinate system, so the read is skipped when the
  coordinate system has not changed.  The read was repeated for every
  drawing before.
* **Each drawing is saved as soon as it is finished**, so that a failure
  in a later drawing does not lose the work of an earlier one.
* **The deployment and the Pro package carry `WF_Rules_Pro.dll`** as
  well.  The installed set is then the five modules that belong
  together — a mixed set is what the version handshake now reports.
* The macro can be run **without saving**, for a test that wants to see
  what it would draw and then leave the drawing as it was
  (`WfCore.Execute(..., save: false)`).  The hand-made drawings of a
  project are the reference the macro is measured against, and they must
  stay as they are.

### Fixed

* **The section marks were copied from the block NEXT DOOR.**  The Pro
  module copies the hand-placed section lines of a reference drawing, and
  the reference was looked for among the hand-made drawings of the model
  that carry section marks.  The **first** one was taken, and in a model
  of many blocks that is the drawing of another block: in
  `MODEL-2` the five lines of `[72]` (the drawing of the **BP1 M/U**
  block) were put into the drawing of **BP07M**, whose
  walls are at X 72012 … 74788 — the lines landed about 68 m to the left
  of the layout, on the side of the sheet.  The reference is now the
  hand-made drawing of the **same block** (`[87] BP07M-10001` for
  `BP07M`), and its lines are checked against the layout of the drawing
  that is being dimensioned before they are used.
* **The test of a section line is a RULE now.**  It is
  `WfRulesPro.SectionLinesInLayout` in the Pro rules
  (`WF_Rules_Pro.dll`, no Tekla reference), it is tested without Tekla by
  the new `tests/test_section_lines.py`, and the Pro rules carry a
  version number of their own: a Pro module and a Pro rules assembly of
  different builds are now refused, instead of failing on a member that
  is not there.
* **The section marks were looked for in the WRONG drawing.**  The Pro
  module copies the hand-placed section lines of a reference drawing, and
  the reference was looked for among the wall layout drawings of the
  model.  In a model with many blocks that is wrong: every block has its
  own wall layout drawing, and the section lines of one block would be
  put into the drawing of another one.  The reference is now a **hand-made
  drawing** — issued or frozen — and the preferred mark is used first.
* **The section marks were counted in the FIRST view only.**  The first
  view of a drawing is not always the plan view: two of the drawings of
  `MODEL-2` have an unnamed container view in front of it, and
  their section marks were therefore counted as **zero**.  Every count
  over the views of a drawing now walks all of them.
* **The Pro module failed on every object of a mixed installation.**
  The deployed `WF_Pro.dll` was built against an older rules assembly,
  in which `WfPanel.A0` was a property; the new rules made it a field,
  and the old module then failed with
  `Method not found: 'Double P142.WallFraming.WfPanel.get_A0()'` — 234
  times, once per wall panel mark.  The version handshake reports this
  as a deployment problem instead of as 234 unrelated failures.
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
* **`tests/test_loader.py` could not read the version of the installed
  rules.**  It looked for the rules type in the **core** assembly, but
  the type lives in `WF_Rules.dll`, so the lookup returned nothing and
  the harness reported *"the rules assembly carries no version number"*
  whatever was installed.  The macro itself was never affected — it
  reads the version of the assembly its own types come from.  The
  harness now reads `WF_Rules.dll` from the folder of the core, which
  is where the runtime takes it from.

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
