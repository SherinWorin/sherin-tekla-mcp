# Wall-framing layout auto-dimensioner

A drawing macro for **Tekla Structures 2025** that reproduces the annotations
of a **WALL FRAMING LAYOUT** GA drawing — the stud chains of every wall panel,
the band chains of the layout, the top outline, the overall dimensions, the
north run, the wall-panel marks, the section marks and the notes.

It was written for the P142 COREL REEF BP4 mockup, where the issued reference
drawing **[50]** "WALL FRAMING LAYOUT" was the model for the annotations, and
it was checked against a second model, **MODEL-2**, which has 19 wall
layout drawings instead of one. The macro does **not** work on a fixed drawing
number: it finds the wall layout drawing by its **title**, so the same file
works on every computer of a project.

| | |
|---|---|
| Tekla version | **2025.0** (`Tekla.Structures.*` v2025.0.0.0, .NET Framework 4.x) |
| language | C#, compiled with the Roslyn `csc.exe` of Visual Studio 2022 |
| install | a thin loader in the Tekla macro catalog + a few modules in `%LOCALAPPDATA%` — nothing to register |
| editions | **free** (the dimension chains) and **Pro** (everything) — see [docs/LICENSING.md](docs/LICENSING.md) |
| second macro | **`WallFramingAutoDimensionPro-AIO`** — free, no licence key: it dimensions every wall framing layout of a GA sheet on its own |
| risk | **MEDIUM** — it creates drawing objects; it never touches the model, and it refuses an issued or frozen drawing |

## The layout

```
macros/wall-framing/
  README.md                this file
  INSTALL_NOTES.txt        the step-by-step installation, in plain text
  README.txt               the same notes in brief, for the package
  install.ps1              the installer (optional - the manual method needs no script)
  source/
    WF_Rules.cs            the FREE annotation rules - NO Tekla reference
    WF_License.cs          the licence engine - NO Tekla reference
    WF_Core.cs             the shared drawing machinery
    WF_KeyGen.cs           the owner's licence key tool
    P142_WF_AutoDimension.cs  the LOADER: it holds no rule and no logic
    WallFramingAutoDimensionPro-AIO.cs
                             the LOADER of the second macro: it asks for
                             no licence and dimensions EVERY wall framing
                             layout of a GA sheet on its own
    build.ps1              builds the modules, the tool and the package
    bin/                   the build output (not in the repository)
  docs/
    README.md              the full description of the macro
    ARCHITECTURE.md        the layers, the data flow, the target selection
    API_NOTES.md           the Tekla 2025 API facts and the ten traps found
    LICENSING.md           the two editions, the trial and the key workflow
    SETUP.md               how to build, install and run it
    TESTING.md             the test harnesses
    TODO.md                what is left to do
    CHANGELOG.md           the history
    WALL_FRAMING_RULES.md  every rule, with the reference set it was derived from
  package/
    WallFramingAutoDimension.zip     the free package - one file for the other PC
    WallFramingAutoDimensionPro.zip  the Pro package
  tests/
    tekla_bootstrap.py     loads the Tekla assemblies for pythonnet
    test_rules.py          the rules against the reference  (no Tekla needed)
    test_license.py        the licence engine  (no Tekla needed)
    test_layout_groups.py  the rule of the wall framing layouts  (no Tekla needed)
    test_loader.py         the deployment: the modules load and the licence answers
    test_target_selection.py  which drawing the macro works on
    test_upgrade.py        the rules on a model with many wall layout drawings
    cmp_chains.py          the generated drawing against the reference
    test_marks.py          the mark placement
    audit_50_57.py         the state of both drawings
    run_macro.py           runs the macro and saves the drawing
    dump_*.py              the scripts that (re)create the reference dumps
    reference/             the reference dumps - see reference/README.md
```

The **hand-over pack** of the whole project is in
[HANDOVER/](../../HANDOVER/README.md) at the root of the repository: the
state of the work, the hard rules, the Tekla 2025 traps and the commands to
continue on another computer.

## Which drawing does it work on?

The **mark is never used** to recognise a drawing:

1. the **active** drawing is used when its title holds `WALL FRAMING`,
   `WALL LAYOUT` or `WALL FRAMING LAYOUT`;
2. otherwise **every** wall layout drawing of the model that may be written
   to is worked on, one after the other — a model has one wall layout
   drawing per block;
3. a drawing that is **issued or frozen is never written to** — the macro
   skips it and says so.

Step 3 is what protects the issued reference drawing of a project: it is
frozen, so the macro refuses it even when it is the active drawing.

The **wall panels of a drawing are the assemblies the drawing shows**: a GA
drawing shows the assemblies of its own block and of no other, so a block is
never dimensioned on the drawing of another block.

The **panel prefix** (`BP4-WP` in the P142 project) is only a preference as
well: when it matches nothing, the wall panels are found automatically from
the parts themselves.

See [WALL_FRAMING_RULES.md](docs/WALL_FRAMING_RULES.md) (R8 and R9).

## Build

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1
```

**Ubuntu (WSL)** — the build needs the .NET Framework compiler, so it has to
run on Windows; run it from the Command Prompt of the Windows side.

The build produces

* `source\bin\WF_Rules.dll` — the free rules, no Tekla reference;
* `source\bin\WF_License.dll` — the licence engine, no Tekla reference;
* `source\bin\WF_Core.dll` — the shared drawing machinery;
* `source\bin\WF_KeyGen.exe` — the owner's licence key tool;
* `source\bin\P142_WF_AutoDimension.dll` — the **loader**, the file the
  Tekla macro catalog compiles.  It holds no rule and no drawing logic: it
  loads `WF_Core.dll` and hands the work over.

The build script finds the Tekla installation and the C# compiler by itself;
`-TeklaPath` and `-CscPath` override the search.  The **Pro** stages are
not in this repository; `-ProPath` points the build at the private sources
and the Pro package is written as well.

## Install

The full procedure is in **[INSTALL_NOTES.txt](INSTALL_NOTES.txt)** — the
manual method (two files copied) and the optional script.

**Command Prompt (Windows)**

```
copy macros\wall-framing\source\bin\P142_WF_AutoDimension.cs "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
copy macros\wall-framing\source\bin\P142_WF_AutoDimension.bmp "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
```

where `%TEKLA%` is the Tekla installation, e.g. `C:\TeklaStructures\2025.0`.
The `.cs` file **must** be named `P142_WF_AutoDimension.cs` in the catalog:
the name of the file is the name of the macro on the ribbon.

The **modules** go into `%LOCALAPPDATA%\P142\WallFraming\`:

```
copy macros\wall-framing\source\bin\WF_Core.dll   "%LOCALAPPDATA%\P142\WallFraming\"
copy macros\wall-framing\source\bin\WF_Rules.dll  "%LOCALAPPDATA%\P142\WallFraming\"
copy macros\wall-framing\source\bin\WF_License.dll "%LOCALAPPDATA%\P142\WallFraming\"
```

The **package** `package/WallFramingAutoDimension.zip` holds the same files
plus the sources, the documentation and the installer — one file to copy to
another computer.

## Test

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1
cd ..\tests
python test_rules.py
python test_license.py
```

`test_rules.py` and `test_license.py` need **no Tekla** — neither the rules
assembly nor the licence engine has a Tekla reference. The other harnesses
need a running Tekla Structures and `pythonnet`; see
[docs/TESTING.md](docs/TESTING.md).

`test_upgrade.py` compares the rules with the wall layout drawings that are
already in the model — it needs no reference dump and it **writes nothing**:
it is the harness to run against a new model.

The **reference dumps** the harnesses compare against (the model parts and
the measured dimension sets of the issued drawing) are **not** in this
repository: they hold the geometry of a client project. See
[tests/reference/README.md](tests/reference/README.md).

## What the macro writes

**Pro edition:** 37 objects in one run — 18 dimension sets, 11 wall-panel
marks, 5 section marks and 3 notes.

**Free edition:** 14 dimension sets — the stud chain of every wall panel and
the band chains of the layout.

Every object carries the user property `P142_WF_AUTODIM = 1`, so a re-run
replaces the previous output instead of duplicating it, and hand-made
annotations are never touched.

**12 of the 18 dimension chains** of the reference drawing are reproduced
exactly; the other six are hand edits in the reference (see the rules
document).

## The second macro: the AIO one

A GA sheet does not always carry **one** wall framing layout.  The sheet
**[176]** of the client carries the wall framing layout of **19 blocks**
side by side in one view.  The macro above works on **one** layout: it
takes the box of all the panels of the sheet, so the top and the bottom
chain cross every block, while the left and the right chain of each
block is right.

`WallFramingAutoDimensionPro-AIO` is the second macro of the package, and
it is **free — it asks for no licence key at all**:

* it groups the panels of the sheet into their **blocks** — the part of
  the assembly position before the first dash, `BP1-WP3` → `BP1`;
* it dimensions every group with the box of **its own** panels: the band
  chains, the top outline, the overall dimensions, the north run, the
  wall-panel marks, the section marks and the notes;
* the stud chain of every wall panel is drawn once, because the chain
  belongs to the panel and not to the layout;
* the section marks of the sheet are **kept**: the macro only copies the
  section marks of the drawing of a block into a layout that has none of
  its own.

The chain of one block never runs into the next one.  The rules of the
grouping are in
[WALL_FRAMING_RULES.md](docs/WALL_FRAMING_RULES.md) (R10) and they are
tested without a running Tekla (`tests/test_layout_groups.py`).

## The two editions

The macro is sold in two editions:

* the **free** edition draws the dimension chains — the stud chain of every
  wall panel and the band chains of the layout;
* the **Pro** edition draws everything, and a **licence key** unlocks it.

Without a key the Pro features are free for **three days**, from the first
run of the macro on that computer.  A key is tied to the computer it was
issued for, and the whole workflow — the owner's key tool, the activation
step, the trial and the clock-rollback rule — is in
**[docs/LICENSING.md](docs/LICENSING.md)**.

The Pro stages are **not in this repository**: they are a separate module
that is loaded only when the licence allows it, which is what the thin
loader in the macro catalog is for.

## Licence of the source

MIT, like the rest of this repository. The macro is independent of the MCP
server: the rules assembly references no Tekla type, and the test harnesses
use only `pythonnet` and the Tekla Open API.
