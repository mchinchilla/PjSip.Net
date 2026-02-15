#!/usr/bin/env bash
#
# build-ios-arm64.sh — Build PJSIP pjsua2 shared library for iOS arm64.
#
# This script:
#   1. Clones pjproject 2.16 if not already present
#   2. Copies config_site_ios.h to config_site.h
#   3. Configures and builds pjproject for iOS arm64 with Secure Transport
#   4. Runs SWIG to generate the C# interop wrapper
#   5. Copies libpjsua2.dylib to the native NuGet package runtimes folder
#
# Prerequisites:
#   - macOS with Xcode command-line tools installed
#   - SWIG 4.0+ (optional, for C# wrapper generation)
#
# Usage:
#   ./build-ios-arm64.sh [--skip-clone] [--tag 2.16]
#

set -euo pipefail

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------

PJPROJECT_TAG="2.16"
SKIP_CLONE=false
IOS_MIN_VERSION="15.0"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-clone)     SKIP_CLONE=true; shift ;;
        --tag)            PJPROJECT_TAG="$2"; shift 2 ;;
        --min-ios)        IOS_MIN_VERSION="$2"; shift 2 ;;
        *)                echo "Unknown option: $1"; exit 1 ;;
    esac
done

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
NATIVE_ROOT="$REPO_ROOT/native"
PJPROJECT_DIR="$NATIVE_ROOT/pjproject"
CONFIG_SITE_DIR="$NATIVE_ROOT/config_site"

NATIVE_PKG_DIR="$REPO_ROOT/src/PjSip.Net.Native.iOS"
RUNTIME_DIR="$NATIVE_PKG_DIR/runtimes/ios-arm64/native"

INTEROP_DIR="$REPO_ROOT/src/PjSip.Net.Interop"
SWIG_OUTPUT_DIR="$INTEROP_DIR/Generated"

ARCH="arm64"
TARGET_TRIPLE="arm-apple-darwin"

echo "=== PjSip.Net Native Build — iOS arm64 ==="
echo "Repository root : $REPO_ROOT"
echo "pjproject source: $PJPROJECT_DIR"
echo "Output runtime  : $RUNTIME_DIR"
echo "iOS min version : $IOS_MIN_VERSION"
echo ""

# ---------------------------------------------------------------------------
# Detect Xcode / iOS SDK
# ---------------------------------------------------------------------------

XCODE_DEVELOPER="$(xcode-select -p)"
if [[ ! -d "$XCODE_DEVELOPER" ]]; then
    echo "ERROR: Xcode command-line tools not found."
    echo "  Install with: xcode-select --install"
    exit 1
fi

IOS_SDK="$(xcrun --sdk iphoneos --show-sdk-path)"
if [[ ! -d "$IOS_SDK" ]]; then
    echo "ERROR: iOS SDK not found. Ensure Xcode is installed."
    exit 1
fi

echo "Using Xcode  : $XCODE_DEVELOPER"
echo "Using iOS SDK: $IOS_SDK"

# ---------------------------------------------------------------------------
# Step 1 — Clone pjproject
# ---------------------------------------------------------------------------

if [[ "$SKIP_CLONE" == "false" ]]; then
    if [[ -d "$PJPROJECT_DIR" ]]; then
        echo "[1/5] pjproject directory exists, checking out tag $PJPROJECT_TAG..."
        cd "$PJPROJECT_DIR"
        git fetch --tags
        git checkout "$PJPROJECT_TAG"
        git clean -fdx
        cd "$REPO_ROOT"
    else
        echo "[1/5] Cloning pjproject tag $PJPROJECT_TAG..."
        git clone --depth 1 --branch "$PJPROJECT_TAG" \
            "https://github.com/pjsip/pjproject.git" "$PJPROJECT_DIR"
    fi
else
    echo "[1/5] Skipping clone (--skip-clone)."
    if [[ ! -d "$PJPROJECT_DIR" ]]; then
        echo "ERROR: pjproject directory not found at $PJPROJECT_DIR"
        exit 1
    fi
fi

# ---------------------------------------------------------------------------
# Step 2 — Copy config_site.h
# ---------------------------------------------------------------------------

echo "[2/5] Copying config_site_ios.h -> config_site.h..."

CONFIG_SITE_SRC="$CONFIG_SITE_DIR/config_site_ios.h"
CONFIG_SITE_DST="$PJPROJECT_DIR/pjlib/include/pj/config_site.h"

if [[ ! -f "$CONFIG_SITE_SRC" ]]; then
    echo "ERROR: Config site header not found: $CONFIG_SITE_SRC"
    exit 1
fi

cp "$CONFIG_SITE_SRC" "$CONFIG_SITE_DST"
echo "  -> $CONFIG_SITE_DST"

# ---------------------------------------------------------------------------
# Step 3 — Configure and build
# ---------------------------------------------------------------------------

echo "[3/5] Configuring pjproject for iOS $ARCH (min $IOS_MIN_VERSION)..."

cd "$PJPROJECT_DIR"

# Clean any previous build
make distclean 2>/dev/null || true

# iOS cross-compilation flags
export DEVPATH="$XCODE_DEVELOPER/Platforms/iPhoneOS.platform/Developer"
export CC="$(xcrun --sdk iphoneos -f clang)"
export CXX="$(xcrun --sdk iphoneos -f clang++)"
export AR="$(xcrun --sdk iphoneos -f ar)"
export RANLIB="$(xcrun --sdk iphoneos -f ranlib)"

export CFLAGS="-arch $ARCH -isysroot $IOS_SDK -miphoneos-version-min=$IOS_MIN_VERSION -fembed-bitcode -fPIC"
export CXXFLAGS="-arch $ARCH -isysroot $IOS_SDK -miphoneos-version-min=$IOS_MIN_VERSION -fembed-bitcode -fPIC -std=c++14"
export LDFLAGS="-arch $ARCH -isysroot $IOS_SDK -miphoneos-version-min=$IOS_MIN_VERSION -framework AudioToolbox -framework AVFoundation -framework CoreAudio -framework Security"

./configure \
    --host="$TARGET_TRIPLE" \
    --disable-ssl \
    --enable-shared \
    --disable-video \
    --disable-v4l2 \
    --with-external-pa=no

# The Darwin SSL implementation is enabled via config_site.h (PJ_SSL_SOCK_IMP_DARWIN).
# We pass --disable-ssl to skip OpenSSL detection; Secure Transport is linked via frameworks.

echo "[3/5] Building pjproject..."

make dep
make -j"$(sysctl -n hw.logicalcpu)"

echo "  Build succeeded."

# ---------------------------------------------------------------------------
# Step 4 — Run SWIG to generate C# wrapper
# ---------------------------------------------------------------------------

echo "[4/5] Running SWIG to generate C# interop wrapper..."

SWIG_INTERFACE="$PJPROJECT_DIR/pjsip-apps/src/swig/pjsua2.i"

if command -v swig &>/dev/null && [[ -f "$SWIG_INTERFACE" ]]; then
    mkdir -p "$SWIG_OUTPUT_DIR"

    swig -csharp \
        -c++ \
        -namespace PjSip.Net.Interop.Generated \
        -outdir "$SWIG_OUTPUT_DIR" \
        -o "$PJPROJECT_DIR/pjsip-apps/src/swig/csharp/pjsua2_wrap.cpp" \
        -I"$PJPROJECT_DIR/pjlib/include" \
        -I"$PJPROJECT_DIR/pjlib-util/include" \
        -I"$PJPROJECT_DIR/pjnath/include" \
        -I"$PJPROJECT_DIR/pjmedia/include" \
        -I"$PJPROJECT_DIR/pjsip/include" \
        "$SWIG_INTERFACE"

    echo "  SWIG generation complete -> $SWIG_OUTPUT_DIR"
else
    echo "  WARNING: SWIG not found or interface file missing. Skipping SWIG step."
fi

# ---------------------------------------------------------------------------
# Step 5 — Copy native binary to runtimes folder
# ---------------------------------------------------------------------------

echo "[5/5] Copying libpjsua2.dylib to native package runtimes folder..."

# Locate the built dylib
BUILT_DYLIB=""
for candidate in \
    "$PJPROJECT_DIR/pjsip-apps/lib/libpjsua2.dylib" \
    "$PJPROJECT_DIR/lib/libpjsua2.dylib" \
    "$PJPROJECT_DIR/pjsip-apps/build/output/lib/libpjsua2.dylib"; do
    if [[ -f "$candidate" ]]; then
        BUILT_DYLIB="$candidate"
        break
    fi
done

if [[ -z "$BUILT_DYLIB" ]]; then
    echo "WARNING: Could not locate built libpjsua2.dylib."
    echo "  You may need to copy it manually to: $RUNTIME_DIR"
else
    mkdir -p "$RUNTIME_DIR"
    cp "$BUILT_DYLIB" "$RUNTIME_DIR/libpjsua2.dylib"
    echo "  -> $RUNTIME_DIR/libpjsua2.dylib"

    # Fix the install name for iOS loading
    install_name_tool -id "@rpath/libpjsua2.dylib" "$RUNTIME_DIR/libpjsua2.dylib" 2>/dev/null || true
fi

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

cd "$REPO_ROOT"

echo ""
echo "=== Build complete ==="
echo "Native binary location: $RUNTIME_DIR"
echo "SWIG output location  : $SWIG_OUTPUT_DIR"
