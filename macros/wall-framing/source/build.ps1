# Builds the wall-framing macro for Tekla Structures 2025.
#
#   source/bin/WF_Rules.dll              - the FREE annotation rules, no Tekla dependency
#   source/bin/WF_License.dll            - the licence engine, no Tekla dependency
#   source/bin/WF_Core.dll               - the shared drawing machinery (needs Tekla)
#   source/bin/WF_Pro.dll                - the PRO stages (NOT in the public repository;
#                                          built only when the private sources are found)
#   source/bin/WF_KeyGen.exe             - the owner's licence key tool
#   source/bin/P142_WF_AutoDimension.dll - the LOADER, the file the Tekla macro
#                                          catalog compiles
#   source/bin/WallFramingAutoDimensionPro-AIO.dll
#                                        - the LOADER of the AIO macro: it
#                                          dimensions EVERY wall framing
#                                          layout of a GA sheet on its own,
#                                          and it is free - it asks for no
#                                          licence key
#   source/bin/WF_IsometricRules.dll     - the ISOMETRIC VIEW rules, no Tekla dependency
#   source/bin/WF_Isometric.dll          - the ISOMETRIC VIEW machinery (needs Tekla)
#   source/bin/P142_WF_Isometric.dll     - the LOADER for ISOMETRIC VIEW macro
#   source/bin/WF_SectionRules.dll       - the SECTIONAL ELEVATION rules, no Tekla dependency
#   source/bin/WF_Section.dll            - the SECTIONAL ELEVATION machinery (needs Tekla)
#   source/bin/P142_WF_Section.dll       - the LOADER for SECTIONAL ELEVATION macro
#
# The loader is thin: it holds no rule and no drawing logic.  Everything is
# in the assemblies above, which are installed in
#   %LOCALAPPDATA%\P142\WallFraming
# and are loaded at run time.  The Pro stages are in a separate assembly, so
# that the published repository does not hold them.
#
# Usage (Command Prompt (Windows)):
#   powershell -NoProfile -File source/build.ps1
#   powershell -NoProfile -File source/build.ps1 -Package
#   powershell -NoProfile -File source/build.ps1 -Package -AioPackage
#                                       (also the package of the AIO macro)
#   powershell -NoProfile -File source/build.ps1 -Deploy    (also copies to the macro catalog)
#   powershell -NoProfile -File source/build.ps1 -ProPath "C:\path\to\private"
param(
    [switch]$Deploy,
    [switch]$Package,
    [switch]$AioFree,
    [switch]$AioPackage,
    [string]$TeklaPath = '',
    [string]$CscPath = '',
    [string]$ProPath = ''
)

$ErrorActionPreference = 'Stop'

$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir  = Join-Path $here 'bin'

# ---------------------------------------------------------------------------
# Find the Tekla Structures installation.
#
# The path is NOT hard coded, so that the same build runs on every computer
# of the project.  The candidates are looked for in this order:
#   1. the -TeklaPath argument
#   2. the TEKLA_HOME / TEKLA_STRUCTURES_HOME environment variable
#   3. C:\TeklaStructures\<version> and the other drives
#   4. C:\Program Files\Tekla Structures\<version>
# A 2025 installation is preferred over any other one.
# ---------------------------------------------------------------------------
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

    # a 2025 installation first, then the highest version
    $v2025 = $good | Where-Object { (Split-Path -Leaf $_) -like '2025*' }
    if ($v2025) { return ($v2025 | Sort-Object -Descending | Select-Object -First 1) }
    return ($good | Sort-Object -Descending | Select-Object -First 1)
}

# ---------------------------------------------------------------------------
# Find the C# compiler of Visual Studio.
#
# Every edition is looked for, so that the build works with Community,
# Professional, Enterprise or the Build Tools - and on a computer that has
# only the Build Tools installed.
# ---------------------------------------------------------------------------
function Find-CscPath {
    param([string]$Given)

    if ($Given) { if (Test-Path $Given) { return $Given } }

    $roots = @('C:\Program Files\Microsoft Visual Studio',
               'C:\Program Files (x86)\Microsoft Visual Studio')
    $found = @()
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $found += Get-ChildItem -Path $r -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
                  Where-Object { $_.FullName -like '*Roslyn*' } |
                  Select-Object -ExpandProperty FullName
    }
    if ($found.Count -eq 0) { return $null }
    # the newest Roslyn first
    return ($found | Sort-Object -Descending | Select-Object -First 1)
}

# ---------------------------------------------------------------------------
# Find the PRIVATE Pro sources.
#
# The Pro stages are not in the published repository.  They are looked for
# in a few places, so that the owner can build the Pro edition from the
# dev tree, and so that a customer can still build the FREE edition.
# ---------------------------------------------------------------------------
function Find-ProPath {
    param([string]$Given)

    if ($Given) { if (Test-Path $Given) { return $Given } return $null }
    $candidates = @(
        (Join-Path (Split-Path -Parent $here) 'private'),
        (Join-Path $here 'private'),
        (Join-Path $env:LOCALAPPDATA 'P142\WallFraming\pro-src')
    )
    foreach ($c in $candidates) {
        if (-not $c) { continue }
        if (Test-Path (Join-Path $c 'WF_Pro.cs')) { return $c }
    }
    return $null
}

$tekla = Find-TeklaPath -Given $TeklaPath
if (-not $tekla) {
    throw 'No Tekla Structures installation was found. ' +
          'Give the folder with the -TeklaPath argument, e.g. -TeklaPath "C:\TeklaStructures\2025.0"'
}
$dllDir  = Join-Path $tekla 'bin'
$catalog = Join-Path $tekla 'Environments\common\macros\drawings'
$fw      = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

$csc = Find-CscPath -Given $CscPath
if (-not $csc) {
    throw 'The C# compiler (csc.exe) was not found. ' +
          'Install the Visual Studio Build Tools, or give the path with -CscPath.'
}

$proPath = Find-ProPath -Given $ProPath

Write-Output ("Tekla  : {0}" -f $tekla)
Write-Output ("csc    : {0}" -f $csc)
if ((Split-Path -Leaf $tekla) -notlike '2025*') {
    Write-Warning ("This macro is written for Tekla Structures 2025, but {0} was found." -f $tekla)
}
Write-Output ("Pro    : {0}" -f $(if ($proPath) { $proPath } else { '(not found - the free edition only)' }))

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# ---------------------------------------------------------------------------
# The macro catalog shows an icon next to the macro.  One is drawn when there
# is none yet: a blue square with a white dimension cross in it.  The AIO
# macro gets a green one, so that the two are told apart in the list.
# ---------------------------------------------------------------------------
function New-IconFile {
    param([string]$Path, [switch]$Force, [int]$Red = 0, [int]$Green = 90, [int]$Blue = 120)
    if ((Test-Path $Path) -and (-not $Force)) { return }
    Add-Type -AssemblyName System.Drawing
    $img = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($img)
    $g.Clear([System.Drawing.Color]::FromArgb(0, $Red, $Green, $Blue))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2
    $g.DrawRectangle($pen, 5, 5, 21, 21)
    $g.DrawLine($pen, 5, 16, 26, 16)
    $g.DrawLine($pen, 16, 5, 16, 26)
    $g.Dispose()
    $img.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $img.Dispose()
}

$framework = @(
    (Join-Path $fw 'mscorlib.dll'),
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Management.dll'),
    (Join-Path $fw 'System.Web.Extensions.dll')
)
$teklaDlls = @(
    (Join-Path $dllDir 'Tekla.Structures.dll'),
    (Join-Path $dllDir 'Tekla.Structures.Drawing.dll'),
    (Join-Path $dllDir 'Tekla.Structures.Model.dll'),
    (Join-Path $dllDir 'Tekla.Structures.Geometry3d.Compatibility.dll'),
    (Join-Path $dllDir 'Tekla.Macros.Runtime.dll')
)
foreach ($f in $framework + $teklaDlls) {
    if (-not (Test-Path $f)) { throw "MISSING REFERENCE: $f" }
}
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

function Invoke-Csc {
    param([string[]]$Refs, [string[]]$Source, [string]$Out, [string]$Target = 'library')
    $args = @('-nologo', "-target:$Target", "-out:$Out", '-nostdlib+', '-define:TRACE')
    foreach ($r in $Refs) { $args += "-r:`"$r`"" }
    foreach ($s in $Source) { $args += "`"$s`"" }
    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $Source (exit $LASTEXITCODE)" }
    Write-Output ("  OK  {0}  ({1} bytes)" -f (Split-Path -Leaf $Out), (Get-Item $Out).Length)
}

# ---------------------------------------------------------------------------
# Remove the artefacts of the OLD layout.
#
# Before the two editions, the build also wrote a "merged" source - the
# macro and the rules in one file, so that the Tekla macro catalog could
# compile it without a second DLL.  That file carried the Pro stages in
# plain text, so it must never be installed, and it must never be left
# lying in the build output where somebody could pick it up.  The loader
# replaced it; this removes the leftovers.
# ---------------------------------------------------------------------------
$stale = @(
    (Join-Path $outDir 'P142_WF_AutoDimension_merged.cs'),
    (Join-Path $outDir 'P142_WF_AutoDimension_merged.dll'),
    (Join-Path $outDir 'P142_WF_AutoDimension_merged.bmp')
)
foreach ($f in $stale) {
    if (Test-Path $f) {
        Remove-Item $f -Force
        Write-Output ("  removed the old artefact {0}" -f (Split-Path -Leaf $f))
    }
}

# ---------------------------------------------------------------------------
# The build.
# ---------------------------------------------------------------------------
Write-Output 'Building WF_Rules.dll ...'
Invoke-Csc -Refs $framework -Source (Join-Path $here 'WF_Rules.cs') -Out (Join-Path $outDir 'WF_Rules.dll')

Write-Output 'Building WF_License.dll ...'
Invoke-Csc -Refs $framework -Source (Join-Path $here 'WF_License.cs') -Out (Join-Path $outDir 'WF_License.dll')

Write-Output 'Building WF_Core.dll ...'
Invoke-Csc -Refs ($framework + $teklaDlls +
            @((Join-Path $outDir 'WF_Rules.dll'), (Join-Path $outDir 'WF_License.dll'))) `
            -Source @((Join-Path $here 'WF_Settings.cs'), (Join-Path $here 'WF_Core.cs')) `
            -Out (Join-Path $outDir 'WF_Core.dll')

Write-Output 'Building WF_KeyGen.exe ...'
Invoke-Csc -Refs $framework -Target 'exe' `
            -Source @((Join-Path $here 'WF_License.cs'), (Join-Path $here 'WF_KeyGen.cs')) `
            -Out (Join-Path $outDir 'WF_KeyGen.exe')

Write-Output 'Building P142_WF_AutoDimension.dll (the loader) ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source (Join-Path $here 'P142_WF_AutoDimension.cs') `
            -Out (Join-Path $outDir 'P142_WF_AutoDimension.dll')

Write-Output 'Building WallFramingAutoDimensionPro-AIO.dll (the AIO loader) ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source (Join-Path $here 'WallFramingAutoDimensionPro-AIO.cs') `
            -Out (Join-Path $outDir 'WallFramingAutoDimensionPro-AIO.dll')

# ============================================================
# NEW MACROS: ISOMETRIC VIEW, SECTIONAL ELEVATION, BASE CEILING WALL
# ============================================================

Write-Output 'Building WF_IsometricRules.dll ...'
Invoke-Csc -Refs $framework -Source (Join-Path $here 'WF_IsometricRules.cs') -Out (Join-Path $outDir 'WF_IsometricRules.dll')

Write-Output 'Building WF_Isometric.dll ...'
Invoke-Csc -Refs ($framework + $teklaDlls + @((Join-Path $outDir 'WF_IsometricRules.dll'), (Join-Path $outDir 'WF_Rules.dll'))) `
            -Source (Join-Path $here 'WF_Isometric.cs') `
            -Out (Join-Path $outDir 'WF_Isometric.dll')

Write-Output 'Building P142_WF_Isometric.dll (the ISOMETRIC VIEW loader) ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source (Join-Path $here 'P142_WF_Isometric.cs') `
            -Out (Join-Path $outDir 'P142_WF_Isometric.dll')

Write-Output 'Building WF_SectionRules.dll ...'
Invoke-Csc -Refs $framework -Source (Join-Path $here 'WF_SectionRules.cs') -Out (Join-Path $outDir 'WF_SectionRules.dll')

Write-Output 'Building WF_BaseCW.dll (BASE, CEILING, WALL drawing) ...'
Invoke-Csc -Refs ($framework + $teklaDlls + @((Join-Path $outDir 'WF_Rules.dll'), (Join-Path $outDir 'WF_License.dll'), (Join-Path $outDir 'WF_Core.dll'), (Join-Path $outDir 'WF_Isometric.dll'), (Join-Path $outDir 'WF_IsometricRules.dll'), (Join-Path $outDir 'WF_SectionRules.dll'))) `
            -Source @((Join-Path $here 'WF_BaseCW.cs'), (Join-Path $here 'WF_BfDim.cs'), (Join-Path $here 'WF_CeilStudy.cs'), (Join-Path $here 'WF_CeilDim.cs'), (Join-Path $here 'WF_BcwNotes.cs')) `
            -Out (Join-Path $outDir 'WF_BaseCW.dll')

Write-Output 'Building P142_BASE_C_W.dll (the BASE, CEILING, WALL loader) ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source (Join-Path $here 'P142_BASE_C_W.cs') `
            -Out (Join-Path $outDir 'P142_BASE_C_W.dll')

Write-Output 'Building WF_Section.dll ...'
Invoke-Csc -Refs ($framework + $teklaDlls + @((Join-Path $outDir 'WF_SectionRules.dll'), (Join-Path $outDir 'WF_Rules.dll'), (Join-Path $outDir 'WF_BaseCW.dll'), (Join-Path $outDir 'WF_Isometric.dll'), (Join-Path $outDir 'WF_IsometricRules.dll'))) `
            -Source (Join-Path $here 'WF_Section.cs') `
            -Out (Join-Path $outDir 'WF_Section.dll')

Write-Output 'Building P142_WF_Section.dll (the SECTIONAL ELEVATION loader) ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source (Join-Path $here 'P142_WF_Section.cs') `
            -Out (Join-Path $outDir 'P142_WF_Section.dll')

$proBuilt = $false
if ($proPath) {
    Write-Output 'Building WF_Rules_Pro.dll (the Pro rules, no Tekla dependency) ...'
    Invoke-Csc -Refs ($framework + @((Join-Path $outDir 'WF_Rules.dll'))) `
                -Source (Join-Path $proPath 'WF_Rules_Pro.cs') `
                -Out (Join-Path $outDir 'WF_Rules_Pro.dll')

    Write-Output 'Building WF_Pro.dll (the Pro stages) ...'
    Invoke-Csc -Refs ($framework + $teklaDlls +
                @((Join-Path $outDir 'WF_Rules.dll'), (Join-Path $outDir 'WF_Core.dll'))) `
                -Source @((Join-Path $proPath 'WF_Pro.cs'), (Join-Path $proPath 'WF_Rules_Pro.cs')) `
                -Out (Join-Path $outDir 'WF_Pro.dll')
    $proBuilt = $true
}
else {
    Write-Output 'WF_Pro.dll    : skipped (the private Pro sources were not found)'
}

# The icon of the macro, next to the loader, so that install.ps1 can find it
# in the build output as well.
New-IconFile -Path (Join-Path $outDir 'P142_WF_AutoDimension.bmp') -Force
New-IconFile -Path (Join-Path $outDir 'WallFramingAutoDimensionPro-AIO.bmp') -Force -Red 0 -Green 110 -Blue 60

# Icons for new macros
New-IconFile -Path (Join-Path $outDir 'P142_WF_Isometric.bmp') -Force -Red 180 -Green 100 -Blue 0
New-IconFile -Path (Join-Path $outDir 'P142_WF_Section.bmp') -Force -Red 0 -Green 150 -Blue 180
New-IconFile -Path (Join-Path $outDir 'P142_BASE_C_W.bmp') -Force -Red 200 -Green 40 -Blue 40

# ---------------------------------------------------------------------------
# The packages.
# ---------------------------------------------------------------------------
function New-Zip {
    param([string]$Root, [string]$Zip)
    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    # The zip is written by hand, so that the entries use "/" and not "\".
    # Windows Explorer accepts both, but a zip with backslashes is refused
    # by some other tools - and the package has to open everywhere.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($Zip, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $root = (Resolve-Path $Root).Path.TrimEnd('\') + '\'
            foreach ($f in Get-ChildItem -Path $Root -Recurse -File) {
                $name = $f.FullName.Substring($root.Length).Replace('\', '/')
                $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $es = $entry.Open()
                try {
                    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
                    $es.Write($bytes, 0, $bytes.Length)
                } finally { $es.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    Write-Output ("  OK  {0}  ({1} bytes)" -f $Zip, (Get-Item $Zip).Length)
}

function New-Package {
    param([string]$Name, [string[]]$ExtraFiles)

    $pkgParent = Split-Path -Parent $here
    $pkgRoot = Join-Path $pkgParent ("package\" + $Name)
    $pkgSrc  = Join-Path $pkgRoot 'source'
    if (Test-Path $pkgRoot) { Remove-Item -Path $pkgRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $pkgSrc | Out-Null

    # the loaders, which the Tekla macro catalog compiles
    Copy-Item (Join-Path $here 'P142_WF_AutoDimension.cs') $pkgSrc -Force
    Copy-Item (Join-Path $here 'WallFramingAutoDimensionPro-AIO.cs') $pkgSrc -Force
    Copy-Item (Join-Path $here 'P142_WF_Isometric.cs') $pkgSrc -Force
    Copy-Item (Join-Path $here 'P142_WF_Section.cs') $pkgSrc -Force
    Copy-Item (Join-Path $here 'P142_BASE_C_W.cs') $pkgSrc -Force
    Copy-Item (Join-Path $outDir 'P142_BASE_C_W.bmp') $pkgSrc -Force
    # the icons of the macros
    $bmpOut = Join-Path $pkgSrc 'P142_WF_AutoDimension.bmp'
    New-IconFile -Path $bmpOut -Force
    $bmpAio = Join-Path $pkgSrc 'WallFramingAutoDimensionPro-AIO.bmp'
    New-IconFile -Path $bmpAio -Force -Red 0 -Green 110 -Blue 60
    $bmpIso = Join-Path $pkgSrc 'P142_WF_Isometric.bmp'
    New-IconFile -Path $bmpIso -Force -Red 180 -Green 100 -Blue 0
    $bmpSec = Join-Path $pkgSrc 'P142_WF_Section.bmp'
    New-IconFile -Path $bmpSec -Force -Red 0 -Green 150 -Blue 180
    # the assemblies, which are installed next to the licence
    foreach ($dll in @('WF_Core.dll', 'WF_Rules.dll', 'WF_License.dll',
                       'WF_Isometric.dll', 'WF_IsometricRules.dll',
                       'WF_Section.dll', 'WF_SectionRules.dll',
                       'WF_BaseCW.dll')) {
        Copy-Item (Join-Path $outDir $dll) $pkgSrc -Force
    }
    foreach ($f in $ExtraFiles) {
        if (Test-Path $f) { Copy-Item $f $pkgSrc -Force }
    }
    # the sources and the build script, for a re-build on the other computer
    foreach ($s in @('WF_Core.cs', 'WF_Rules.cs', 'WF_License.cs', 'WF_Settings.cs',
                     'WF_IsometricRules.cs', 'WF_Isometric.cs', 'P142_WF_Isometric.cs',
                     'WF_SectionRules.cs', 'WF_Section.cs', 'P142_WF_Section.cs',
                     'WF_BaseCW.cs', 'WF_BfDim.cs', 'WF_CeilStudy.cs', 'WF_CeilDim.cs', 'WF_BcwNotes.cs', 'P142_BASE_C_W.cs',
                     'P142_WF_AutoDimension.cs', 'WallFramingAutoDimensionPro-AIO.cs',
                     'WF_KeyGen.cs', 'WF_Settings.json', 'build.ps1')) {
        Copy-Item (Join-Path $here $s) $pkgSrc -Force
    }

    # the files of the package root: they sit next to the sources in the
    # development tree, and one level above them in the published tree
    foreach ($doc in @('install.ps1', 'INSTALL_NOTES.txt')) {
        $f = Join-Path $here $doc
        if (-not (Test-Path $f)) { $f = Join-Path $pkgParent $doc }
        if (Test-Path $f) { Copy-Item $f (Join-Path $pkgRoot $doc) -Force }
    }
    # PACKAGE_README.txt (or README.txt) becomes the README.txt of the package
    $readme = $null
    foreach ($cand in @((Join-Path $here 'PACKAGE_README.txt'),
                        (Join-Path $pkgParent 'PACKAGE_README.txt'),
                        (Join-Path $pkgParent 'README.txt'))) {
        if (Test-Path $cand) { $readme = $cand; break }
    }
    if ($readme) { Copy-Item $readme (Join-Path $pkgRoot 'README.txt') -Force }

    # the documentation
    $docs = Join-Path $pkgParent 'docs'
    if (-not (Test-Path $docs)) { $docs = Join-Path (Split-Path -Parent $pkgParent) 'docs' }
    if (Test-Path $docs) {
        $pkgDocs = Join-Path $pkgRoot 'docs'
        New-Item -ItemType Directory -Path $pkgDocs | Out-Null
        Copy-Item (Join-Path $docs '*') $pkgDocs -Recurse -Force
    }

    New-Zip -Root $pkgRoot -Zip (Join-Path $pkgParent ("package\" + $Name + ".zip"))
}

if ($Package) {
    Write-Output 'Assembling the free package ...'
    $freeExtra = @()
    if ($AioFree) {
        # The AIO macro asks for no licence key and draws the whole set of
        # annotations.  For that it needs the Pro modules, which the free
        # package does not carry by default (rule R10: the Pro stages are
        # not published).  This switch is the owner's decision: with it, the
        # free package carries the Pro modules as well, and the AIO macro
        # of a free install is complete.
        if ($proBuilt) {
            $freeExtra = @((Join-Path $outDir 'WF_Pro.dll'),
                           (Join-Path $outDir 'WF_Rules_Pro.dll'))
            Write-Output '  the free package carries the Pro modules: the AIO macro draws everything'
        } else {
            Write-Warning 'The free package cannot carry the Pro modules: they were not built.'
            Write-Warning 'Build with -ProPath <the private tree> and try again.'
        }
    }
    New-Package -Name 'WallFramingAutoDimension' -ExtraFiles $freeExtra

    if ($proBuilt) {
        Write-Output 'Assembling the Pro package ...'
        New-Package -Name 'WallFramingAutoDimensionPro' `
                    -ExtraFiles @((Join-Path $outDir 'WF_Pro.dll'),
                                  (Join-Path $outDir 'WF_Rules_Pro.dll'))
    }
}

if ($AioPackage) {
    # The package of the AIO macro: it is free - it asks for no licence key -
    # and it draws the whole set of annotations, so it needs the Pro modules.
    # It is NOT published (rule R10: the Pro stages are not published); it is
    # built in the dev tree and handed to a customer directly, like the Pro
    # package.
    if (-not $proBuilt) {
        Write-Warning 'The AIO package cannot be built: the Pro modules were not built.'
        Write-Warning 'Build with -ProPath <the private tree> and try again.'
    } else {
        Write-Output 'Assembling the AIO package ...'
        Write-Output '  it carries the Pro modules: the AIO macro draws everything, and asks for no key'
        New-Package -Name 'WallFramingAutoDimensionAIO' `
                    -ExtraFiles @((Join-Path $outDir 'WF_Pro.dll'),
                                  (Join-Path $outDir 'WF_Rules_Pro.dll'))
    }
}

# ---------------------------------------------------------------------------
# The deployment.
#
# The loader goes into the macro catalog of Tekla; the assemblies go into
# the app folder, next to the licence.
# ---------------------------------------------------------------------------
$appFolder = Join-Path $env:LOCALAPPDATA 'P142\WallFraming'

if ($Deploy) {
    Write-Output 'Deploying to the Tekla macro catalog ...'
    if (-not (Test-Path $catalog)) { throw "macro catalog not found: $catalog" }
    Copy-Item -Path (Join-Path $here 'P142_WF_AutoDimension.cs') `
              -Destination (Join-Path $catalog 'P142_WF_AutoDimension.cs') -Force
    Copy-Item -Path (Join-Path $here 'WallFramingAutoDimensionPro-AIO.cs') `
              -Destination (Join-Path $catalog 'WallFramingAutoDimensionPro-AIO.cs') -Force
    Copy-Item -Path (Join-Path $here 'P142_WF_Isometric.cs') `
              -Destination (Join-Path $catalog 'P142_WF_Isometric.cs') -Force
    Copy-Item -Path (Join-Path $here 'P142_WF_Section.cs') `
              -Destination (Join-Path $catalog 'P142_WF_Section.cs') -Force
    Copy-Item -Path (Join-Path $here 'P142_BASE_C_W.cs') `
              -Destination (Join-Path $catalog 'P142_BASE_C_W.cs') -Force

    # The catalog shows an icon next to the macro; draw a small one if there
    # is none yet.
    $bmp = Join-Path $catalog 'P142_WF_AutoDimension.bmp'
    New-IconFile -Path $bmp
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'P142_WF_AutoDimension.cs'))
    Write-Output ("  OK  {0}" -f $bmp)

    $bmpAio = Join-Path $catalog 'WallFramingAutoDimensionPro-AIO.bmp'
    New-IconFile -Path $bmpAio -Red 0 -Green 110 -Blue 60
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'WallFramingAutoDimensionPro-AIO.cs'))
    Write-Output ("  OK  {0}" -f $bmpAio)

    $bmpIso = Join-Path $catalog 'P142_WF_Isometric.bmp'
    New-IconFile -Path $bmpIso -Red 180 -Green 100 -Blue 0
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'P142_WF_Isometric.cs'))
    Write-Output ("  OK  {0}" -f $bmpIso)

    $bmpBcw = Join-Path $catalog 'P142_BASE_C_W.bmp'
    New-IconFile -Path $bmpBcw -Red 200 -Green 40 -Blue 40
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'P142_BASE_C_W.cs'))
    Write-Output ("  OK  {0}" -f $bmpBcw)

    $bmpSec = Join-Path $catalog 'P142_WF_Section.bmp'
    New-IconFile -Path $bmpSec -Red 0 -Green 150 -Blue 180
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'P142_WF_Section.cs'))
    Write-Output ("  OK  {0}" -f $bmpSec)

    Write-Output 'Deploying the modules ...'
    if (-not (Test-Path $appFolder)) { New-Item -ItemType Directory -Path $appFolder -Force | Out-Null }
    foreach ($dll in @('WF_Core.dll', 'WF_Rules.dll', 'WF_License.dll',
                       'WF_Isometric.dll', 'WF_IsometricRules.dll',
                       'WF_Section.dll', 'WF_SectionRules.dll',
                       'WF_BaseCW.dll')) {
        Copy-Item (Join-Path $outDir $dll) $appFolder -Force
        Write-Output ("  OK  {0}" -f (Join-Path $appFolder $dll))
    }
    # Deploy settings file (create from default if not exists in appFolder)
    $settingsSrc = Join-Path $here 'WF_Settings.json'
    $settingsDst = Join-Path $appFolder 'WF_Settings.json'
    if (-not (Test-Path $settingsDst)) {
        Copy-Item $settingsSrc $settingsDst -Force
        Write-Output ("  OK  {0} (default settings)" -f $settingsDst)
    } else {
        Write-Output ("  existing settings kept: {0}" -f $settingsDst)
    }
    if ($proBuilt) {
        foreach ($dll in @('WF_Pro.dll', 'WF_Rules_Pro.dll')) {
            Copy-Item (Join-Path $outDir $dll) $appFolder -Force
            Write-Output ("  OK  {0}" -f (Join-Path $appFolder $dll))
        }
    }
}

Write-Output 'BUILD OK'
