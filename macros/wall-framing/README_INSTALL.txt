================================================================================
P142 TEKLA STRUCTURES 2025 DRAWING MACROS  -  INSTALLATION ON A NEW LAPTOP
Package date: 2026-10-26
================================================================================

WHAT IS IN THIS PACKAGE
-----------------------
Five drawing macros (Applications > Macros, drawing mode):

  Macro                               Drawing it works on
  ----------------------------------  ------------------------------------------
  P142_POD_DRAWINGS                   any drawing of a pod: asks a new pod name and
                                        makes its empty drawings (-10001, -20001, -80001,
                                        -CPL, -WPL).  Settings: save each reference
                                        drawing's properties ONCE per model as
                                        P142_10001, P142_20001, P142_80001, P142_CPL,
                                        P142_WPL (else 'standard' is used)
  P142_BASE_C_W                       BASE, CEILING, WALL drawing (e.g. BP20M-10001)
                                      - 3 plans built from the pod's own parts,
                                        base / ceiling / wall dimensions,
                                        CEILING_GYPSUM shown
  P142_WF_Isometric                   ISOMETRIC drawing (e.g. BP20M-80001)
  P142_WF_Section                     SECTIONAL ELEVATION (-20001): on an empty
                                        drawing, one view per S mark of the base
                                        plan (S1..S4 + extras); else READ-ONLY study
  P142_WF_AutoDimension               WALL FRAMING LAYOUT drawing (one block)
  WallFramingAutoDimensionPro-AIO     GA sheet with many wall framing layouts

  install.cmd            the installer - Command Prompt (all five macros)
  install.ps1            the same installer for PowerShell (optional)
  README_INSTALL.txt     this file
  INSTALL_NOTES.txt      details of the wall framing macros (licence, editions)
  source\                what gets installed:
     *.cs  + *.bmp         the 5 macro LOADERS + icons  -> Tekla macro folder
     *.dll                 the macro logic (modules)   -> %LOCALAPPDATA%\P142\WallFraming
     WF_Settings.json      wall framing settings        -> same folder (kept if present)
     WF_Isometric_Reference_v2.txt  isometric reference -> same folder (kept if present)

How it works: each .cs in the macro catalog is a small loader.  On every run it
loads its DLL from %LOCALAPPDATA%\P142\WallFraming, so a new DLL is picked up
without restarting Tekla.


REQUIREMENTS
------------
  * Windows, Tekla Structures 2025 installed
  * Command Prompt (PowerShell is NOT needed)
  * The P142 model (the macros read the model and the reference drawings in it,
    e.g. the twin pod's BASE, CEILING, WALL drawing and [118] BP02M-80001)


INSTALL  -  Command Prompt (Windows)   [works where PowerShell is blocked]
-----------------------------------------------------------------------
1. Close Tekla Structures.
2. Unzip the package, e.g. to  C:\P142_Tekla_Macros
3. Open Command Prompt (Windows) and run:

     cd /d C:\P142_Tekla_Macros\P142_Tekla_Macros_2026-10-26
     install.cmd

   Tekla installed somewhere else:
     install.cmd "D:\TeklaStructures\2025.0"
   "Access is denied": open Command Prompt with "Run as administrator".

   OPTION - macros in your own folder (e.g. inside the model folder) instead
   of the Tekla environment catalog:
     install.cmd "" "D:\Models\P142\macros"
   It creates  macros\drawings  and  macros\modeling.  Then add that folder to
   the advanced option XS_MACRO_DIRECTORY (keep the existing value, append
   ;D:\Models\P142\macros ).  Do not keep a second copy of the same macro in
   the Tekla catalog, or it shows twice.
4. Start Tekla, open a drawing: Applications > Macros - the five macros are
   listed.  The first run of each macro compiles its loader (a few seconds).

ALTERNATIVE  -  PowerShell (Windows), only where PowerShell is allowed
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -WhatIfOnly
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   (finds the Tekla installation itself; -TeklaPath / -MacroFolder as above)


CHECK AFTER INSTALLING
----------------------
  * %LOCALAPPDATA%\P142\WallFraming  holds 10 DLLs:
      WF_Core, WF_Rules, WF_License, WF_Isometric, WF_IsometricRules,
      WF_BaseCW, WF_Section, WF_SectionRules, WF_Pro, WF_Rules_Pro
  * Run P142_BASE_C_W on a FINISHED drawing such as BP02M-10001: it asks for the
    read-only ceiling study - answer NO.  If it got that far, the install works.


LICENCE  -  P142_WF_AutoDimension only
--------------------------------------
P142_WF_AutoDimension's Pro stages need a licence key, and a key is tied to the
computer it was issued for.  On the new laptop it runs the 3-day trial, then the
free edition, until a key is issued for that laptop (WF_KeyGen on the owner's
computer - it is NOT in this package).  See INSTALL_NOTES.txt.
The other four macros - including the AIO macro and the wall plan of
P142_BASE_C_W - need no licence.


SAFETY
------
  * The macros only write into the drawing that is open; issued / frozen
    drawings are refused; they never change the model.
  * P142_BASE_C_W and P142_WF_Section never switch the active drawing.
  * Save the drawing yourself after checking the result.


UNINSTALL
---------
Delete the five .cs and .bmp files from the macro folder
(<Tekla>\Environments\common\macros\drawings, or your -MacroFolder\drawings)
and the folder %LOCALAPPDATA%\P142\WallFraming.


BUILT AND CHECKED
-----------------
All 14 assemblies were compiled from the sources of 2026-10-26 against the
Tekla Structures 2025 DLLs (zero errors), and an automatic check confirmed that
every type / method / field each loader looks up exists with the right
signature, that WF_Pro.dll matches the rules (API version 3), and that all 205
member references of WF_Pro / WF_Rules_Pro resolve.  Section rule tests: 17/17.
To rebuild with Microsoft's compiler on Windows: source\build.ps1 in the GitHub
repository (github.com/SherinWorin/sherin-tekla-mcp, macros/wall-framing).
