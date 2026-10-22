# Architecture

## The four modules

```
        +--------------------------------------------------+
        |  Tekla Structures 2025  (the host process)       |
        +--------------------------------------------------+
             ^                                  |
             | reads the model,                 | writes drawing objects
             | writes drawing objects          v
        +--------------------------------------------------+
        |  P142_WF_AutoDimension.cs   (the LOADER)          |
        |  - shows the message box and the log path         |
        |  - loads WF_Core.dll by reflection                |
        |  - hands the work over and saves the drawing      |
        +--------------------------------------------------+
             ^                                  |
             |                          P142.WallFraming.WfCore.Execute
             |                                  |
        +--------------------------------------------------+
        |  WF_Core.cs   (the shared machinery)              |
        |  - resolves the target drawings                    |
        |  - finds the plan view                             |
        |  - reads the wall parts out of the model          |
        |  - asks the rules where every annotation goes      |
        |  - creates the dimension sets / marks / texts      |
        |  - asks the licence which edition may be used      |
        +--------------------------------------------------+
             ^                     ^                  |
             |                     |                  | only when the
             | pure geometry,      | the licence      | licence says Pro
             | no Tekla types,     | engine           v
             | no I/O, no state    |            +-------------------------+
        +------------------------+            |  WF_Pro.dll  (the Pro)   |
        |  WF_Rules.cs          |            |  - the top outline        |
        |  BuildPanels,         |            |  - the overall dimensions |
        |  BuildPanelsAuto,     |            |  - the north run          |
        |  ChainStations,       |            |  - the wall-panel marks   |
        |  OpeningStations,     |            |  - the section marks      |
        |  BandStations         |            |  - the notes              |
        +------------------------+            +-------------------------+
             ^
             | and, in the private tree only:
        +---------------------------------------------------------------+
        |  WF_Rules_Pro.cs  - the Pro rules: TopOutlinePolyline,        |
        |  NorthRun, Overall, MarkPlacement                              |
        +---------------------------------------------------------------+

        +---------------------------------------------------------------+
        |  WF_License.cs  - the licence engine: the key, the trial,      |
        |  the machine ids.  No Tekla reference.                          |
        +---------------------------------------------------------------+
```

**The dependency rule is one-way: the rules must never know about
Tekla.** `WF_Rules.cs` and `WF_License.cs` compile against nothing but the
.NET framework, which is what makes them testable without a running Tekla
Structures (`tests/test_rules.py` and `tests/test_license.py` load the DLLs
with pythonnet and check the output against the reference drawing).

The macro calls the rules and does nothing else with geometry: it never
computes a station, a level or a mark position itself.

## The editions and where they are cut

The two editions are cut **at the assembly boundary**, not with `if` inside
one file:

| | Free | Pro |
|---|---|---|
| the rules | `WF_Rules.dll` | `WF_Rules.dll` + `WF_Rules_Pro.dll` |
| the stages | `WF_Core.dll` | `WF_Core.dll` + `WF_Pro.dll` |

`WF_Core.Execute` ends with

```csharp
if (edition == 1) RunPro(result);
```

`RunPro` loads `WF_Pro.dll` by reflection out of the app folder.  The core
does not reference it, so the free edition works without it, and a Pro
licence on a computer that has no `WF_Pro.dll` still runs — it simply does
not get the Pro annotations, and the log says so.

The **Pro sources are not in the published repository**.  They live in the
owner's private tree, and `build.ps1 -ProPath` points the build at them.

## The loader

The file that the Tekla macro catalog compiles is a **thin loader**.  It
holds no rule and no drawing logic; it loads `WF_Core.dll` and hands the
work over.

That is not a style choice — it is what makes the Pro stages possible to
withhold.  A single merged file would have to hold everything, the free
stages and the Pro stages together, and the "free" package would then
carry the Pro rules in plain text.

The loader finds the modules in `%LOCALAPPDATA%\P142\WallFraming\`.  It
loads them with `Assembly.LoadFrom`, so the dependencies of the modules are
resolved from the folder of the module itself.

## The target selection

The macro does **not** work on a fixed drawing mark.  A mark is a number
of one project; the wall layout drawing of another computer has another
mark, and there may be several of them.  The macro therefore recognises a
drawing by its **title**:

| step | what the macro does |
|---|---|
| 1 | the **active** drawing is used when its title holds `WALL FRAMING`, `WALL LAYOUT` or `WALL FRAMING LAYOUT` |
| 2 | otherwise **every** wall layout drawing of the model that may be written to is worked on, one after the other |
| 3 | a drawing that is **issued or frozen is never written to** — the macro skips it and says so |

Step 3 is what protects the issued reference drawing of the project: it
is frozen, so the macro refuses it even when it is the active drawing.
`tests/test_target_selection.py` proves all three steps.

Step 2 is what a model with many blocks needs: `MODEL-2` has 20
wall layout drawings, one per block, and the blocks are far apart in the
model.  Working on "the wall layout drawing of the model" is not a
thing; the macro works on all of them.

The panel drawings of the model are named `WALL_PANEL` and the other
layout drawings are named `BASE FRAMING LAYOUT` and `CEILING FRAMING
LAYOUT`; none of them matches the wall layout fragments, so the macro
never touches them.

The **reference** drawing — the one the section marks are copied from — is
looked up by the preferred mark first (a constant), and by its title
otherwise.  It is only ever read.

## The panels of a drawing

A GA drawing shows the assemblies of **its own block** and of no other.
The macro therefore does not ask "which panels does the model have" but
"which panels does **this drawing** show":

```
model  ->  Part.GetAssembly()  ->  the id of the assembly of every part
drawing ->  the model objects it shows  ->  the id of their model parts
                                       ->  the ids of their assemblies
```

The join is `WfCore.ShownAssemblies(view)`, and the result is the key set
that `WfRules.BuildPanelsForAssemblies` keeps.

The drawing API has **no** `AssemblyIdentifier` — the part → assembly map
can only be built on the model side, and the drawing gives the id of the
*part*.  That is why the model is read before the panels are chosen.

When the drawing shows no wall assembly at all — an empty view, or a
model whose assemblies are not shown in the drawing — the panels are found
the old way, by the name of the assembly and then by their construction,
and the log says that it fell back.


## The data flow of one run

1. **Ask the licence.** `WfCore.LicenceEdition()` works out whether this
   computer may use the Pro edition, and records the run (the trial stamps).
2. **Resolve the targets.** The title rule above; a frozen or issued
   drawing is skipped, and the run goes on with the next one.
3. **Find the view.** The macro walks the views of the sheet and takes
   the first one whose display coordinate system looks straight down
   (`|AxisX.Z| < 1e-3` and `|AxisY.Z| < 1e-3`).
4. **Clean up.** Every object tagged `P142_WF_AUTODIM = 1` in that view
   is deleted, so a re-run is idempotent.
5. **Read the model.** The wall parts (`STUD`, `BOTTOM_TRACK`,
   `TOP_TRACK`, `MIDDLE_TRACK`, `GYPSUM_BOARD`, `JAMB`, `LINTEL`, `SILL`)
   are read from the model and converted to `WfPart` objects with their
   plan-view extents, in the **display coordinate system of the view**.
   The `ASSEMBLY_POS` is read from the **assembly** the part belongs to
   (`Part.GetAssembly()`), because a model may leave it off its parts.
   The model is read **once** for a whole run: every drawing of a model
   shares its plan coordinate system, and the read is skipped when the
   coordinate system has not changed.
6. **Choose the panels.** The assemblies the **drawing shows** are read
   out of the view, joined with the part → assembly map of step 5, and
   `WfRules.BuildPanelsForAssemblies` keeps only those assemblies.  When
   the drawing shows no wall assembly, the panels are found by the name of
   the assembly (`WfRules.BuildPanels`, prefix `BP4-WP`) and then by
   their construction (`WfRules.BuildPanelsAuto`), which recognises a
   wall assembly by its parts (a bottom track, a top track, a stud and a
   gypsum board) and leaves the ceiling panels alone.
7. **Ask.** For every annotation the macro calls a rules method and gets
   back a list of numbers — never a Tekla object.
8. **Write.** The macro turns the numbers into `StraightDimensionSet`s,
   `Mark`s and `Text`s, applies the style copied from the reference
   drawing, and tags every object it created.
9. **The Pro stages** — the top outline, the overall dimensions, the north
   run, the wall-panel marks, the section marks and the notes — are drawn
   by the Pro module, and only when the licence allows it.
10. **Save.** `Drawing.CommitChanges()` after every step, and
    `DrawingHandler.SaveActiveDrawing()` at the end — without the latter
    the objects vanish when the drawing is re-read (see API_NOTES, trap 1).
    Each drawing is saved as soon as it is finished, so that a failure
    in the next drawing does not lose the work of the previous one.

The **whole of steps 2 to 10 runs once per drawing**.  The macro switches
the active drawing with `DrawingHandler.SetActiveDrawing`, because the
drawing API only writes into the drawing that is open.


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
only in the wall layout drawings it has resolved. It never
modifies the model, never modifies another drawing and never changes a
view. The one destructive step is the clean-up, and it is limited to
objects that carry the macro's own user property. An issued or frozen
drawing is skipped before anything is written to it.

The macro can now write to **several** drawings in one run — one per
block of the model.  The set is decided by the title rule, and every
drawing in it is checked for the issued/frozen state before it is
touched.

The operation log is written to
`<folder of the macro DLL>/wfdim_log_<mark>.txt` and is also returned in
`Result.Log`.

## Deployment

The deployment is **two things**:

```
<Tekla>\Environments\common\macros\drawings\
    P142_WF_AutoDimension.cs      the loader
    P142_WF_AutoDimension.bmp      the icon

%LOCALAPPDATA%\P142\WallFraming\
    WF_Core.dll  WF_Rules.dll  WF_License.dll  [WF_Pro.dll]
    license.txt  state.txt
```

`install.ps1` finds the Tekla installation of the target computer by
itself and copies both.  There is nothing to register and no installer to
run: the modules are plain assemblies that the loader picks up at run
time.

The package (`package/WallFramingAutoDimension.zip`) holds the loader, the
icon, the modules, the sources, the build script, the documentation and
the installer.  The Pro package adds `WF_Pro.dll`.

## The licence

The edition is decided **before** anything is drawn, by
`WfCore.LicenceEdition()`, which reads the key from
`%LOCALAPPDATA%\P142\WallFraming\license.txt` and asks
`WF_License.EvaluateAndRecord` what this computer may use.  The whole
workflow — the trial, the machine binding, the owner's key tool — is in
[LICENSING.md](LICENSING.md).

The licence engine has no Tekla reference either, so it is tested on its
own: `tests/test_license.py` runs 42 checks without a running Tekla.

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
9. **The two editions.** The Pro stages are cut out into their own
   module, the licence engine decides what a computer may use, and the
   Pro rules move to a separate Tekla-free assembly so that they stay
   testable. *Done* — see [LICENSING.md](LICENSING.md).

## The honest caveat

The **Pro path has never been run against a live Tekla Structures.**  The
rules were moved into `WF_Rules_Pro.dll` and the stages into `WF_Pro.dll`
after the drawing was finished, and the two harnesses prove that the move
changed no answer:

* `tests/test_rules.py` — 12 of 18 chains, the same as before the move;
* `tests/test_marks.py` — angle 11/11, leader 9/11, offset 9/11, the same
  as before the move.

What has **not** been proven is that the Pro module writes the same
objects in the same places once it is loaded by the core.  That needs one
run of the macro against a live Tekla with a Pro licence.  It is the first
item of [TODO.md](TODO.md).
