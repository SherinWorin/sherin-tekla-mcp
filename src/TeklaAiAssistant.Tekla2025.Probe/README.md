# Tekla 2025 probe

This is the first Windows-side integration project. It references the pinned
Trimble NuGet package and the gacless binding-redirect task, but intentionally
contains no Tekla calls until the project is built on Windows and the package
surface is checked against the installed Tekla build.

The probe must be run as a Windows executable while Tekla Structures is open.
Do not run it against a Linux process or commit restored packages/DLLs.
