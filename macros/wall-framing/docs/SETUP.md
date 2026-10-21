# Setup

## What is needed

| | |
|---|---|
| Tekla Structures | **2025.0** — the path is **not** hard coded, the scripts find it |
| Visual Studio | 2022 (Community, Professional, Enterprise or the Build Tools) — only for the Roslyn compiler `csc.exe` |
| .NET Framework | 4.x on the machine (ships with Windows) |
| Python | 3.12 with `pythonnet` — for the test harnesses only, the macro itself needs no Python |

The macro itself is **one `.cs` file**. It does not need Python, a NuGet
package, a DLL to register or an MSBuild project.

## Install on another computer (the package)

The package is `package/WallFramingAutoDimension.zip`.

**`INSTALL_NOTES.txt` in the package is the step-by-step guide** — the
manual method, the optional script, the first-run checklist and the
troubleshooting. It is plain text and can be opened in Notepad.

1. Unzip it into any folder, e.g. `C:\Temp\WallFramingAutoDimension`.
2. Copy the two files into the macro catalog of Tekla:

   **Command Prompt (Windows)**

   ```
   set TEKLA=C:\TeklaStructures\2025.0
   copy source\P142_WF_AutoDimension_merged.cs "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.cs"
   copy source\P142_WF_AutoDimension.bmp       "%TEKLA%\Environments\common\macros\drawings\P142_WF_AutoDimension.bmp"
   ```

   The merged file keeps the `_merged` name inside the package, because the
   same folder also holds the two sources it was made of. **It has to be
   renamed to `P142_WF_AutoDimension.cs` in the catalog** — the name of the
   file is the name of the macro on the ribbon.

3. Or let the installer do it, where PowerShell is available:

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

4. Start Tekla. The macro is on the ribbon under
   **Applications ▸ Macros ▸ P142_WF_AutoDimension**.

That is all: the rules and the macro are merged into the one `.cs` file, so
there is no second DLL and nothing to register.

## Build from the sources

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1
```

The script

1. finds the Tekla Structures installation and the C# compiler of Visual
   Studio by itself (`-TeklaPath` and `-CscPath` override the search);
2. builds `source/bin/WF_Rules.dll` — the rules, no Tekla reference;
3. builds `source/bin/P142_WF_AutoDimension.dll` — the macro, against the
   rules and the Tekla assemblies;
4. writes `source/bin/P142_WF_AutoDimension_merged.cs` — the rules and
   the macro in **one file**, for the Tekla macro catalog;
5. compiles that merged file as well, so a broken merge cannot reach the
   catalog.

Expected output:

```
Tekla  : C:\TeklaStructures\2025.0
csc    : C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe
Building WF_Rules.dll ...
  OK  WF_Rules.dll  (16384 bytes)
Building P142_WF_AutoDimension.dll ...
  OK  P142_WF_AutoDimension.dll  (27136 bytes)
Generating P142_WF_AutoDimension_merged.cs ...
  OK  P142_WF_AutoDimension_merged.cs  (94073 bytes)
Verifying the merged source compiles ...
  OK  P142_WF_AutoDimension_merged.dll  (38400 bytes)
BUILD OK
```

> Use `powershell -NoProfile -File ...`. The `-ExecutionPolicy Bypass`
> form is not needed for a script that is run, not imported, and some
> security policies block the flag itself.

## Build the package

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package
```

This writes `package/WallFramingAutoDimension/` and zips it into
`package/WallFramingAutoDimension.zip`.  The package holds the merged
source, the icon, both sources, the build script, the installer and the
documentation.

The package is self-contained: `source\build.ps1` rebuilds the macro from
the two sources in the package, and the result is byte-identical to the
merged file that was shipped.

## Deploy to the macro catalog (optional)

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Deploy
```

This copies

* `P142_WF_AutoDimension_merged.cs` → `<Tekla>\Environments\common\macros\drawings\P142_WF_AutoDimension.cs`
* an icon → `…\P142_WF_AutoDimension.bmp`

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
<venv>\Scripts\python.exe test_target_selection.py :: which drawing the macro works on
<venv>\Scripts\python.exe cmp_chains.py            :: [50] vs [57], needs Tekla
<venv>\Scripts\python.exe test_marks.py            :: the mark placement, needs Tekla
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
| a compile error in the merged file | the rules and the macro drifted apart | the build compiles the merged file — fix the source, never the merged file (it is generated) |
