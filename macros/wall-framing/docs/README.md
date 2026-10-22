# Wall-framing layout auto-dimensioner — Tekla Structures 2025

A drawing macro that reproduces the annotations of the **WALL FRAMING
LAYOUT** GA drawing on any other GA drawing of the same model.

| | |
|---|---|
| model | `P142-COREL REEF_BP4_MOCKUP-R00`, checked against `MODEL-2` |
| reference drawing | **[50]** “WALL FRAMING LAYOUT” (GA) — read only, never written |
| target drawing | **the wall layout drawing that is open**, or — when it is not one — **every** wall layout drawing of the model. Found by its title, not by its mark |
| Tekla version | **2025.0** (`Tekla.Structures.*` v2025.0.0.0, .NET Framework 4.x) |
| language | C#, compiled with the Roslyn `csc.exe` of VS 2022 Community |
| risk | **MEDIUM** — it creates drawing objects in the wall layout drawings; it never touches the model |
| deployment | **`package/WallFramingAutoDimension.zip`** — a thin loader, a few modules, one script, no DLL to register |
| editions | **free** (the dimension chains) and **Pro** (everything) — [LICENSING.md](LICENSING.md) |

## What it produces

In one run, on the plan view of the target drawing, the **Pro** edition
writes:

* **18 dimension sets** — the stud chain of every wall panel (11), the
  band chains of the bottom / left / right edges of the layout (3), the
  **top outline** of the layout (1), the **overall height** and **overall
  width** of the layout (2) and the **north run** (1);
* **11 wall-panel marks** — `BP4-WP1` … `BP4-WP11`, associative
  `ASSEMBLY_POS` property elements;
* **5 section marks** — `S1` … `S5`, copied from the reference drawing
  (they are hand placed and cannot be derived, see
  [WALL_FRAMING_RULES.md](WALL_FRAMING_RULES.md));
* **3 text notes** — the LGS / plasterboard typical notes.

The **free** edition writes the first two groups only — the 11 panel
chains and the 3 band chains, 14 dimension sets.

Every object it creates is tagged with the user property
`P142_WF_AUTODIM = 1`, so a re-run **replaces** its own output instead of
duplicating it.

## Status

| check | result |
|---|---|
| dimension chains of [50] reproduced in [57] | **12 of 18** (the other 6 are hand edits in [50] — see the rules document) |
| wall-panel mark text angle | 11 of 11 |
| wall-panel mark leader line | 9 of 11 |
| wall-panel mark label offset | 9 of 11 |
| last macro run | 37 objects created, **0 failed**, drawing saved |
| [50] | **never written to** — the macro refuses an issued or frozen drawing |
| target selection | the active drawing is used when it is a wall layout drawing, otherwise **every** wall layout drawing of the model is worked on — **2 of 2 tests pass** |
| panel detection | the configured prefix `BP4-WP` finds 11 panels; the automatic detection finds the same 11 and leaves the ceiling panels alone |
| the rules on a model with many drawings | `MODEL-2`: **205 of 291** chains are a subset of a hand-made chain, 0 failures — `tests/test_upgrade.py` |
| licence engine | **42 of 42 checks pass** — the key, the three day trial, the machine binding, the clock-rollback rule |
| the Pro path | **never run against a live Tekla** — the rules were moved without changing an answer, but the writing path of `WF_Pro.dll` is not proven |

## Which drawing does it work on?

**The mark is never used to recognise a drawing**, so the same file works on
every computer of the project, whatever the wall layout drawing happens to
be called there.

1. The **active** drawing is used when its title holds a wall layout
   fragment (`WALL FRAMING`, `WALL LAYOUT`, `WALL FRAMING LAYOUT`).
2. Otherwise **every** wall layout drawing of the model that may be written
   to is worked on, one after the other — a model has one wall layout
   drawing per block.
3. A drawing that is **issued or frozen is never written to** — the macro
   skips it and says so. This is what keeps it away from [50], the issued
   reference drawing of this project.

The **wall panels of a drawing are the assemblies the drawing shows**: a GA
drawing shows the assemblies of its own block and of no other, so a block is
never dimensioned on the drawing of another block.

The panel drawings of the model are named `WALL_PANEL` and the other
layout drawings are named `BASE FRAMING LAYOUT` and `CEILING FRAMING
LAYOUT`; none of them matches, so the macro never touches them.

## Layout of the sources

```
macros/wall-framing/
  README.md                      the short description of the macro
  INSTALL_NOTES.txt              the step-by-step installation, in plain text
  README.txt                     the same notes in brief, for the package
  install.ps1                    installs the macro into the macro catalog
                                 of THIS computer (used by the package)
  source/
    WF_Rules.cs                  the free annotation rules - NO Tekla reference,
                                 unit-testable without a running Tekla
    WF_License.cs                the licence engine - NO Tekla reference
    WF_Core.cs                   the shared drawing machinery
    WF_KeyGen.cs                 the owner's licence key tool
    P142_WF_AutoDimension.cs     the LOADER: it holds no rule and no logic
    build.ps1                    builds the modules, the tool and the package;
                                 finds Tekla and the C# compiler by itself
    bin/                         the build output
  package/
    WallFramingAutoDimension.zip    the free deployment package
    WallFramingAutoDimensionPro.zip the Pro deployment package
  docs/
    README.md                    this file
    ARCHITECTURE.md              the layers, the data flow, the MVP plan
    API_NOTES.md                 the Tekla 2025 API facts this project relies on
    LICENSING.md                 the two editions, the trial, the key workflow
    SETUP.md                     how to build, deploy and run it
    TESTING.md                   the test harnesses
    TODO.md                      what is left to do
    CHANGELOG.md                 the history
    WALL_FRAMING_RULES.md        the annotation rules, derived from [50]
                                 and from MODEL-2
  tests/                         the test harnesses
    reference/                   the reference dumps - not in the repository
```

## Build and run

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1            :: build
powershell -NoProfile -File build.ps1 -Package   :: build + the deployment package
powershell -NoProfile -File build.ps1 -Deploy    :: build + install in the macro catalog
```

**Command Prompt (Windows)**

```
cd macros\wall-framing\tests
python run_macro.py "[57]"             :: run the macro into [57] and save
python test_rules.py                   :: the rules against the reference (no Tekla needed)
python test_license.py                 :: the licence engine (no Tekla needed)
python test_target_selection.py        :: which drawing the macro works on
python test_upgrade.py                 :: the rules on a model with many drawings
python cmp_chains.py                   :: [50] vs [57]
```

See [SETUP.md](SETUP.md) for the full procedure and [TESTING.md](TESTING.md)
for what each harness checks.

## Installing it on another computer

**Command Prompt (Windows)**

```
:: on the other computer, in the folder the package was unzipped into
powershell -NoProfile -File .\install.ps1
```

The script finds the Tekla Structures installation by itself, copies the
loader and the icon into `…\Environments\common\macros\drawings`, and the
modules into `%LOCALAPPDATA%\P142\WallFraming\`.  The macro is then on
the ribbon under *Applications ▸ Macros ▸ P142_WF_AutoDimension*.

Without a licence the Pro features are free for **three days**.  To use
them after that, the key goes into
`%LOCALAPPDATA%\P142\WallFraming\license.txt` — see
[LICENSING.md](LICENSING.md).

## The one hard rule

> **[50] is the reference and must never be written to.**
> The macro refuses any drawing that is **issued or frozen**, and [50] is
> frozen. `tests/test_target_selection.py` proves that the macro stops
> with a message and leaves [50] with its 18 dimension sets, 126 marks,
> 5 section marks and 3 texts.

## The other documents

* [WALL_FRAMING_RULES.md](WALL_FRAMING_RULES.md) — every rule, with the
  evidence from [50] it was derived from, and the six places where [50]
  contradicts itself.
* [API_NOTES.md](API_NOTES.md) — the ten Tekla 2025 API traps this
  project ran into, each with the code that works.
* [LICENSING.md](LICENSING.md) — the two editions, the three day trial,
  the machine binding and the key workflow of the owner.
