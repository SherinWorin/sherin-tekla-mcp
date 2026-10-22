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
| `WF_Rules.dll` | the rules. No Tekla reference. |
| `P142_WF_AutoDimension.dll` | the macro |
| `P142_WF_AutoDimension_merged.cs` | **the file to install** — rules + macro in one |
| `P142_WF_AutoDimension_merged.dll` | proof that the merged file compiles |
| `P142_WF_AutoDimension.bmp` | the ribbon icon |

The script **compiles the merged file a second time**, so a broken merge
can never reach the catalog.  The build prints `BUILD OK` at the end.

### The build is reproducible

A fresh clone of the pushed branch produces the merged file **byte for
byte identical** to the one in the repository:

```
md5sum source/bin/P142_WF_AutoDimension_merged.cs
d3b296fdffd9a50e1fec9ba9804ca9c9
```

**The dev tree's copy has a different md5** (`f0c86011…`) because its
banner comment names a different build script.  That is the **only**
difference between the two trees — compare the **size** (94073 bytes) when
comparing across trees, not the hash.

### The deployment package

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package
```

writes `macros\wall-framing\package\WallFramingAutoDimension.zip` and the
unzipped `package\WallFramingAutoDimension\`.

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
copy "macros\wall-framing\source\bin\P142_WF_AutoDimension_merged.cs" "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
copy "macros\wall-framing\source\bin\P142_WF_AutoDimension.bmp"        "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
```

**Check the size afterwards — it must be about 94000 bytes.**  A file of
about 49000 bytes is the macro *without* the rules and will not compile.

**Restart Tekla afterwards** if it was already open.

---

## D. Run the harnesses

Five harnesses, on two levels.

| harness | needs Tekla | what it proves |
|---|---|---|
| `tests/test_rules.py` | **no** | the rules produce the right numbers, and the panels are found automatically |
| `tests/cmp_chains.py` | yes | the drawing that was written contains the right chains |
| `tests/test_marks.py` | yes | the marks are placed like the ones of the reference |
| `tests/test_target_selection.py` | yes | the macro works on the right drawing and refuses an issued one |
| `tests/audit_50_57.py` | yes | both drawings are in a sane state, and [50] was not touched |

### The rules — no Tekla needed

**Command Prompt (Windows)**

```
cd macros\wall-framing
python tests\test_rules.py
```

Expected:

```
exact chain matches: 12 / 18
```

The six that do not match are the **hand edits of the issued drawing** —
documented in `docs/WALL_FRAMING_RULES.md`, and deliberately not
reproduced.

> This harness needs the reference dumps.  Without them it stops with a
> clear message pointing at `tests/reference/README.md` — that is the
> intended behaviour, not a failure.  See [REFERENCE-DATA.md](REFERENCE-DATA.md).

### The drawings — Tekla must be running

Tekla open, the model open, **the title without `(Loading)`**.

**Command Prompt (Windows)**

```
cd macros\wall-framing
python tests\test_target_selection.py
python tests\cmp_chains.py
python tests\test_marks.py
python tests\audit_50_57.py
```

Expected:

| harness | expected |
|---|---|
| `test_target_selection.py` | 2 of 2 tests pass |
| `cmp_chains.py` | 12 of 18 chains exact |
| `test_marks.py` | angle 11/11, leader 9/11, offset 9/11 |
| `audit_50_57.py` | [50] = 18 dimsets / 126 marks / 5 section marks / 3 texts — **unchanged** |

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
| 4 | the merged file is right | `dir source\bin\P142_WF_AutoDimension_merged.cs` | 94073 bytes, md5 `d3b296fd…` |
| 5 | the installed file is right | `dir "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"` | ~94000 bytes, md5 `d3b296fd…` |
| 6 | the rules have no Tekla reference | `grep -c "^using Tekla" source/WF_Rules.cs` | 0 |
| 7 | the C# tests pass | `dotnet test tests/SherinTeklaMcp.Core.Tests` | 48 passed |
| 8 | the client data is not committed | `git ls-files \| grep -E "asm_parts\|full_50\|marks_50\|dims_50"` | no output |
| 9 | the PR is open | the PR page | PR #1, both CI jobs green |
| 10 | [50] is untouched | `python tests\audit_50_57.py` | 18 dimsets / 126 marks / 5 sections / 3 texts |
