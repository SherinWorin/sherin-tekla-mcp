# 05 — Build and verify

Every command is labelled.  **Command Prompt (Windows)** is the default on
the owner's machine — PowerShell is blocked there, so a `.ps1` is only ever
*called* from Command Prompt, never run in a PowerShell window, and never
with `-ExecutionPolicy Bypass` (the safety layer blocks that form).

---

## A. Get the code

**Command Prompt (Windows)**

```
git clone https://github.com/SherinWorin/sherin-tekla-mcp.git
cd sherin-tekla-mcp
git checkout feature/wall-framing-dimensioner
```

The macro is under `macros/wall-framing/`.  The C# server is the rest.

**Ubuntu (WSL)**

```
git clone https://github.com/SherinWorin/sherin-tekla-mcp.git
cd sherin-tekla-mcp
git checkout feature/wall-framing-dimensioner
```

(The macro itself cannot be built on WSL — it needs the Windows .NET
Framework and the Windows Tekla install.  The C# server's **tests** run on
WSL/Linux against the mock backend.)

---

## B. Build the macro

Needs Visual Studio 2022 (or the Build Tools) for the Roslyn compiler.

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1
```

It writes into `source\bin\` — **nothing outside the macro folder is
touched**:

| file | what it is |
|---|---|
| `WF_Rules.dll` | the free rules. No Tekla reference. |
| `WF_License.dll` | the licence engine. No Tekla reference. |
| `WF_Core.dll` | the shared drawing machinery |
| `WF_KeyGen.exe` | the owner's licence key tool |
| `P142_WF_AutoDimension.dll` | **the loader** — the file to install |
| `P142_WF_AutoDimension.bmp` | the ribbon icon |
| `WallFramingAutoDimensionPro-AIO.dll` | **the loader of the AIO macro** — the second file to install |
| `WallFramingAutoDimensionPro-AIO.bmp` | the ribbon icon of the AIO macro |

The loader holds **no rule and no drawing logic**: it loads `WF_Core.dll`
out of `%LOCALAPPDATA%\P142\WallFraming` at run time.  That is what keeps
the Pro stages out of the public repository.

The build prints `BUILD OK` at the end.

### The Pro edition

The Pro stages are **not in this repository** — they live in the owner's
private tree.  The build finds them when `-ProPath` points at it:

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -ProPath "<the private sources>"
```

It then also writes `WF_Rules_Pro.dll` (the Pro rules, no Tekla
reference — so they stay testable) and `WF_Pro.dll` (the Pro stages).

### The deployment package

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package
```

writes `macros\wall-framing\package\WallFramingAutoDimension.zip` and the
unzipped `package\WallFramingAutoDimension\`.  With `-ProPath` it writes
the Pro package beside it, `package\WallFramingAutoDimensionPro.zip`.

**`-AioFree`** puts the Pro modules into the **free** package as well, so
that the AIO macro of a free install draws the whole set of annotations:

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package -AioFree -ProPath "<the private sources>"
```

The switch is **off** by default, because the Pro stages are not
published (rule R10).  Turning it on is the owner's commercial decision —
see [06-OPEN-QUESTIONS.md](06-OPEN-QUESTIONS.md) (A9b).

**The zip is written by hand** with `System.IO.Compression.ZipArchive`, so
the entries use `/` as the separator.  `Compress-Archive` writes `\`, and
`unzip` then refuses the file with *"appears to use backslashes as path
separators"*.

---

## C. Install the macro — the human does this

See [02-ENVIRONMENT.md](02-ENVIRONMENT.md) for the full instructions.

**Command Prompt (Windows)**

```
set TEKLA=C:\TeklaStructures\2025.0
copy "macros\wall-framing\source\bin\P142_WF_AutoDimension.cs" "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
copy "macros\wall-framing\source\bin\P142_WF_AutoDimension.bmp" "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
copy "macros\wall-framing\source\bin\WallFramingAutoDimensionPro-AIO.cs" "%TEKLA%\Environments\common\macros\drawings\WallFramingAutoDimensionPro-AIO.cs"
copy "macros\wall-framing\source\bin\WallFramingAutoDimensionPro-AIO.bmp" "%TEKLA%\Environments\common\macros\drawings\WallFramingAutoDimensionPro-AIO.bmp"

set APP=%LOCALAPPDATA%\P142\WallFraming
if not exist "%APP%" mkdir "%APP%"
copy "macros\wall-framing\source\bin\WF_Core.dll"    "%APP%\"
copy "macros\wall-framing\source\bin\WF_Rules.dll"   "%APP%\"
copy "macros\wall-framing\source\bin\WF_License.dll" "%APP%\"
```

For the **Pro** edition the two Pro modules go with them — and they
belong together with the three above, because the Pro module is built
against the rules:

```
copy "macros\wall-framing\source\bin\WF_Pro.dll"      "%APP%\"
copy "macros\wall-framing\source\bin\WF_Rules_Pro.dll" "%APP%\"
```

A **mixed** set — a new core with an old Pro module, or the other way
round — is now reported as such by the version handshake, instead of
failing once per object with a bare `Method not found`.  If that
message comes up, copy all five again; do not try to fix it in the
code.

**The loader is about 8000 bytes** — it is a thin file, not the whole
macro.  A file of about 94000 bytes is the **old** merged source; it holds
the Pro stages and must not be installed.

**Restart Tekla afterwards** if it was already open.

---

## D. Run the harnesses

Eight harnesses, on two levels.

| harness | needs Tekla | what it proves |
|---|---|---|
| `tests/test_rules.py` | **no** | the rules produce the right numbers, and the panels are found automatically |
| `tests/test_license.py` | **no** | the key verifies, the trial is three days, the machine is bound |
| `tests/test_marks.py` | **no** | the marks are placed like the ones of the reference (a Pro rule) |
| `tests/test_section_lines.py` | **no** | the section lines that belong to a layout are told from the lines of another block (a Pro rule) |
| `tests/test_version_handshake.py` | **no** | the rules and the Pro module can tell when they are of different versions |
| `tests/test_loader.py` | **no** (the assemblies are loaded) | the deployment is complete: the core and the Pro module load, the licence answers, and the **installed** modules are of the same version |
| `tests/cmp_chains.py` | yes | the drawing that was written contains the right chains |
| `tests/test_target_selection.py` | yes | the macro works on the right drawing and refuses an issued one |
| `tests/audit_50_57.py` | yes | both drawings are in a sane state, and [50] was not touched |
| `tests/test_upgrade.py` | yes | the rules hold on a model with many wall layout drawings (`MODEL-2`) |

### The rules, the licence and the loader — no Tekla needed

**Command Prompt (Windows)**

```
cd macros\wall-framing
python tests\test_rules.py
python tests\test_license.py
python tests\test_version_handshake.py
python tests\test_loader.py
python tests\test_marks.py
python tests\test_section_lines.py
python tests\test_layout_groups.py
```

Expected:

```
exact chain matches: 12 / 18
42 passed, 0 failed
FAILURES: 0
the loader chain is OK
exact mark placements: 7 / 11
  text angle  (rule-derived):  11 / 11
  leader line (rule-derived):   9 / 11
  label offset(rule-derived):   9 / 11
12 passed, 0 failed
26 passed, 0 failed
```

`test_marks.py` needs the **Pro rules** (`WF_Rules_Pro.dll`), so it needs
a build with `-ProPath`.  Without them it says so and stops without a
failure.  The same is true of `test_section_lines.py`.

The six chains that do not match are the **hand edits of the issued
drawing** — documented in `docs/WALL_FRAMING_RULES.md`, and deliberately
not reproduced.

> The rules, the marks and the licence harnesses need the reference dumps.
> Without them they stop with a clear message pointing at
> `tests/reference/README.md` — that is the intended behaviour, not a
> failure.  See [REFERENCE-DATA.md](REFERENCE-DATA.md).  The dumps can also
> be found through the `WF_REFERENCE` environment variable.

### The drawings — Tekla must be running

Tekla open, the model open, **the title without `(Loading)`**.

**Command Prompt (Windows)**

```
cd macros\wall-framing
python tests\test_target_selection.py
python tests\cmp_chains.py
python tests\audit_50_57.py
python tests\test_upgrade.py
```

Expected:

| harness | expected |
|---|---|
| `test_target_selection.py` | 2 of 2 tests pass |
| `cmp_chains.py` | 12 of 18 chains exact |
| `audit_50_57.py` | [50] = 18 dimsets / 126 marks / 5 section marks / 3 texts — **unchanged** |
| `test_upgrade.py` | 205 of 291 chains are a subset of a hand-made chain, 0 failures |

`test_upgrade.py` needs **no reference dump**: it compares the rules with
the drawings that are already in the model, and it **writes nothing** — it
is the harness to run against a new model.

**`audit_50_57.py` is the safety check.**  If [50] changed, something wrote
to the issued drawing — see R1 in [03-HARD-RULES.md](03-HARD-RULES.md).

### Run the macro itself

**Command Prompt (Windows)**

```
cd macros\wall-framing
python tests\run_macro.py "[57]"
```

`[57]` is the target on this model.  On another computer the mark is
different — the macro finds the drawing by its **title**, so the argument is
only needed when the macro is driven from a script.

**In Tekla**, the normal way is the ribbon:
`Applications > Macros > P142_WF_AutoDimension`.

---

## E. Build and test the C# MCP server

**Command Prompt (Windows)** (or the Visual Studio *Developer Command
Prompt*)

```
dotnet build SherinTeklaMcp.sln -c Release
dotnet test tests/SherinTeklaMcp.Core.Tests
```

**Ubuntu (WSL)**

```
dotnet build SherinTeklaMcp.sln -c Release
dotnet test tests/SherinTeklaMcp.Core.Tests -c Release
```

Expected: **48 tests, all passing.**

The CI does the same plus a smoke test of the server over stdio against the
mock backend:

**Command Prompt (Windows)**

```
python scripts\smoke_test.py src\SherinTeklaMcp.Server\bin\Release\net48\SherinTeklaMcp.Server.exe
```

**Ubuntu (WSL)**

```
python3 scripts/smoke_test.py dotnet src/SherinTeklaMcp.Server/bin/Release/net8.0/SherinTeklaMcp.Server.dll
```

**After any change:** run the tests, then update `docs/TOOLS.md` and
`CHANGELOG.md`.  That is the rule of the repository, and the CI checks the
build.

---

## F. The checks of the state

Run this list at the start of a session on a new computer.  Everything
should pass without a change.

| # | check | how | expected |
|---|---|---|---|
| 1 | the branch | `git branch --show-current` | `feature/wall-framing-dimensioner` |
| 2 | the tree is clean | `git status --short` | no output |
| 3 | the macro builds | `build.ps1` | `BUILD OK` |
| 4 | the loader is thin | `dir source\bin\P142_WF_AutoDimension.cs` | about 8000 bytes |
| 5 | the loader holds no rule | `grep -c "namespace P142.WallFraming" source/P142_WF_AutoDimension.cs` | 0 |
| 6 | the installed file is the loader | `dir "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"` | about 8000 bytes |
| 7 | the modules are installed | `dir "%LOCALAPPDATA%\P142\WallFraming\*.dll"` | WF_Core, WF_Rules, WF_License |
| 8 | the rules have no Tekla reference | `grep -c "^using Tekla" source/WF_Rules.cs` | 0 |
| 9 | the licence engine has no Tekla reference | `grep -c "^using Tekla" source/WF_License.cs` | 0 |
| 10 | the licence passes | `python tests\test_license.py` | 42 passed, 0 failed |
| 11 | the C# tests pass | `dotnet test tests/SherinTeklaMcp.Core.Tests` | 48 passed |
| 12 | the client data is not committed | `git ls-files \| grep -E "asm_parts\|full_50\|marks_50\|dims_50"` | no output |
| 13 | the Pro sources are not committed | `git ls-files \| grep -E "WF_Pro\|WF_Rules_Pro"` | no output |
| 14 | the PR is open | the PR page | PR #1, both CI jobs green |
| 15 | [50] is untouched | `python tests\audit_50_57.py` | 18 dimsets / 126 marks / 5 sections / 3 texts |
| 16 | the old merged source is not lying around | `dir source\bin\*merged*` | no output |
