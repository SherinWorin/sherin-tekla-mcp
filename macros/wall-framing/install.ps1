# Installs the P142 drawing macros into Tekla Structures 2025 on THIS computer:
#   P142_WF_AutoDimension, WallFramingAutoDimensionPro-AIO, P142_WF_Isometric,
#   P142_BASE_C_W, P142_WF_Section.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -TeklaPath "C:\TeklaStructures\2025.0"
#   powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -MacroFolder "D:\Models\P142\macros"
#   powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -WhatIfOnly
#
# -MacroFolder puts the loaders in <MacroFolder>\drawings instead of the Tekla
# environment catalog; add that folder to XS_MACRO_DIRECTORY yourself.
#
# What it does:
#   1. finds the Tekla Structures installation (no hard coded path)
#   2. checks that it is a 2025 installation
#   3. copies the LOADER source\P142_WF_AutoDimension.cs and its icon
#         -> <Tekla>\Environments\common\macros\drawings\
#   4. copies the modules
#         -> %LOCALAPPDATA%\P142\WallFraming\
#
# The macro in the catalog is only a loader: it holds no rule and no drawing
# logic.  The logic is in the modules, which are loaded at run time.  That is
# what keeps the Pro stages out of the published repository.
#
# The Pro modules (WF_Pro.dll) are installed too, when they are found next to
# the loader - so that a licence that is later upgraded to Pro starts working
# without a new installation.
param(
    [string]$TeklaPath = '',
    [string]$MacroFolder = '',
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $here 'source'
$appFolder = Join-Path $env:LOCALAPPDATA 'P142\WallFraming'

function Find-TeklaPath {
    param([string]$Given)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Given) { $candidates.Add($Given) }
    foreach ($v in @('TEKLA_HOME', 'TEKLA_STRUCTURES_HOME', 'TeklaStructuresHome')) {
        $e = [Environment]::GetEnvironmentVariable($v)
        if ($e) { $candidates.Add($e) }
    }
    $roots = @('C:\TeklaStructures', 'C:\Program Files\Tekla Structures',
               'C:\Program Files (x86)\Tekla Structures')
    foreach ($d in (Get-PSDrive -PSProvider FileSystem)) {
        $roots += (Join-Path $d.Root 'TeklaStructures')
        $roots += (Join-Path $d.Root 'Program Files\Tekla Structures')
    }
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        foreach ($v in (Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue)) {
            $candidates.Add($v.FullName)
        }
    }
    $good = @()
    foreach ($c in $candidates) {
        if (-not $c) { continue }
        if (Test-Path (Join-Path $c 'bin\Tekla.Structures.Drawing.dll')) { $good += $c }
    }
    if ($good.Count -eq 0) { return $null }
    $v2025 = $good | Where-Object { (Split-Path -Leaf $_) -like '2025*' }
    if ($v2025) { return ($v2025 | Sort-Object -Descending | Select-Object -First 1) }
    return ($good | Sort-Object -Descending | Select-Object -First 1)
}

$tekla = Find-TeklaPath -Given $TeklaPath
if (-not $tekla) {
    Write-Output 'ERROR: no Tekla Structures installation was found.'
    Write-Output '       Run it again with the path, for example:'
    Write-Output '       powershell -NoProfile -File install.ps1 -TeklaPath "C:\TeklaStructures\2025.0"'
    exit 1
}

Write-Output ("Tekla installation : {0}" -f $tekla)
if ((Split-Path -Leaf $tekla) -notlike '2025*') {
    Write-Warning ("This macro is written for Tekla Structures 2025, but {0} was found." -f $tekla)
    Write-Warning 'Press Ctrl+C to stop, or wait 5 seconds to continue anyway.'
    Start-Sleep -Seconds 5
}

$catalog = Join-Path $tekla 'Environments\common\macros\drawings'
if ($MacroFolder) {
    # a macro folder of your own (e.g. in the model folder): Tekla wants modeling + drawings in it
    $catalog = Join-Path $MacroFolder 'drawings'
    if (-not $WhatIfOnly) {
        New-Item -ItemType Directory -Force -Path $catalog, (Join-Path $MacroFolder 'modeling') | Out-Null
    }
    Write-Output ("Macro folder       : {0}  (add {1} to XS_MACRO_DIRECTORY)" -f $catalog, $MacroFolder)
}
if (-not (Test-Path $catalog) -and -not $WhatIfOnly) {
    Write-Output ("ERROR: the macro catalog was not found: {0}" -f $catalog)
    exit 1
}

# ---------------------------------------------------------------------------
# The files that go into the macro catalog: the loader and its icon.
#
# They are looked for in a few places, so that the same script works in the
# deployment package and in the source tree:
#   source\            - the layout of the package
#   source\bin\        - the layout of the source tree, after a build
# ---------------------------------------------------------------------------
$catalogFiles = @(
    @{ Name = 'P142_WF_AutoDimension.cs'; To = (Join-Path $catalog 'P142_WF_AutoDimension.cs') },
    @{ Name = 'P142_WF_AutoDimension.bmp'; To = (Join-Path $catalog 'P142_WF_AutoDimension.bmp') },
    @{ Name = 'WallFramingAutoDimensionPro-AIO.cs'; To = (Join-Path $catalog 'WallFramingAutoDimensionPro-AIO.cs') },
    @{ Name = 'WallFramingAutoDimensionPro-AIO.bmp'; To = (Join-Path $catalog 'WallFramingAutoDimensionPro-AIO.bmp') },
    @{ Name = 'P142_WF_Isometric.cs'; To = (Join-Path $catalog 'P142_WF_Isometric.cs') },
    @{ Name = 'P142_WF_Isometric.bmp'; To = (Join-Path $catalog 'P142_WF_Isometric.bmp') },
    @{ Name = 'P142_BASE_C_W.cs'; To = (Join-Path $catalog 'P142_BASE_C_W.cs') },
    @{ Name = 'P142_BASE_C_W.bmp'; To = (Join-Path $catalog 'P142_BASE_C_W.bmp') },
    @{ Name = 'P142_WF_Section.cs'; To = (Join-Path $catalog 'P142_WF_Section.cs') },
    @{ Name = 'P142_WF_Section.bmp'; To = (Join-Path $catalog 'P142_WF_Section.bmp') }
)

foreach ($f in $catalogFiles) {
    $f.From = $null
    foreach ($dir in @($src, (Join-Path $src 'bin'), (Join-Path $here 'bin'))) {
        $cand = Join-Path $dir $f.Name
        if (Test-Path $cand) { $f.From = $cand; break }
    }
    if (-not $f.From) {
        Write-Output ("ERROR: the package is incomplete, this file is missing: {0}" -f $f.Name)
        Write-Output ("       looked in {0}, {0}\bin and {1}\bin" -f $src, $here)
        Write-Output '       Build the package:  cd source  &&  powershell -NoProfile -File build.ps1 -Package'
        exit 1
    }
}

# The file that goes into the catalog must be the LOADER.  The loader has no
# rule in it, and it loads the modules at run time.  A file that holds the
# whole logic (namespace P142.WallFraming) is the old merged source, and it
# would put the Pro stages into the public repository.
#
# Every .cs of the catalog is checked: there are two loaders, the one of the
# licensed macro and the one of the free AIO macro.
foreach ($f in $catalogFiles) {
    if ($f.Name -notlike '*.cs') { continue }
    $loader = $f.From
    $text = Get-Content $loader -Raw
    if ($text -match 'namespace\s+P142\.WallFraming') {
        Write-Output ("ERROR: {0} is the merged source, not the loader." -f $loader)
        Write-Output '       Only the loader goes into the macro catalog.  The logic'
        Write-Output '       is in the modules, which are installed next to it in'
        Write-Output ("       {0}" -f $appFolder)
        Write-Output '       Build the package:  cd source  &&  powershell -NoProfile -File build.ps1 -Package'
        exit 1
    }
    if ($text -notmatch 'MacroEntryPoint') {
        Write-Output ("ERROR: {0} has no macro entry point - it is not a macro." -f $loader)
        exit 1
    }
}

# ---------------------------------------------------------------------------
# The modules, which are installed in the app folder next to the licence.
#
# WF_Pro.dll is the Pro edition.  It is installed when it is found, so that a
# licence that is upgraded to Pro starts working without a new installation.
# WF_Rules_Pro.dll is the Pro rules; it goes with it.  The two belong to the
# same build as the three free modules, so a set that is mixed is refused by
# the version handshake of the macro.
# ---------------------------------------------------------------------------
$moduleFiles = @('WF_Core.dll', 'WF_Rules.dll', 'WF_License.dll',
                  'WF_Isometric.dll', 'WF_IsometricRules.dll',
                  'WF_BaseCW.dll', 'WF_Section.dll', 'WF_SectionRules.dll',
                  'WF_Pro.dll', 'WF_Rules_Pro.dll')
$proModules = @('WF_Pro.dll', 'WF_Rules_Pro.dll')

$modules = @()
$proFound = @()
foreach ($name in $moduleFiles) {
    $from = $null
    foreach ($dir in @($src, (Join-Path $src 'bin'), (Join-Path $here 'bin'))) {
        $cand = Join-Path $dir $name
        if (Test-Path $cand) { $from = $cand; break }
    }
    if (-not $from) {
        if ($proModules -contains $name) {
            Write-Output ("  --  {0} is not here: the free edition is installed." -f $name)
            continue
        }
        Write-Output ("ERROR: the package is incomplete, this module is missing: {0}" -f $name)
        Write-Output '       Build it:  cd source  &&  powershell -NoProfile -File build.ps1'
        exit 1
    }
    if ($proModules -contains $name) { $proFound += $name }
    $modules += @{ Name = $name; From = $from; To = (Join-Path $appFolder $name) }
}

# A half-Pro installation is a mixed set: the macro then says that the
# modules are of different versions.
if ($proFound.Count -ne 0 -and $proFound.Count -ne $proModules.Count) {
    Write-Output ("WARNING: only {0} of the Pro modules is in the package ({1})." -f
        $proFound.Count, ($proFound -join ', '))
    Write-Output '         Copy the package again, complete, so that the five modules are of one build.'
}

if ($WhatIfOnly) {
    foreach ($f in $catalogFiles) { Write-Output ("would copy {0} -> {1}" -f $f.From, $f.To) }
    foreach ($f in $modules)   { Write-Output ("would copy {0} -> {1}" -f $f.From, $f.To) }
    exit 0
}

foreach ($f in $catalogFiles) {
    Copy-Item -Path $f.From -Destination $f.To -Force
    Write-Output ("  OK  {0}" -f $f.To)
}

if (-not (Test-Path $appFolder)) {
    New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
}
foreach ($f in $modules) {
    Copy-Item -Path $f.From -Destination $f.To -Force
    Write-Output ("  OK  {0}" -f $f.To)
}

# Data files: installed only when missing, so your own settings and the
# isometric reference you captured are never overwritten.
foreach ($name in @('WF_Settings.json', 'WF_Isometric_Reference_v2.txt')) {
    $to = Join-Path $appFolder $name
    $from = $null
    foreach ($dir in @($src, (Join-Path $src 'bin'), (Join-Path $here 'bin'))) {
        if (Test-Path (Join-Path $dir $name)) { $from = Join-Path $dir $name; break }
    }
    if (-not $from) { Write-Output ("  --  {0} is not in the package" -f $name); continue }
    if (Test-Path $to) { Write-Output ("  --  {0} kept (already there)" -f $to); continue }
    Copy-Item -Path $from -Destination $to
    Write-Output ("  OK  {0}" -f $to)
}

Write-Output ''
Write-Output 'The P142 macros are installed:'
Write-Output '  P142_BASE_C_W     - BASE, CEILING, WALL drawing (-10001)'
Write-Output '  P142_WF_Isometric - isometric drawing (-80001)'
Write-Output '  P142_WF_Section   - sectional elevation (-20001; study mode for now)'
Write-Output 'Restart Tekla Structures if it was running.'
Write-Output ''
Write-Output 'The wall framing macro is on the ribbon:'
Write-Output '  Applications > Macros > P142_WF_AutoDimension'
Write-Output ''
Write-Output 'There is a second macro on the ribbon, which asks for no licence:'
Write-Output '  Applications > Macros > WallFramingAutoDimensionPro-AIO'
Write-Output 'It dimensions EVERY wall framing layout of a GA sheet on its own, so'
Write-Output 'that the chain of one block does not run into the next one.'
Write-Output ''
Write-Output 'Open the GA drawing whose title holds "WALL FRAMING LAYOUT" and run it.'
Write-Output 'The macro refuses to write into a drawing that is issued or frozen.'
Write-Output ''
Write-Output ("The licence is read from: {0}" -f (Join-Path $appFolder 'license.txt'))
Write-Output 'Without a licence the free edition runs, with a three day trial.'
