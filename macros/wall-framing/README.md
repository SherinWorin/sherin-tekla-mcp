# Wall-framing layout auto-dimensioner

A drawing macro for **Tekla Structures 2025** that reproduces the annotations
of a **WALL FRAMING LAYOUT** GA drawing — the stud chains of every wall panel,
the band chains of the layout, the top outline, the overall dimensions, the
north run, the wall-panel marks, the section marks and the notes.

It was written for the P142 COREL REEF BP4 mockup, where the issued reference
drawing **[50]** "WALL FRAMING LAYOUT" was the model for the annotations. The
macro does **not** work on a fixed drawing number: it finds the wall layout
drawing by its **title**, so the same file works on every computer of a project.

| | |
|---|---|
| Tekla version | **2025.0** (`Tekla.Structures.*` v2025.0.0.0, .NET Framework 4.x) |
| language | C#, compiled with the Roslyn `csc.exe` of Visual Studio 2022 |
| install | two files in the Tekla macro catalog — no DLL to register |
| risk | **MEDIUM** — it creates drawing objects; it never touches the model, and it refuses an issued or frozen drawing |

## The layout

```
macros/wall-framing/
  README.md                this file
  INSTALL_NOTES.txt        the step-by-step installation, in plain text
  README.txt               the same notes in brief, for the package
  install.ps1              the installer (optional - the manual method needs no script)
  source/
    WF_Rules.cs            the annotation rules - NO Tekla reference
    P142_WF_AutoDimension.cs  the macro: reads the model, calls the rules, writes the drawing
    build.ps1              builds both, and the merged single-file source
    bin/                   the build output (not in the repository)
  docs/
    README.md              the full description of the macro
    ARCHITECTURE.md        the layers, the data flow, the target selection
    API_NOTES.md           the Tekla 2025 API facts and the eight traps found
    SETUP.md               how to build, install and run it
    TESTING.md             the test harnesses
    TODO.md                what is left to do
    CHANGELOG.md           the history
    WALL_FRAMING_RULES.md  every rule, with the reference set it was derived from
  package/
    WallFramingAutoDimension.zip   the deployment package - one file for the other PC
  tests/
    tekla_bootstrap.py     loads the Tekla assemblies for pythonnet
    test_rules.py          the rules against the reference  (no Tekla needed)
    test_target_selection.py  which drawing the macro works on
    cmp_chains.py          the generated drawing against the reference
    test_marks.py          the mark placement
    audit_50_57.py         the state of both drawings
    run_macro.py           runs the macro and saves the drawing
    dump_*.py              the scripts that (re)create the reference dumps
    reference/             the reference dumps - see reference/README.md
```

## Which drawing does it work on?

The **mark is never used** to recognise a drawing:

1. the **active** drawing is used when its title holds `WALL FRAMING`,
   `WALL LAYOUT` or `WALL FRAMING LAYOUT`;
2. otherwise the wall layout drawing of the model is looked up and
   **activated**;
3. a drawing that is **issued or frozen is never written to** — the macro
   stops and says so.

Step 3 is what protects the issued reference drawing of a project: it is
frozen, so the macro refuses it even when it is the active drawing.

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

* `source\bin\WF_Rules.dll` — the rules, no Tekla reference;
* `source\bin\P142_WF_AutoDimension.dll` — the macro;
* `source\bin\P142_WF_AutoDimension_merged.cs` — **the rules and the macro in
  one file**, which is what the Tekla macro catalog compiles. The build
  compiles that merged file as well, so a broken merge can never reach the
  catalog.

The build script finds the Tekla installation and the C# compiler by itself;
`-TeklaPath` and `-CscPath` override the search.

## Install

The full procedure is in **[INSTALL_NOTES.txt](INSTALL_NOTES.txt)** — the
manual method (two files copied) and the optional script.

**Command Prompt (Windows)**

```
copy macros\wall-framing\source\bin\P142_WF_AutoDimension_merged.cs "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
copy macros\wall-framing\source\bin\P142_WF_AutoDimension.bmp       "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
```

where `%TEKLA%` is the Tekla installation, e.g. `C:\TeklaStructures\2025.0`.
The `.cs` file **must** be named `P142_WF_AutoDimension.cs` in the catalog:
the name of the file is the name of the macro on the ribbon.

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
```

`test_rules.py` needs **no Tekla** — the rules assembly has no Tekla
reference. The other harnesses need a running Tekla Structures and
`pythonnet`; see [docs/TESTING.md](docs/TESTING.md).

The **reference dumps** the harnesses compare against (the model parts and
the measured dimension sets of the issued drawing) are **not** in this
repository: they hold the geometry of a client project. See
[tests/reference/README.md](tests/reference/README.md).

## What the macro writes

37 objects in one run: 18 dimension sets, 11 wall-panel marks, 5 section
marks and 3 notes. Every object carries the user property
`P142_WF_AUTODIM = 1`, so a re-run replaces the previous output instead of
duplicating it, and hand-made annotations are never touched.

**12 of the 18 dimension chains** of the reference drawing are reproduced
exactly; the other six are hand edits in the reference (see the rules
document).

## Licence

MIT, like the rest of this repository. The macro is independent of the MCP
server: the rules assembly references no Tekla type, and the test harnesses
use only `pythonnet` and the Tekla Open API.
