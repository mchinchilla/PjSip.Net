#!/usr/bin/env bash
#
# build-macos-arm64.sh — Build PJSIP pjsua2 shared library for macOS arm64 (Apple Silicon).
#
# This script:
#   1. Clones pjproject 2.16 if not already present
#   2. Copies config_site_macos.h to config_site.h
#   3. Configures and builds pjproject for arm64 with OpenSSL (Homebrew)
#   4. Runs SWIG to generate the C# interop wrapper
#   5. Copies libpjsua2.dylib to the native NuGet package runtimes folder
#
# Usage:
#   ./build-macos-arm64.sh [--skip-clone] [--tag 2.16]
#

set -euo pipefail

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------

PJPROJECT_TAG="2.16"
SKIP_CLONE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-clone)  SKIP_CLONE=true; shift ;;
        --tag)         PJPROJECT_TAG="$2"; shift 2 ;;
        *)             echo "Unknown option: $1"; exit 1 ;;
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

NATIVE_PKG_DIR="$REPO_ROOT/src/PjSip.Net.Native.MacOS"
RUNTIME_DIR="$NATIVE_PKG_DIR/runtimes/osx-arm64/native"

INTEROP_DIR="$REPO_ROOT/src/PjSip.Net.Interop"
SWIG_OUTPUT_DIR="$INTEROP_DIR/Generated"

ARCH="arm64"
TARGET_TRIPLE="aarch64-apple-darwin"

echo "=== PjSip.Net Native Build — macOS arm64 (Apple Silicon) ==="
echo "Repository root : $REPO_ROOT"
echo "pjproject source: $PJPROJECT_DIR"
echo "Output runtime  : $RUNTIME_DIR"
echo ""

# ---------------------------------------------------------------------------
# Detect OpenSSL (Homebrew)
# ---------------------------------------------------------------------------

if [[ -d "/opt/homebrew/opt/openssl@3" ]]; then
    OPENSSL_DIR="/opt/homebrew/opt/openssl@3"
elif [[ -d "/opt/homebrew/opt/openssl@1.1" ]]; then
    OPENSSL_DIR="/opt/homebrew/opt/openssl@1.1"
elif [[ -d "/opt/homebrew/opt/openssl" ]]; then
    OPENSSL_DIR="/opt/homebrew/opt/openssl"
else
    echo "ERROR: OpenSSL not found. Install with: brew install openssl@3"
    exit 1
fi

echo "Using OpenSSL: $OPENSSL_DIR"

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

echo "[2/5] Copying config_site_macos.h -> config_site.h..."

CONFIG_SITE_SRC="$CONFIG_SITE_DIR/config_site_macos.h"
CONFIG_SITE_DST="$PJPROJECT_DIR/pjlib/include/pj/config_site.h"

if [[ ! -f "$CONFIG_SITE_SRC" ]]; then
    echo "ERROR: Config site header not found: $CONFIG_SITE_SRC"
    exit 1
fi

cp "$CONFIG_SITE_SRC" "$CONFIG_SITE_DST"
echo "  -> $CONFIG_SITE_DST"

# Apply patches from native/patches/
PATCHES_DIR="$NATIVE_ROOT/patches"
if [[ -d "$PATCHES_DIR" ]]; then
    for patch in "$PATCHES_DIR"/*.patch; do
        [[ -f "$patch" ]] && echo "  Applying patch: $(basename "$patch")" && git -C "$PJPROJECT_DIR" apply "$patch"
    done
fi

# ---------------------------------------------------------------------------
# Step 3 — Configure and build
# ---------------------------------------------------------------------------

echo "[3/5] Configuring pjproject for $ARCH with OpenSSL..."

cd "$PJPROJECT_DIR"

# Clean any previous build
make distclean 2>/dev/null || true

# Set compiler flags for arm64
export CFLAGS="-arch $ARCH -mmacosx-version-min=12.0 -fPIC"
export CXXFLAGS="-arch $ARCH -mmacosx-version-min=12.0 -fPIC"
export LDFLAGS="-arch $ARCH -mmacosx-version-min=12.0"

./configure \
    --host="$TARGET_TRIPLE" \
    --with-ssl="$OPENSSL_DIR" \
    --enable-shared \
    --disable-video \
    --disable-v4l2 \
    --disable-sound \
    --with-external-pa=no

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

    # Fix the install name so the loader finds the library next to the app
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
