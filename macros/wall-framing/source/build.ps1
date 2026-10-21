# Builds the wall-framing macro for Tekla Structures 2025.
#
#   source/bin/WF_Rules.dll             - the annotation rules, no Tekla dependency
#   source/bin/P142_WF_AutoDimension.dll - the macro, needs both Tekla and the rules
#   source/bin/P142_WF_AutoDimension_merged.cs
#                                         - the rules and the macro in ONE file,
#                                           so that Tekla's macro catalog can
#                                           compile it without a second DLL
#   source/bin/P142_WF_AutoDimension_merged.dll
#                                         - proof that the merged file compiles
#
# Usage (Command Prompt (Windows)):
#   powershell -NoProfile -File source/build.ps1
#   powershell -NoProfile -File source/build.ps1 -Deploy    (also copies to the macro catalog)
param(
    [switch]$Deploy,
    [switch]$Package,
    [string]$TeklaPath = '',
    [string]$CscPath = ''
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

Write-Output ("Tekla  : {0}" -f $tekla)
Write-Output ("csc    : {0}" -f $csc)
if ((Split-Path -Leaf $tekla) -notlike '2025*') {
    Write-Warning ("This macro is written for Tekla Structures 2025, but {0} was found." -f $tekla)
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# ---------------------------------------------------------------------------
# The macro catalog shows an icon next to the macro.  One is drawn when there
# is none yet: a blue square with a white dimension cross in it.
# ---------------------------------------------------------------------------
function New-IconFile {
    param([string]$Path, [switch]$Force)
    if ((Test-Path $Path) -and (-not $Force)) { return }
    Add-Type -AssemblyName System.Drawing
    $img = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($img)
    $g.Clear([System.Drawing.Color]::FromArgb(0, 90, 120))
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
    (Join-Path $fw 'System.Windows.Forms.dll')
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
    param([string[]]$Refs, [string]$Source, [string]$Out)
    $args = @('-nologo', '-target:library', "-out:$Out", '-nostdlib+', '-define:TRACE')
    foreach ($r in $Refs) { $args += "-r:`"$r`"" }
    $args += $Source
    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $Source (exit $LASTEXITCODE)" }
    Write-Output ("  OK  {0}  ({1} bytes)" -f (Split-Path -Leaf $Out), (Get-Item $Out).Length)
}

Write-Output 'Building WF_Rules.dll ...'
Invoke-Csc -Refs $framework -Source (Join-Path $here 'WF_Rules.cs') -Out (Join-Path $outDir 'WF_Rules.dll')

Write-Output 'Building P142_WF_AutoDimension.dll ...'
Invoke-Csc -Refs ($framework + $teklaDlls + @((Join-Path $outDir 'WF_Rules.dll'))) `
            -Source (Join-Path $here 'P142_WF_AutoDimension.cs') `
            -Out (Join-Path $outDir 'P142_WF_AutoDimension.dll')

# ---------------------------------------------------------------------------
# The merged single-file source, for the Tekla macro catalog.
#
# The catalog compiles ONE .cs file, so the rules namespace has to live in the
# same file as the macro.  The rules file keeps its own "using" block; those
# directives must be stripped, because a compilation unit may only declare
# "using" before its first namespace or type.
# ---------------------------------------------------------------------------
Write-Output 'Generating P142_WF_AutoDimension_merged.cs ...'
$macroSrc = Get-Content (Join-Path $here 'P142_WF_AutoDimension.cs') -Raw
$rulesSrc = Get-Content (Join-Path $here 'WF_Rules.cs') -Raw
$rulesBody = ($rulesSrc -split "`r?`n" |
              Where-Object { $_ -notmatch '^\s*using\s' }) -join "`r`n"

$banner = @(
    '// AUTO-GENERATED by source/build.ps1 - do not edit by hand.',
    '//',
    '// Merged source of WF_Rules.cs (annotation rules, namespace P142.WallFraming)',
    '// and P142_WF_AutoDimension.cs (the macro, namespace UserMacros), so that the',
    '// Tekla macro catalog can compile the whole macro from a single .cs file',
    '// without a second DLL.',
    ''
) -join "`r`n"

$merged = $banner + $macroSrc + "`r`n`r`n// ===== WF_Rules.cs (rules, no Tekla dependency) =====`r`n`r`n" + $rulesBody
$mergedPath = Join-Path $outDir 'P142_WF_AutoDimension_merged.cs'
# UTF-8 WITHOUT a byte-order mark: Tekla's own macro compiler is stricter
# than Roslyn about a BOM at the start of the file.
[System.IO.File]::WriteAllText($mergedPath, $merged,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("  OK  {0}  ({1} bytes)" -f (Split-Path -Leaf $mergedPath), (Get-Item $mergedPath).Length)

Write-Output 'Verifying the merged source compiles ...'
Invoke-Csc -Refs ($framework + $teklaDlls) `
            -Source $mergedPath `
            -Out (Join-Path $outDir 'P142_WF_AutoDimension_merged.dll')

# The icon of the macro, next to the merged source, so that install.ps1 can
# find the two files it needs in the build output as well.
New-IconFile -Path (Join-Path $outDir 'P142_WF_AutoDimension.bmp') -Force

if ($Package) {
    Write-Output 'Assembling the deployment package ...'
    $pkgParent = Split-Path -Parent $here
    $pkgRoot = Join-Path $pkgParent 'package\WallFramingAutoDimension'
    $pkgSrc  = Join-Path $pkgRoot 'source'
    if (Test-Path $pkgRoot) { Remove-Item -Path $pkgRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $pkgSrc | Out-Null

    # the single file the Tekla macro catalog needs.  It keeps the name
    # _merged here, because the folder also holds the two sources it was
    # made of: the installer gives it the name of the macro in the catalog.
    Copy-Item (Join-Path $outDir 'P142_WF_AutoDimension_merged.cs') `
              (Join-Path $pkgSrc 'P142_WF_AutoDimension_merged.cs') -Force
    # the icon of the macro
    $bmpOut = Join-Path $pkgSrc 'P142_WF_AutoDimension.bmp'
    New-IconFile -Path $bmpOut -Force
    # the sources and the build script, for a re-build on the other computer
    Copy-Item (Join-Path $here 'WF_Rules.cs') (Join-Path $pkgSrc 'WF_Rules.cs') -Force
    Copy-Item (Join-Path $here 'P142_WF_AutoDimension.cs') (Join-Path $pkgSrc 'P142_WF_AutoDimension.cs') -Force
    Copy-Item (Join-Path $here 'build.ps1') (Join-Path $pkgSrc 'build.ps1') -Force

    # the files of the package root: they sit next to the sources in the
    # development tree, and one level above them in the published tree
    foreach ($name in @('install.ps1', 'INSTALL_NOTES.txt')) {
        $f = Join-Path $here $name
        if (-not (Test-Path $f)) { $f = Join-Path $pkgParent $name }
        if (Test-Path $f) { Copy-Item $f (Join-Path $pkgRoot $name) -Force }
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

    $zip = Join-Path $pkgParent 'package\WallFramingAutoDimension.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # The zip is written by hand, so that the entries use "/" and not "\".
    # Windows Explorer accepts both, but a zip with backslashes is refused
    # by some other tools - and the package has to open everywhere.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $root = (Resolve-Path $pkgRoot).Path.TrimEnd('\') + '\'
            foreach ($f in Get-ChildItem -Path $pkgRoot -Recurse -File) {
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
    Write-Output ("  OK  {0}  ({1} bytes)" -f $zip, (Get-Item $zip).Length)
}

if ($Deploy) {
    Write-Output 'Deploying to the Tekla macro catalog ...'
    if (-not (Test-Path $catalog)) { throw "macro catalog not found: $catalog" }
    Copy-Item -Path $mergedPath -Destination (Join-Path $catalog 'P142_WF_AutoDimension.cs') -Force

    # The catalog shows an icon next to the macro; draw a small one if there
    # is none yet.
    $bmp = Join-Path $catalog 'P142_WF_AutoDimension.bmp'
    New-IconFile -Path $bmp
    Write-Output ("  OK  {0}" -f (Join-Path $catalog 'P142_WF_AutoDimension.cs'))
    Write-Output ("  OK  {0}" -f $bmp)
}

Write-Output 'BUILD OK'
