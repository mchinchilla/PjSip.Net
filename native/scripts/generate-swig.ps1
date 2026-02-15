#Requires -Version 7.0
<#
.SYNOPSIS
    Runs SWIG to generate C# wrapper source files from the pjsua2 SWIG interface.

.DESCRIPTION
    This script invokes SWIG to process the pjsua2.i interface definition and
    produce C# source files that can be compiled into the PjSip.Net.Interop
    project. It also produces the C++ wrapper source (pjsua2_wrap.cpp) that
    must be compiled into the native pjsua2 shared library.

    The generated C# files use the namespace PjSip.Net.Interop.Generated.

.PARAMETER PjProjectDir
    Path to the pjproject source tree. Defaults to native/pjproject relative
    to the repository root.

.PARAMETER OutputDir
    Directory where the generated C# files will be written. Defaults to
    src/PjSip.Net.Interop/Generated.

.PARAMETER SwigPath
    Path to the SWIG executable. Defaults to "swig" (assumes it is on PATH).

.PARAMETER CppOutputPath
    Path where the generated C++ wrapper file will be written. If not specified,
    it is written into the pjproject source tree at
    pjsip-apps/src/swig/csharp/pjsua2_wrap.cpp.

.EXAMPLE
    .\generate-swig.ps1
    .\generate-swig.ps1 -PjProjectDir "C:\pjproject" -OutputDir "C:\output"
    .\generate-swig.ps1 -SwigPath "C:\tools\swig\swig.exe"
#>

[CmdletBinding()]
param(
    [string]$PjProjectDir,
    [string]$OutputDir,
    [string]$SwigPath = "swig",
    [string]$CppOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

if (-not $PjProjectDir) {
    $PjProjectDir = Join-Path $RepoRoot "native" "pjproject"
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "src" "PjSip.Net.Interop" "Generated"
}

if (-not $CppOutputPath) {
    $CppOutputPath = Join-Path $PjProjectDir "pjsip-apps" "src" "swig" "csharp" "pjsua2_wrap.cpp"
}

$SwigInterface = Join-Path $PjProjectDir "pjsip-apps" "src" "swig" "pjsua2.i"

Write-Host "=== SWIG C# Wrapper Generation ===" -ForegroundColor Cyan
Write-Host "pjproject dir  : $PjProjectDir"
Write-Host "SWIG interface : $SwigInterface"
Write-Host "C# output dir  : $OutputDir"
Write-Host "C++ output     : $CppOutputPath"
Write-Host ""

# ---------------------------------------------------------------------------
# Validate prerequisites
# ---------------------------------------------------------------------------

# Check SWIG is available
try {
    $SwigVersion = & $SwigPath -version 2>&1
    $VersionLine = ($SwigVersion | Select-String "SWIG Version").ToString().Trim()
    Write-Host "Using $VersionLine" -ForegroundColor Green
}
catch {
    throw @"
SWIG not found at '$SwigPath'.
Install SWIG 4.0+ and ensure it is on your PATH, or pass -SwigPath.
  Windows: choco install swig  OR  scoop install swig
  macOS:   brew install swig
  Linux:   apt-get install swig
"@
}

# Check interface file exists
if (-not (Test-Path $SwigInterface)) {
    throw @"
SWIG interface file not found: $SwigInterface
Ensure pjproject has been cloned first (run build-win-x64.ps1 or equivalent).
"@
}

# ---------------------------------------------------------------------------
# Prepare output directories
# ---------------------------------------------------------------------------

if (Test-Path $OutputDir) {
    Write-Host "Cleaning existing generated files in $OutputDir..." -ForegroundColor Yellow
    Get-ChildItem -Path $OutputDir -Filter "*.cs" | Remove-Item -Force
}
else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$CppOutputDir = Split-Path $CppOutputPath -Parent
if (-not (Test-Path $CppOutputDir)) {
    New-Item -ItemType Directory -Path $CppOutputDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Include paths for SWIG — it needs to find PJSIP headers
# ---------------------------------------------------------------------------

$IncludePaths = @(
    "-I$PjProjectDir/pjlib/include",
    "-I$PjProjectDir/pjlib-util/include",
    "-I$PjProjectDir/pjnath/include",
    "-I$PjProjectDir/pjmedia/include",
    "-I$PjProjectDir/pjsip/include"
)

# ---------------------------------------------------------------------------
# Run SWIG
# ---------------------------------------------------------------------------

Write-Host "Running SWIG..." -ForegroundColor Green

$SwigArgs = @(
    "-csharp",
    "-c++",
    "-namespace", "PjSip.Net.Interop.Generated",
    "-dllimport", "pjsua2",
    "-outdir", $OutputDir,
    "-o", $CppOutputPath
) + $IncludePaths + @($SwigInterface)

Write-Host "  Command: $SwigPath $($SwigArgs -join ' ')" -ForegroundColor DarkGray

& $SwigPath @SwigArgs

if ($LASTEXITCODE -ne 0) {
    throw "SWIG failed with exit code $LASTEXITCODE."
}

# ---------------------------------------------------------------------------
# Post-process: count generated files
# ---------------------------------------------------------------------------

$GeneratedFiles = Get-ChildItem -Path $OutputDir -Filter "*.cs"
$FileCount = $GeneratedFiles.Count

Write-Host ""
Write-Host "SWIG generation complete." -ForegroundColor Green
Write-Host "  C# files generated : $FileCount (in $OutputDir)"
Write-Host "  C++ wrapper        : $CppOutputPath"

# List the largest generated files for reference
if ($FileCount -gt 0) {
    Write-Host ""
    Write-Host "  Largest generated files:" -ForegroundColor DarkGray
    $GeneratedFiles | Sort-Object Length -Descending | Select-Object -First 10 | ForEach-Object {
        $SizeKb = [math]::Round($_.Length / 1024, 1)
        Write-Host "    $($_.Name) ($SizeKb KB)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
