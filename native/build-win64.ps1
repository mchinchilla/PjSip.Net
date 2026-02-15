# build-win64.ps1 — Build PJSIP 2.16 native pjsua2.dll for Windows x64
# Prerequisites: Visual Studio 2022 with C++ workload, SWIG 4.x in PATH
#
# Usage: .\native\build-win64.ps1 [-SwigOnly] [-SkipSwig]

param(
    [switch]$SwigOnly,   # Only regenerate SWIG wrapper, skip native build
    [switch]$SkipSwig    # Skip SWIG, only build native DLL
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PjDir = Join-Path $RepoRoot "pjproject"
$OutputDir = Join-Path $RepoRoot "src\PjSip.Net.Native.Win64\runtimes\win-x64\native"

# --- Clone / checkout pjproject ---
if (-not (Test-Path $PjDir)) {
    Write-Host "Cloning pjproject 2.16..."
    git clone --branch 2.16 --depth 1 https://github.com/pjsip/pjproject.git $PjDir
}

# --- Copy config_site.h ---
$configSrc = Join-Path $PSScriptRoot "config_site.h"
$configDst = Join-Path $PjDir "pjlib\include\pj\config_site.h"
Copy-Item $configSrc $configDst -Force
Write-Host "Copied config_site.h"

# --- SWIG: generate C# wrapper + C++ bridge ---
if (-not $SkipSwig) {
    Write-Host "Running SWIG to generate C# bindings..."
    $swigDir = Join-Path $PjDir "pjsip-apps\src\swig"
    $csharpDir = Join-Path $swigDir "csharp"

    if (-not (Test-Path $csharpDir)) { New-Item -ItemType Directory -Path $csharpDir | Out-Null }

    $swigArgs = @(
        "-I`"$(Join-Path $PjDir 'pjlib\include')`""
        "-I`"$(Join-Path $PjDir 'pjlib-util\include')`""
        "-I`"$(Join-Path $PjDir 'pjmedia\include')`""
        "-I`"$(Join-Path $PjDir 'pjsip\include')`""
        "-I`"$(Join-Path $PjDir 'pjnath\include')`""
        "-w312"
        "-c++"
        "-csharp"
        "-dllimport", "pjsua2"
        "-namespace", "PjSip.Net.Interop.Generated"
        "-outdir", $csharpDir
        "-o", (Join-Path $csharpDir "pjsua2_wrap.cpp")
        (Join-Path $swigDir "pjsua2.i")
    )

    & swig $swigArgs
    if ($LASTEXITCODE -ne 0) { throw "SWIG failed with exit code $LASTEXITCODE" }
    Write-Host "SWIG generated files in $csharpDir"

    if ($SwigOnly) {
        Write-Host "SwigOnly mode — done."
        exit 0
    }
}

# --- Build pjproject with MSBuild ---
Write-Host "Building pjproject (Release-Dynamic|x64)..."

# Find MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1

if (-not $msbuild) { throw "MSBuild not found. Install Visual Studio 2022 with C++ workload." }

$slnPath = Join-Path $PjDir "pjproject-vs14.sln"
& $msbuild $slnPath /p:Configuration="Release-Dynamic" /p:Platform=x64 /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "pjproject build failed" }

# --- Build the SWIG wrapper DLL ---
Write-Host "Building pjsua2.dll wrapper..."

$wrapCpp = Join-Path $PjDir "pjsip-apps\src\swig\csharp\pjsua2_wrap.cpp"
$libDir = Join-Path $PjDir "lib"

# Collect all pjproject .lib files
$libs = Get-ChildItem -Path $libDir -Filter "*-x86_64-x64-vc*.lib" | ForEach-Object { $_.FullName }
$systemLibs = @("ws2_32.lib", "Iphlpapi.lib", "dsound.lib", "dxguid.lib",
                 "netapi32.lib", "mswsock.lib", "ole32.lib", "user32.lib",
                 "gdi32.lib", "advapi32.lib", "Secur32.lib", "Crypt32.lib")

$includeArgs = @(
    "/I`"$(Join-Path $PjDir 'pjlib\include')`""
    "/I`"$(Join-Path $PjDir 'pjlib-util\include')`""
    "/I`"$(Join-Path $PjDir 'pjmedia\include')`""
    "/I`"$(Join-Path $PjDir 'pjsip\include')`""
    "/I`"$(Join-Path $PjDir 'pjnath\include')`""
)

# Find cl.exe via vswhere
$vsInstall = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -property installationPath
$vcVarsAll = Join-Path $vsInstall "VC\Auxiliary\Build\vcvarsall.bat"

$dllOutput = Join-Path $OutputDir "pjsua2.dll"
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

# Build using a Developer Command Prompt
$buildCmd = @"
call "$vcVarsAll" x64
cl /nologo /MD /EHsc /O2 /DNDEBUG /DPJ_DLL=1 /DPJ_EXPORTING=1 $($includeArgs -join ' ') /LD "$wrapCpp" /Fe:"$dllOutput" /link /LIBPATH:"$libDir" $($libs -join ' ') $($systemLibs -join ' ')
"@

$tempBat = Join-Path $env:TEMP "build_pjsua2.bat"
$buildCmd | Out-File -FilePath $tempBat -Encoding ascii
& cmd /c $tempBat
if ($LASTEXITCODE -ne 0) { throw "pjsua2.dll compilation failed" }

# Cleanup temp files from cl.exe
Remove-Item (Join-Path $OutputDir "*.obj") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $OutputDir "*.exp") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $OutputDir "*.lib") -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "SUCCESS: $dllOutput" -ForegroundColor Green
Write-Host "Size: $((Get-Item $dllOutput).Length / 1MB) MB"
