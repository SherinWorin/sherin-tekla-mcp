WALL FRAMING AUTO-DIMENSION MACRO
==================================
Package for Tekla Structures 2025


READ THIS FIRST
---------------
INSTALL_NOTES.txt - the step by step installation, for the PowerShell method
and for the manual method, with the first-run checklist and the
troubleshooting.


WHAT THIS IS
------------
A macro that draws the wall framing dimensions of a "WALL FRAMING LAYOUT"
GA drawing, the way they are drawn in the issued reference drawing of the
P142 COREL REEF project.

The macro does NOT work on a fixed drawing number.  It finds the drawing by
its TITLE, so it works on every computer of the project, whatever mark the
wall layout drawing happens to have there.

  * it works on the drawing that is open, when that drawing is a wall layout
  * otherwise it activates the wall layout drawing of the model
  * it NEVER writes into a drawing that is issued or frozen
    (the issued reference drawing of the project is frozen, so it is safe)


TWO EDITIONS
------------
  FREE  the dimension chains: the stud chain of every wall panel and the
        band chains of the layout.

  PRO   everything: the chains, the top outline, the overall dimensions,
        the north run, the wall panel marks, the section marks and the
        notes.  A licence key unlocks it.

Without a key the Pro features are free for THREE DAYS, from the first run
of the macro on that computer.  To use the Pro edition after that, the key
goes into this file:

    %LOCALAPPDATA%\P142\WallFraming\license.txt

The whole licence workflow is in docs\LICENSING.md.


THE SECOND MACRO - "WallFramingAutoDimensionPro-AIO"
----------------------------------------------------
  A GA sheet does not always carry ONE wall framing layout.  The sheet of
  the client that holds the layout of 19 blocks side by side is such a
  sheet.  The macro above works on one layout, so on that sheet the top
  and the bottom chain cross every block.

  The AIO macro is on the ribbon as well:

      Applications > Macros > WallFramingAutoDimensionPro-AIO

  It asks for NO LICENCE KEY, and it draws the whole set of annotations.
  It groups the panels of the sheet into their blocks (the part of the
  assembly position before the first dash, BP1-WP3 -> BP1) and dimensions
  every group with the box of its own panels, so the chain of one block
  never runs into the next one.


INSTALLING (2 minutes, on the other computer)
---------------------------------------------
1. Unzip the package into any folder, e.g.  C:\Temp\WallFramingAutoDimension

2. Open PowerShell in that folder and run:

       powershell -NoProfile -File .\install.ps1

   The script finds the Tekla Structures installation by itself.  If it does
   not find it, give the path yourself:

       powershell -NoProfile -File .\install.ps1 -TeklaPath "C:\TeklaStructures\2025.0"

   The script copies two things:
     - the loaders and the icons into the macro catalog of Tekla
       (P142_WF_AutoDimension and WallFramingAutoDimensionPro-AIO)
     - the modules into  %LOCALAPPDATA%\P142\WallFraming\

3. Start Tekla Structures 2025 and open a drawing whose title holds
   "WALL FRAMING LAYOUT" (Applications &gt; Macros &gt; P142_WF_AutoDimension).

There is no DLL to register and no installer to run.  The loader in the
macro catalog is a thin file: it holds no rule and no drawing logic, it
loads the modules next to it at run time.

The installer puts the loader into the catalog under the name
P142_WF_AutoDimension.cs, so that is the name of the macro on the ribbon.
The second loader, WallFramingAutoDimensionPro-AIO.cs, is the AIO macro.


IF THE MACRO CATALOG DOES NOT SHOW THE MACRO
---------------------------------------------
The catalog is re-read when Tekla starts.  If the macro does not appear,
close Tekla, start it again, and look under
  Applications &gt; Macros &gt; the group of the drawings macro
The name of the macro is the name of the .cs file: P142_WF_AutoDimension
The second one is WallFramingAutoDimensionPro-AIO

If the macro appears but says that a module is missing, the modules were
not copied into  %LOCALAPPDATA%\P142\WallFraming\  - run install.ps1 again.


REBUILDING THE MACRO FROM THE SOURCES (optional)
-------------------------------------------------
The source folder holds the sources and the build script.  To build it
again (for example after a change):

       powershell -NoProfile -File .\source\build.ps1

The build script finds both the Tekla installation and the C# compiler of
Visual Studio by itself.  It needs the Visual Studio Build Tools (or Visual
Studio 2022) for the compiler.


WHAT THE MACRO WRITES
---------------------
  * the stud spacing chain of every wall panel
  * the band chains of the bottom, left and right edge of the layout
  * the top outline of the layout (the staircase of the upper wall boundary)
  * the two overall dimensions of the layout
  * the north run (the northern wall, measured to the end of the layout)
  * the wall panel marks (WP1, WP2, ...)
  * the section marks S1..S5, copied from the reference drawing
  * the three notes

The first two are the free edition; the rest is the Pro edition.

A re-run replaces the output of the previous run: every object carries the
user property P142_WF_AUTODIM = 1, and only objects with that property are
deleted.  Hand-made annotations are never touched.


THE MODEL
---------
The macro reads the wall parts of the model and groups them into panels by
the report property ASSEMBLY_POS.  The panel prefix of the P142 project is
BP4-WP; when another prefix is used, the panels are found automatically (a
wall assembly is a group of parts with a bottom track, a top track, a stud
and a gypsum board), and the log says so.

Every rule is written down in docs\WALL_FRAMING_RULES.md.


WHERE THE LOG IS
----------------
The log of a run is written next to the macro file, or in the temporary
folder when the macro was started from the catalog.  The name of the file is
wfdim_log_<mark>.txt.  It lists every object that was created and every
warning.
