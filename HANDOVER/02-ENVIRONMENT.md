# 02 — Environment

## What the new computer needs

| what | needed for | without it |
|---|---|---|
| **Tekla Structures 2025** (build 2025.0.46985.0 or later 2025.x) | running the macro, running the MCP server | nothing works |
| **Windows 10/11 x64** | everything | — |
| **.NET Framework 4.x** | the macro and the net48 server | ships with Windows; check it is present |
| **Visual Studio 2022** (any edition, or the Build Tools) | the Roslyn `csc.exe` — only to **rebuild** the macro | the macro still runs; you just cannot rebuild it |
| **Python 3.12 + `pythonnet`** | the **test harnesses only** | the macro still runs; you just cannot run the harnesses |
| **Claude Code** | to drive the MCP server | the macro still runs from the ribbon |

**The macro itself needs none of the optional parts.**  It is one `.cs`
file and one `.bmp`.  No DLL to register, no NuGet, no MSBuild, no Python.

## The four gotchas that make the tools silently return nothing

These cost real time on the first machine.  They are not obvious from the
code and they produce **no error message**.

### 1. `read_only: true` only filters the tool list

In the **Python** server's `config/settings.json`, `"read_only": true`
makes `ReadOnlyToolFilter` **hide** the write tools from the client.  It is
**not** a runtime write-block.  A tool that is mislabelled
`readOnlyHint: True` still writes to the live model.

`AGENTS.md` in that repository flags this as security-critical, and the
audit script is at `Tekla-MCP/debug/audit_readonly_hints.py`.

> The **C#** server in the GitHub repository does not have this problem:
> its `ReadOnly` check is in the code path of every write, not in a filter.

### 2. The remoting channel is session-scoped

Tekla must run **in the same Windows session** as the MCP client.  A
session-0 `tekla.exe` is a different service, not Tekla Structures — the
real binary is `TeklaStructures.exe`.

### 3. The channel only exists after the model has finished loading

The Tekla window title shows `(Loading)` for 30–60 seconds after the model
opens.  **Before that, every tool returns no data.**  Wait for the title
to lose `(Loading)`.

### 4. `element_types.json` does not match every model

In the Python server, `STEEL_BEAM` maps to class 100, but the
`LiveTest_Disposable` model's beams are in classes 0, 2, 3, 4, 5, 6, 8.
Selecting by `element_type: "Steel Beam"` silently returns
`selected_count: 0`.  Select by `tekla_classes: [2, 3]` instead.

### The order to check, when a tool returns nothing

1. `tekla://connection_status`
2. the Tekla window title — is it still `(Loading)`?
3. is it a class-mapping mismatch, rather than a broken server?

### The Trimble login failure is harmless

Trimble Identity login fails on this machine with *Entry point was not
found* (outbound HTTPS to Trimble is blocked).  **It does not stop the MCP
connection.**  It is a cosmetic failure; ignore it.

## The macro catalog

The macro lives in the **macro catalog of the drawings**:

```
<Tekla installation>\Environments\common\macros\drawings\
```

On this machine that is
`C:\TeklaStructures\2025.0\Environments\common\macros\drawings\`.

**The catalog is read once, at start-up.**  If Tekla was already open when
the files were copied in, **restart Tekla** — otherwise the macro does not
appear on the ribbon and the reason is not obvious.

## Installing the macro — for the human

The full guide, in plain text, is
[`macros/wall-framing/INSTALL_NOTES.txt`](../macros/wall-framing/INSTALL_NOTES.txt).
It is also inside the deployment package.

### The file to install

```
P142_WF_AutoDimension_merged.cs   (~94000 bytes)   <- this one
P142_WF_AutoDimension.bmp         (the ribbon icon)
```

**The merged file holds the macro AND the rules in one file.**  The
`source/P142_WF_AutoDimension.cs` in the repository is the macro **only**
(~49000 bytes) and Tekla cannot compile it alone — the error is a missing
namespace and it does not point at the cause.

The installer checks this and refuses a file that is not the merged one.

### Method A — manual (Command Prompt (Windows) or Explorer)

```
set TEKLA=C:\TeklaStructures\2025.0
copy "<folder>\P142_WF_AutoDimension_merged.cs" "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
copy "<folder>\P142_WF_AutoDimension.bmp"       "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
```

**The rename matters:** the name of the `.cs` file **is** the name of the
macro on the ribbon.  Leave it as `_merged` and the ribbon shows
`P142_WF_AutoDimension_merged`.

Check afterwards: the file must be about **94000 bytes**.

### Method B — the installer script (where PowerShell is available)

```
powershell -NoProfile -File install.ps1 -WhatIfOnly     :: dry run first
powershell -NoProfile -File install.ps1                 :: then install
```

The script finds the Tekla installation by itself (`TEKLA_HOME`,
`C:\TeklaStructures\*`, `C:\Program Files\Tekla Structures\*`, the other
drives; a 2025 installation wins).  `-TeklaPath` overrides the search.

> **Note on the terminal.**  Every command in this pack is labelled
> **Command Prompt (Windows)** or **Ubuntu (WSL)**.  PowerShell is blocked
> on the owner's PC, so the manual method comes first and the script is
> optional.  Where a `.ps1` is unavoidable, it is called as
> `powershell -NoProfile -File <script>` from Command Prompt — **not**
> `powershell -ExecutionPolicy Bypass`, which the safety layer blocks.

## Running it

1. Start Tekla Structures 2025 and open the model.
2. Open the GA drawing whose title holds **`WALL FRAMING LAYOUT`**.
   The mark does not matter — on another computer it will be a different
   mark.
3. **Applications > Macros > P142_WF_AutoDimension**
4. A message box appears: *"Wall framing annotations: 37 created, 0 failed,
   0 skipped."* and the path of the log file.
5. The macro **saves the drawing itself**.  Nothing else to do.

### The first-run checklist

| what to look at | what it should say |
|---|---|
| first line of the log | *"the active drawing [xx] is the wall layout drawing"* |
| the panel count | *"wall panels: 11"* (or however many that model has) |
| the result | *"0 failed"* |
| the issued drawing | **UNTOUCHED** — the macro refuses a frozen drawing and says so |

The log is `wfdim_log_<mark>.txt`, next to the macro file or in the
temporary folder.

If the log says *"no part matched the prefix 'BP4-WP' — the wall panels were
found automatically"*, that is **correct** on a project with a different
naming: the macro detected the wall panels by themselves.

## The Python harness environment

```
C:\Users\sheri\AppData\Local\Programs\Python\Python312\python.exe
```

```
pip install pythonnet
```

`pythonnet` **may be missing** on a fresh machine.  Without it the
harnesses cannot run; the macro is unaffected.

`tests/tekla_bootstrap.py` finds the Tekla installation by itself and loads
the assemblies.

## The C# server environment

```
dotnet build SherinTeklaMcp.sln -c Release
dotnet test tests/SherinTeklaMcp.Core.Tests
```

The Tekla DLLs are **not** copied into the build.  The server loads them
from the installed Tekla at start-up, which is deliberate (the DLLs are
Trimble's and are not redistributed).

`config/settings.json` is read **once** — restart the client after a
change.
