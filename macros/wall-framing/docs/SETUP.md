# Setup

## What is needed

| | |
|---|---|
| Tekla Structures | **2025.0** — the path is **not** hard coded, the scripts find it |
| Visual Studio | 2022 (Community, Professional, Enterprise or the Build Tools) — only for the Roslyn compiler `csc.exe` |
| .NET Framework | 4.x on the machine (ships with Windows) |
| Python | 3.12 with `pythonnet` — for the test harnesses only, the macro itself needs no Python |

The macro itself is a **thin `.cs` file** and a few modules. It does not
need Python, a NuGet package, a DLL to register or an MSBuild project.

## Install on another computer (the package)

The package is `package/WallFramingAutoDimension.zip` (the free edition)
or `package/WallFramingAutoDimensionPro.zip` (the Pro edition).

**`INSTALL_NOTES.txt` in the package is the step-by-step guide** — the
manual method, the optional script, the first-run checklist and the
troubleshooting. It is plain text and can be opened in Notepad.

1. Unzip it into any folder, e.g. `C:\Temp\WallFramingAutoDimension`.
2. Copy the loader and the icon into the macro catalog of Tekla:

   **Command Prompt (Windows)**

   ```
   set TEKLA=C:\TeklaStructures\2025.0
   copy source\P142_WF_AutoDimension.cs "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
   copy source\P142_WF_AutoDimension.bmp "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
   ```

   The loader is already called `P142_WF_AutoDimension.cs` — the name of
   the file is the name of the macro on the ribbon.

3. Copy the modules into the app folder:

   **Command Prompt (Windows)**

   ```
   set APP=%LOCALAPPDATA%\P142\WallFraming
   if not exist "%APP%" mkdir "%APP%"
   copy source\WF_Core.dll    "%APP%\"
   copy source\WF_Rules.dll   "%APP%\"
   copy source\WF_License.dll "%APP%\"
   copy source\WF_Pro.dll     "%APP%\"
   ```

   The last line is only in the Pro package.

4. Or let the installer do steps 2 and 3, where PowerShell is available:

   **Command Prompt (Windows)**

   ```
   powershell -NoProfile -File .\install.ps1 -WhatIfOnly     :: dry run
   powershell -NoProfile -File .\install.ps1                 :: install
   ```

   The script finds the Tekla Structures installation by itself (it looks
   at `TEKLA_HOME`, at `C:\TeklaStructures\*`, at
   `C:\Program Files\Tekla Structures\*` and at the other drives, and it
   prefers a 2025 installation).  If it does not find it, give the path:

   **Command Prompt (Windows)**

   ```
   powershell -NoProfile -File .\install.ps1 -TeklaPath "C:\TeklaStructures\2025.0"
   ```

5. Start Tekla. The macro is on the ribbon under
   **Applications ▸ Macros ▸ P142_WF_AutoDimension**.

There is no second DLL to register and nothing to install: the modules are
plain assemblies that the loader picks up at run time.

## The licence

Without a licence the **Pro** features are free for **three days**, from
the first run of the macro on that computer.  To use the Pro edition after
that, the key goes into

```
%LOCALAPPDATA%\P142\WallFraming\license.txt
```

See [LICENSING.md](LICENSING.md) for the whole workflow, and for the
activation step the owner has to do once (the public key in
`source/WF_License.cs`).

## Build from the sources

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1
```

The script

1. finds the Tekla Structures installation and the C# compiler of Visual
   Studio by itself (`-TeklaPath` and `-CscPath` override the search);
2. builds `source/bin/WF_Rules.dll` — the free rules, no Tekla reference;
3. builds `source/bin/WF_License.dll` — the licence engine, no Tekla
   reference;
4. builds `source/bin/WF_Core.dll` — the shared drawing machinery;
5. builds `source/bin/WF_KeyGen.exe` — the owner's key tool;
6. builds `source/bin/P142_WF_AutoDimension.dll` — the **loader**, the
   file the Tekla macro catalog compiles.

Expected output:

```
Tekla  : C:\TeklaStructures\2025.0
csc    : C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe
Pro    : (not found - the free edition only)
Building WF_Rules.dll ...
  OK  WF_Rules.dll  (12288 bytes)
Building WF_License.dll ...
  OK  WF_License.dll  (15360 bytes)
Building WF_Core.dll ...
  OK  WF_Core.dll  (20992 bytes)
Building WF_KeyGen.exe ...
  OK  WF_KeyGen.exe  (23552 bytes)
Building P142_WF_AutoDimension.dll (the loader) ...
  OK  P142_WF_AutoDimension.dll  (7168 bytes)
BUILD OK
```

> Use `powershell -NoProfile -File ...`. The `-ExecutionPolicy Bypass`
> form is not needed for a script that is run, not imported, and some
> security policies block the flag itself.

### The Pro edition

The Pro stages are **not in this repository**.  They live in the owner's
private tree, and the build finds them when `-ProPath` points at it:

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -ProPath "C:\path\to\private\wall-framing\private"
```

The build then also writes `WF_Rules_Pro.dll` (the Pro rules, no Tekla
reference — so they stay testable) and `WF_Pro.dll` (the Pro stages), and
the Pro package is written with `-Package`.

## Build the package

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package
```

This writes `package/WallFramingAutoDimension/` and zips it into
`package/WallFramingAutoDimension.zip`.  The package holds the loader, the
icon, the modules, the sources, the build script, the installer and the
documentation.

The package is self-contained: `source\build.ps1` rebuilds the modules
from the sources in the package.

## Deploy to the macro catalog (optional)

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Deploy
```

This copies

* `P142_WF_AutoDimension.cs` → `<Tekla>\Environments\common\macros\drawings\P142_WF_AutoDimension.cs`
* an icon → `…\P142_WF_AutoDimension.bmp`
* the modules → `%LOCALAPPDATA%\P142\WallFraming\`

> The deployment writes into the Tekla installation. It is deliberately a
> separate switch, so that a plain build never touches anything outside
> the project folder.

## Run the macro

### From the macro catalog

1. Open the **wall layout drawing** — the GA drawing whose title holds
   `WALL FRAMING LAYOUT` (for example **[57]**).
2. Run the macro from the ribbon.

The macro does not need a particular mark.  If the drawing that is open is
not a wall layout drawing, the macro activates the wall layout drawing of
the model.  If that drawing is issued or frozen, the macro stops and says
so instead of writing.

### From a script (what the test harnesses do)

**Command Prompt (Windows)**

```
cd macros\wall-framing\tests
<venv>\Scripts\python.exe run_macro.py "[57]"
```

The script switches to the requested drawing, **asserts** that the active
drawing really is that drawing, runs the macro through its UI-free entry
point `UserMacros.Macro.Execute(Drawing, Result)`, saves the drawing and
prints the log.

```
active drawing: [57]  P021.64_SD_MI_BP1 M/U_ST-103
--- running macro ---
--- done: ok=True made=37 failed=0 saved=True
```

### The log

Every run writes `wfdim_log_<mark>.txt` next to the macro DLL (or in the
temporary folder when that is not writable) and shows the path in the
message box. The log lists every object it created and every failure.

## Test

**Command Prompt (Windows)**

```
cd macros\wall-framing\tests
<venv>\Scripts\python.exe test_rules.py            :: the rules alone, no Tekla needed
<venv>\Scripts\python.exe test_license.py          :: the licence engine, no Tekla needed
<venv>\Scripts\python.exe test_target_selection.py :: which drawing the macro works on
<venv>\Scripts\python.exe cmp_chains.py            :: [50] vs [57], needs Tekla
<venv>\Scripts\python.exe test_marks.py            :: the mark placement
<venv>\Scripts\python.exe audit_50_57.py           :: the state of both drawings
```

See [TESTING.md](TESTING.md).

## Troubleshooting

| symptom | cause | what to do |
|---|---|---|
| `ERROR: no Tekla Structures installation was found.` | the installation is somewhere unusual | run the installer again with `-TeklaPath` |
| `ERROR: the package is incomplete` | the zip was not unpacked completely | unpack it again |
| `FATAL: no drawing editor is open.` | no drawing is open in Tekla | open a drawing |
| `FATAL: active drawing is [50], expected [57]. Refusing to write.` | the wrong drawing is active | switch to the target drawing — the script refuses to write to [50] |
| `FATAL: no drawing with mark 57.` | the mark is written with its brackets | use `run_macro.py "[57]"` |
| the macro creates nothing | the plan view was not found | the drawing must have a view that looks straight down |
| `No wall layout drawing to work on` | the active drawing is issued/frozen, or the model has no wall layout drawing | open the wall layout drawing; the message says which case it is |
| the objects disappear after the run | the drawing was not saved | the macro saves the drawing itself; the script saves it as well |
| `CS0234: The type or namespace name 'Drawing' does not exist` | the Tekla assemblies were not found | check that `<Tekla>\bin` holds the 2025 assemblies |
| `the module WF_Core.dll was not found` | the modules are not in `%LOCALAPPDATA%\P142\WallFraming\` | run `install.ps1` again, or copy the modules by hand |
| the free edition is in use although there is a licence | the key does not verify, or it was issued for another computer | read the message in the log; the key is in `%LOCALAPPDATA%\P142\WallFraming\license.txt` |
| the Pro annotations do not appear | `WF_Pro.dll` is not in the app folder | install the Pro package |
