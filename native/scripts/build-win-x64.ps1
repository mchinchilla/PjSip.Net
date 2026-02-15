#Requires -Version 7.0
<#
.SYNOPSIS
    Builds PJSIP pjsua2 as a shared library (DLL) for Windows x64 with Schannel TLS.

.DESCRIPTION
    This script:
      1. Clones pjproject 2.16 if the source tree is not already present
      2. Copies the Schannel config_site.h into the source tree
      3. Builds the Release-Dynamic|x64 configuration using MSBuild
      4. Runs SWIG to generate the C# interop wrapper
      5. Copies pjsua2.dll to the native NuGet package runtimes folder

.PARAMETER PjProjectTag
    The pjproject Git tag to clone. Defaults to "2.16".

.PARAMETER SkipClone
    Skip the Git clone step (use the existing source tree).

.PARAMETER SwigPath
    Path to the SWIG executable. Defaults to "swig" (assumes it is on PATH).

.EXAMPLE
    .\build-win-x64.ps1
    .\build-win-x64.ps1 -PjProjectTag "2.16" -SkipClone
#>

[CmdletBinding()]
param(
    [string]$PjProjectTag = "2.16",
    [switch]$SkipClone,
    [string]$SwigPath = "swig"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

$RepoRoot       = (Resolve-Path "$PSScriptRoot\..\..").Path
$NativeRoot     = Join-Path $RepoRoot "native"
$PjProjectDir   = Join-Path $NativeRoot "pjproject"
$ConfigSiteDir  = Join-Path $NativeRoot "config_site"
$ScriptsDir     = Join-Path $NativeRoot "scripts"

$NativePackageDir = Join-Path $RepoRoot "src" "PjSip.Net.Native.Win64"
$RuntimeDir       = Join-Path $NativePackageDir "runtimes" "win-x64" "native"

$InteropDir     = Join-Path $RepoRoot "src" "PjSip.Net.Interop"
$SwigOutputDir  = Join-Path $InteropDir "Generated"

# Target config_site.h location inside pjproject
$ConfigSiteDest = Join-Path $PjProjectDir "pjlib" "include" "pj" "config_site.h"

# The Visual Studio solution for pjproject on Windows
$PjSlnPath      = Join-Path $PjProjectDir "pjproject-vs14.sln"

Write-Host "=== PjSip.Net Native Build — Windows x64 (Schannel) ===" -ForegroundColor Cyan
Write-Host "Repository root : $RepoRoot"
Write-Host "pjproject source: $PjProjectDir"
Write-Host "Output runtime  : $RuntimeDir"
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1 — Clone pjproject
# ---------------------------------------------------------------------------

if (-not $SkipClone) {
    if (Test-Path $PjProjectDir) {
        Write-Host "[1/5] pjproject directory already exists, pulling latest for tag $PjProjectTag..." -ForegroundColor Yellow
        Push-Location $PjProjectDir
        try {
            git fetch --tags 2>&1 | Out-Null
            git checkout $PjProjectTag 2>&1 | Out-Null
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "[1/5] Cloning pjproject tag $PjProjectTag..." -ForegroundColor Green
        git clone --depth 1 --branch $PjProjectTag `
            "https://github.com/pjsip/pjproject.git" $PjProjectDir
    }
}
else {
    Write-Host "[1/5] Skipping clone (--SkipClone)." -ForegroundColor Yellow
    if (-not (Test-Path $PjProjectDir)) {
        throw "pjproject directory not found at $PjProjectDir. Run without -SkipClone first."
    }
}

# ---------------------------------------------------------------------------
# Step 2 — Copy config_site.h
# ---------------------------------------------------------------------------

Write-Host "[2/5] Copying config_site_win_schannel.h -> config_site.h..." -ForegroundColor Green

$ConfigSiteSrc = Join-Path $ConfigSiteDir "config_site_win_schannel.h"
if (-not (Test-Path $ConfigSiteSrc)) {
    throw "Config site header not found: $ConfigSiteSrc"
}

$ConfigSiteDestDir = Split-Path $ConfigSiteDest -Parent
if (-not (Test-Path $ConfigSiteDestDir)) {
    New-Item -ItemType Directory -Path $ConfigSiteDestDir -Force | Out-Null
}

Copy-Item -Path $ConfigSiteSrc -Destination $ConfigSiteDest -Force
Write-Host "  -> $ConfigSiteDest"

# ---------------------------------------------------------------------------
# Step 3 — Build with MSBuild (Release-Dynamic|x64)
# ---------------------------------------------------------------------------

Write-Host "[3/5] Building pjproject Release-Dynamic|x64 with MSBuild..." -ForegroundColor Green

# Locate MSBuild via vswhere
$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $VsWhere)) {
    throw "vswhere.exe not found. Ensure Visual Studio 2019+ is installed."
}

$VsInstallPath = & $VsWhere -latest -property installationPath -requires Microsoft.Component.MSBuild
if (-not $VsInstallPath) {
    throw "Could not locate a Visual Studio installation with MSBuild."
}

$MSBuildPath = Join-Path $VsInstallPath "MSBuild" "Current" "Bin" "MSBuild.exe"
if (-not (Test-Path $MSBuildPath)) {
    # Fallback for VS 2019
    $MSBuildPath = Join-Path $VsInstallPath "MSBuild" "Current" "Bin" "amd64" "MSBuild.exe"
}

if (-not (Test-Path $MSBuildPath)) {
    throw "MSBuild.exe not found at expected paths under $VsInstallPath."
}

Write-Host "  Using MSBuild: $MSBuildPath"

if (-not (Test-Path $PjSlnPath)) {
    throw "pjproject solution not found: $PjSlnPath"
}

& $MSBuildPath $PjSlnPath `
    /p:Configuration="Release-Dynamic" `
    /p:Platform=x64 `
    /m `
    /t:Build `
    /v:minimal

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

Write-Host "  Build succeeded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 4 — Run SWIG to generate C# wrapper
# ---------------------------------------------------------------------------

Write-Host "[4/5] Running SWIG to generate C# interop wrapper..." -ForegroundColor Green

# Invoke the shared SWIG generation script
$SwigScript = Join-Path $ScriptsDir "generate-swig.ps1"
if (Test-Path $SwigScript) {
    & $SwigScript -PjProjectDir $PjProjectDir -OutputDir $SwigOutputDir -SwigPath $SwigPath
}
else {
    Write-Warning "SWIG generation script not found at $SwigScript. Skipping SWIG step."
}

# ---------------------------------------------------------------------------
# Step 5 — Copy native binary to runtimes folder
# ---------------------------------------------------------------------------

Write-Host "[5/5] Copying pjsua2.dll to native package runtimes folder..." -ForegroundColor Green

# The built DLL is typically at:
#   pjproject/pjsip-apps/lib/pjsua2.dll  (Release-Dynamic x64)
# or under the output directory structure depending on the VS version.
$PossibleDllPaths = @(
    (Join-Path $PjProjectDir "pjsip-apps" "lib" "pjsua2.dll"),
    (Join-Path $PjProjectDir "pjsip-apps" "build" "x64" "Release-Dynamic" "pjsua2.dll"),
    (Join-Path $PjProjectDir "lib" "pjsua2.dll"),
    (Join-Path $PjProjectDir "Release-Dynamic" "pjsua2.dll")
)

$BuiltDll = $PossibleDllPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $BuiltDll) {
    Write-Warning "Could not locate the built pjsua2.dll. Searched:"
    $PossibleDllPaths | ForEach-Object { Write-Warning "  $_" }
    Write-Warning "You may need to copy the DLL manually to: $RuntimeDir"
}
else {
    if (-not (Test-Path $RuntimeDir)) {
        New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null
    }

    Copy-Item -Path $BuiltDll -Destination (Join-Path $RuntimeDir "pjsua2.dll") -Force
    Write-Host "  -> $(Join-Path $RuntimeDir 'pjsua2.dll')" -ForegroundColor Green

    # Also copy any dependency DLLs from the same directory (e.g., pjsua.dll, etc.)
    $DllDir = Split-Path $BuiltDll -Parent
    $DepDlls = Get-ChildItem -Path $DllDir -Filter "pj*.dll" | Where-Object { $_.Name -ne "pjsua2.dll" }
    foreach ($dep in $DepDlls) {
        Copy-Item -Path $dep.FullName -Destination (Join-Path $RuntimeDir $dep.Name) -Force
        Write-Host "  -> $(Join-Path $RuntimeDir $dep.Name) (dependency)" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Cyan
Write-Host "Native binary location: $RuntimeDir"
Write-Host "SWIG output location  : $SwigOutputDir"
