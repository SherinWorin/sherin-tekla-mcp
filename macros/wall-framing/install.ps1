# Installs the wall-framing auto-dimension macro into the macro catalog of
# Tekla Structures 2025 on THIS computer.
#
#   powershell -NoProfile -File install.ps1
#   powershell -NoProfile -File install.ps1 -TeklaPath "C:\TeklaStructures\2025.0"
#
# What it does:
#   1. finds the Tekla Structures installation (no hard coded path)
#   2. checks that it is a 2025 installation
#   3. copies source\P142_WF_AutoDimension_merged.cs
#         -> <Tekla>\Environments\common\macros\drawings\P142_WF_AutoDimension.cs
#   4. copies source\P142_WF_AutoDimension.bmp ->  the same folder (the icon)
#
# The macro itself needs no other file: the rules and the macro are merged
# into the one .cs file.
param(
    [string]$TeklaPath = '',
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $here 'source'

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
if (-not (Test-Path $catalog)) {
    Write-Output ("ERROR: the macro catalog was not found: {0}" -f $catalog)
    exit 1
}

$files = @(
    @{ Name = 'P142_WF_AutoDimension_merged.cs'; To = (Join-Path $catalog 'P142_WF_AutoDimension.cs') },
    @{ Name = 'P142_WF_AutoDimension.bmp';       To = (Join-Path $catalog 'P142_WF_AutoDimension.bmp') }
)

# The merged source and the icon are looked for in a few places, so that the
# same script works in the deployment package and in the source tree:
#   source\            - the layout of the package
#   source\bin\        - the layout of the source tree, after a build
foreach ($f in $files) {
    $f.From = $null
    foreach ($dir in @($src, (Join-Path $src 'bin'), (Join-Path $here 'bin'))) {
        $cand = Join-Path $dir $f.Name
        if (Test-Path $cand) { $f.From = $cand; break }
    }
    if (-not $f.From) {
        Write-Output ("ERROR: the package is incomplete, this file is missing: {0}" -f $f.Name)
        Write-Output ("       looked in {0}, in {0}\bin and in {1}\bin" -f $src, $here)
        exit 1
    }
}

# The file that goes into the catalog must be the MERGED one: the macro and
# the rules in one file.  The macro alone calls WF.WfRules..., and without
# the rules next to it Tekla cannot compile it - the error is a missing
# namespace, and it is not easy to see where it came from.
$merged = $files[0].From
$text = Get-Content $merged -Raw
if ($text -notmatch 'namespace\s+P142\.WallFraming') {
    Write-Output ("ERROR: {0} is not the merged source." -f $merged)
    Write-Output '       The file that goes into the catalog must contain BOTH the'
    Write-Output '       macro and the rules (namespace P142.WallFraming).'
    Write-Output '       Build it:  cd source  &&  powershell -NoProfile -File build.ps1'
    Write-Output '       and install  source\bin\P142_WF_AutoDimension_merged.cs'
    exit 1
}

if ($WhatIfOnly) {
    foreach ($f in $files) { Write-Output ("would copy {0} -> {1}" -f $f.From, $f.To) }
    exit 0
}

foreach ($f in $files) {
    Copy-Item -Path $f.From -Destination $f.To -Force
    Write-Output ("  OK  {0}" -f $f.To)
}

Write-Output ''
Write-Output 'The macro is installed.  It is on the ribbon:'
Write-Output '  Applications &gt; Macros &gt; P142_WF_AutoDimension'
Write-Output ''
Write-Output 'Open the GA drawing whose title holds "WALL FRAMING LAYOUT" and run it.'
Write-Output 'The macro refuses to write into a drawing that is issued or frozen.'
