# Architecture

## The three layers

```
        +--------------------------------------------------+
        |  Tekla Structures 2025  (the host process)       |
        +--------------------------------------------------+
             ^                                  |
             | reads the model,                 | writes drawing objects
             | writes drawing objects          v
        +--------------------------------------------------+
        |  P142_WF_AutoDimension.cs   (the adapter)         |
        |  - resolves the target drawing                     |
        |  - finds the plan view                              |
        |  - reads the wall parts out of the model           |
        |  - turns them into WfPart objects                   |
        |  - asks the rules where every annotation goes       |
        |  - creates the dimension sets / marks / texts       |
        +--------------------------------------------------+
             ^                                  |
             | pure geometry, no Tekla types,         |
             | no I/O, no state                       |
             v                                  v
        +--------------------------------------------------+
        |  WF_Rules.cs   (the rules)                        |
        |  BuildPanels, BuildPanelsAuto, ChainStations,     |
        |  OpeningStations, BandStations,                   |
        |  TopOutlinePolyline, NorthRun, Overall,           |
        |  MarkPlacement                                    |
        +--------------------------------------------------+
```

**The dependency rule is one-way: the rules must never know about
Tekla.** `WF_Rules.cs` compiles against nothing but the .NET framework,
which is what makes it testable without a running Tekla Structures
(`tests/test_rules.py` loads the DLL with pythonnet and checks the
output against the reference drawing).

The macro calls the rules and does nothing else with geometry: it never
computes a station, a level or a mark position itself.

## The target selection

The macro does **not** work on a fixed drawing mark.  A mark is a number
of one project; the wall layout drawing of another computer has another
mark, and there may be several of them.  The macro therefore recognises a
drawing by its **title**:

| step | what the macro does |
|---|---|
| 1 | the **active** drawing is used when its title holds `WALL FRAMING`, `WALL LAYOUT` or `WALL FRAMING LAYOUT` |
| 2 | otherwise the wall layout drawing of the model is looked up and **activated** |
| 3 | a drawing that is **issued or frozen is never written to** — the macro stops with a message |

Step 3 is what protects the issued reference drawing of the project: it
is frozen, so the macro refuses it even when it is the active drawing.
`tests/test_target_selection.py` proves all three steps.

The panel drawings of the model are named `WALL_PANEL` and the other
layout drawings are named `BASE FRAMING LAYOUT` and `CEILING FRAMING
LAYOUT`; none of them matches the wall layout fragments, so the macro
never touches them.

The **reference** drawing — the one the section marks are copied from — is
looked up by the preferred mark first (a constant), and by its title
otherwise.  It is only ever read.

## The data flow of one run

1. **Resolve the target.** The title rule above; a frozen or issued
   drawing ends the run.
2. **Find the view.** The macro walks the views of the sheet and takes
   the first one whose display coordinate system looks straight down
   (`|AxisX.Z| < 1e-3` and `|AxisY.Z| < 1e-3`).
3. **Clean up.** Every object tagged `P142_WF_AUTODIM = 1` in that view
   is deleted, so a re-run is idempotent.
4. **Read the model.** The wall parts (`STUD`, `BOTTOM_TRACK`,
   `TOP_TRACK`, `MIDDLE_TRACK`, `GYPSUM_BOARD`, `JAMB`, `LINTEL`, `SILL`)
   are read from the model and converted to `WfPart` objects with their
   plan-view extents, in the **display coordinate system of the view**.
5. **Group.** `WfRules.BuildPanels` groups the parts into wall panels by
   the model report property `ASSEMBLY_POS` (prefix `BP4-WP`) and
   computes the assembly extents of every panel.  When that prefix
   matches nothing — another computer, another project — the panels are
   found automatically by `WfRules.BuildPanelsAuto`, which recognises a
   wall assembly by its parts (a bottom track, a top track, a stud and a
   gypsum board) and leaves the ceiling panels alone.
6. **Ask.** For every annotation the macro calls a rules method and gets
   back a list of numbers — never a Tekla object.
7. **Write.** The macro turns the numbers into `StraightDimensionSet`s,
   `Mark`s and `Text`s, applies the style copied from the reference
   drawing, and tags every object it created.
8. **Save.** `Drawing.CommitChanges()` after every step, and
   `DrawingHandler.SaveActiveDrawing()` at the end — without the latter
   the objects vanish when the drawing is re-read (see API_NOTES, trap 1).

## Coordinate systems

Everything the macro writes is in the **model** coordinate system, and
the drawing engine maps it to the view. The only place a transformation
happens is the read step, where the part extents are taken in the view's
display coordinate system — and both the rules and the writer use that
same system, so the chain that is measured and the chain that is drawn
are the same numbers.

The one unit that is converted is the **paper distance** of a dimension
line: the rules and the constants work in paper millimetres, the API
wants model millimetres, so every distance is multiplied by
`view.Attributes.Scale` (17 for this model).

## Risk level

**MEDIUM.** The macro only creates and deletes *drawing* objects, and
only in the drawing it has resolved as the wall layout drawing. It never
modifies the model, never modifies another drawing and never changes a
view. The one destructive step is the clean-up, and it is limited to
objects that carry the macro's own user property. An issued or frozen
drawing ends the run before anything is written.

The operation log is written to
`<folder of the macro DLL>/wfdim_log_<mark>.txt` and is also returned in
`Result.Log`.

## Deployment

The macro is deployed as **one `.cs` file** in the macro catalog of
Tekla.  The rules and the macro are merged into that file by
`build.ps1` (the rules keep their own namespace, the `using` directives
of the rules file are stripped because a compilation unit may only declare
`using` before its first type).  The merged file is compiled a second
time by the build script, so a broken merge can never reach the catalog.

The package (`package/WallFramingAutoDimension.zip`) holds that file, the
icon, the sources, the build script and an installer that finds the Tekla
installation of the target computer by itself.  The build script finds
the Tekla installation and the Visual Studio compiler by itself too, so
the same commands work on every computer.

## What is *not* in this architecture

* **No plugin.** A Tekla plugin would need an `IPlugin` implementation
  and a plugin manifest; the macro is enough for the job and is much
  easier to deploy. The rules assembly is already a separate, testable
  unit, so it can be lifted into a plugin unchanged.
* **No configuration file.** The constants (paper distances, the notch
  width, the panel prefix, the reference drawing) live in the macro as
  named constants. When a second project needs them they should move
  into a settings file — the rules never read them, so nothing else has
  to change. The panel prefix is already only a preference: the automatic
  panel detection takes over when it matches nothing.
* **No rules for the hand-made parts of [50].** The section marks are
  copied, not derived. A rules engine that tried to derive them would
  be guessing.

## The MVP plan (8 steps)

The master instruction for this project asks for the plan first and the
code in phases. This is the plan the code actually followed:

1. **Connect and read.** A macro that reads the model and reports what
   it found. *Done* — `ReadWallParts`.
2. **Extract the model into plain data.** `WfPart` / `WfPanel`, with no
   Tekla types. *Done* — `WfRules.BuildPanels`.
3. **Make the rules testable without Tekla.** `WF_Rules.cs` as a
   separate assembly, loaded by pythonnet in `tests/test_rules.py`.
   *Done.*
4. **One rule, one reference set.** The bay chain of a panel, checked
   against [50] set by set. *Done* — 11 of 11 panel chains match.
5. **The layout-level rules.** Bands, the top outline, the overall
   dimensions, the north run. *Done* — 12 of 18 sets of [50] match.
6. **The annotation objects.** Dimension sets, marks, section marks,
   notes, with the style copied from [50]. *Done.*
7. **Make it repeatable.** Tag the output, delete it on a re-run, log
   every step. *Done.*
8. **Ship it.** Build script, deployment to the macro catalog, the
   documentation set, the deployment package for the other computers.
   *Done* — `build.ps1 -Package` writes the package, `install.ps1`
   installs it on the target computer.
