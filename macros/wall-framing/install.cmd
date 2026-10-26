@echo off
rem Installs the five P142 drawing macros - Command Prompt only, no PowerShell.
rem   install.cmd                                   default Tekla path C:\TeklaStructures\2025.0
rem   install.cmd "D:\TeklaStructures\2025.0"       another Tekla path
rem   install.cmd "" "D:\Models\P142\macros"        loaders into your own macro folder
rem                                                 (then add it to XS_MACRO_DIRECTORY)
setlocal
set "HERE=%~dp0"
set "SRC=%HERE%source"
set "TEKLA=%~1"
if "%TEKLA%"=="" set "TEKLA=C:\TeklaStructures\2025.0"
set "APP=%LOCALAPPDATA%\P142\WallFraming"

if "%~2"=="" (
    if not exist "%TEKLA%\bin\Tekla.Structures.Drawing.dll" (
        echo ERROR: Tekla Structures 2025 not found in "%TEKLA%".
        echo        Run:  install.cmd "C:\path\to\TeklaStructures\2025.0"
        exit /b 1
    )
    set "MACROS=%TEKLA%\Environments\common\macros\drawings"
) else (
    set "MACROS=%~2\drawings"
    if not exist "%~2\modeling" mkdir "%~2\modeling"
)
if not exist "%SRC%\P142_BASE_C_W.cs" ( echo ERROR: run install.cmd from the unzipped package folder. & exit /b 1 )

if not exist "%MACROS%" mkdir "%MACROS%"
if not exist "%APP%" mkdir "%APP%"

echo Macro loaders + icons  -^> %MACROS%
copy /Y "%SRC%\*.cs"  "%MACROS%\" >nul || goto :fail
copy /Y "%SRC%\*.bmp" "%MACROS%\" >nul || goto :fail
echo Macro modules (DLLs)   -^> %APP%
copy /Y "%SRC%\*.dll" "%APP%\" >nul || goto :fail
rem data files: only when missing, so your own settings are never overwritten
for %%F in (WF_Settings.json WF_Isometric_Reference_v2.txt) do (
    if not exist "%APP%\%%F" ( copy /Y "%SRC%\%%F" "%APP%\" >nul ) else ( echo   kept existing %%F )
)
echo.
echo Installed: P142_BASE_C_W, P142_WF_Isometric, P142_WF_Section,
echo            P142_WF_AutoDimension, WallFramingAutoDimensionPro-AIO, P142_POD_DRAWINGS
if not "%~2"=="" echo Add "%~2" to the advanced option XS_MACRO_DIRECTORY.
echo Restart Tekla Structures, open a drawing: Applications ^> Macros.
exit /b 0

:fail
echo ERROR: copy failed. If it says "Access is denied", run Command Prompt as administrator.
exit /b 1
