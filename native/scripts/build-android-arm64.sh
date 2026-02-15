#!/usr/bin/env bash
#
# build-android-arm64.sh — Build PJSIP pjsua2 shared library for Android arm64-v8a.
#
# This script:
#   1. Clones pjproject 2.16 if not already present
#   2. Copies config_site_android.h to config_site.h
#   3. Configures and builds pjproject using the Android NDK toolchain
#   4. Runs SWIG to generate the C# interop wrapper
#   5. Copies libpjsua2.so to the native NuGet package runtimes folder
#
# Prerequisites:
#   - Android NDK installed (set ANDROID_NDK_HOME or ANDROID_NDK_ROOT)
#   - OpenSSL for Android arm64 prebuilt (set OPENSSL_ANDROID_DIR or it will
#     be built from source)
#   - SWIG 4.0+ (optional, for C# wrapper generation)
#
# Usage:
#   ./build-android-arm64.sh [--skip-clone] [--tag 2.16] [--ndk /path/to/ndk]
#

set -euo pipefail

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------

PJPROJECT_TAG="2.16"
SKIP_CLONE=false
NDK_OVERRIDE=""
API_LEVEL=24

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-clone)  SKIP_CLONE=true; shift ;;
        --tag)         PJPROJECT_TAG="$2"; shift 2 ;;
        --ndk)         NDK_OVERRIDE="$2"; shift 2 ;;
        --api)         API_LEVEL="$2"; shift 2 ;;
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

NATIVE_PKG_DIR="$REPO_ROOT/src/PjSip.Net.Native.Android"
RUNTIME_DIR="$NATIVE_PKG_DIR/runtimes/android-arm64/native"

INTEROP_DIR="$REPO_ROOT/src/PjSip.Net.Interop"
SWIG_OUTPUT_DIR="$INTEROP_DIR/Generated"

# Android-specific
TARGET_ABI="arm64-v8a"
TARGET_ARCH="aarch64"
TARGET_HOST="aarch64-linux-android"

echo "=== PjSip.Net Native Build — Android arm64-v8a ==="
echo "Repository root : $REPO_ROOT"
echo "pjproject source: $PJPROJECT_DIR"
echo "Output runtime  : $RUNTIME_DIR"
echo ""

# ---------------------------------------------------------------------------
# Detect Android NDK
# ---------------------------------------------------------------------------

if [[ -n "$NDK_OVERRIDE" ]]; then
    NDK_HOME="$NDK_OVERRIDE"
elif [[ -n "${ANDROID_NDK_HOME:-}" ]]; then
    NDK_HOME="$ANDROID_NDK_HOME"
elif [[ -n "${ANDROID_NDK_ROOT:-}" ]]; then
    NDK_HOME="$ANDROID_NDK_ROOT"
elif [[ -n "${ANDROID_HOME:-}" && -d "${ANDROID_HOME}/ndk" ]]; then
    # Pick the latest NDK version installed
    NDK_HOME="$(ls -d "${ANDROID_HOME}/ndk/"* 2>/dev/null | sort -V | tail -1)"
else
    echo "ERROR: Android NDK not found."
    echo "  Set ANDROID_NDK_HOME, ANDROID_NDK_ROOT, or pass --ndk /path/to/ndk"
    exit 1
fi

if [[ ! -d "$NDK_HOME" ]]; then
    echo "ERROR: NDK directory does not exist: $NDK_HOME"
    exit 1
fi

echo "Using NDK: $NDK_HOME"

# Set up the NDK toolchain
TOOLCHAIN="$NDK_HOME/toolchains/llvm/prebuilt"
if [[ "$(uname -s)" == "Darwin" ]]; then
    TOOLCHAIN="$TOOLCHAIN/darwin-x86_64"
elif [[ "$(uname -s)" == "Linux" ]]; then
    TOOLCHAIN="$TOOLCHAIN/linux-x86_64"
else
    echo "ERROR: Unsupported host OS for Android NDK: $(uname -s)"
    exit 1
fi

export CC="$TOOLCHAIN/bin/${TARGET_HOST}${API_LEVEL}-clang"
export CXX="$TOOLCHAIN/bin/${TARGET_HOST}${API_LEVEL}-clang++"
export AR="$TOOLCHAIN/bin/llvm-ar"
export RANLIB="$TOOLCHAIN/bin/llvm-ranlib"
export STRIP="$TOOLCHAIN/bin/llvm-strip"

if [[ ! -f "$CC" ]]; then
    echo "ERROR: Clang not found at $CC"
    echo "  Verify NDK path and API level ($API_LEVEL)."
    exit 1
fi

echo "Using compiler: $CC"

# ---------------------------------------------------------------------------
# Detect OpenSSL for Android (optional)
# ---------------------------------------------------------------------------

OPENSSL_FLAGS=""
if [[ -n "${OPENSSL_ANDROID_DIR:-}" && -d "${OPENSSL_ANDROID_DIR}" ]]; then
    OPENSSL_FLAGS="--with-ssl=${OPENSSL_ANDROID_DIR}"
    echo "Using OpenSSL: $OPENSSL_ANDROID_DIR"
else
    echo "WARNING: OPENSSL_ANDROID_DIR not set. Building without OpenSSL."
    echo "  TLS will not be available unless you set OPENSSL_ANDROID_DIR."
    OPENSSL_FLAGS="--disable-ssl"
fi

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

echo "[2/5] Copying config_site_android.h -> config_site.h..."

CONFIG_SITE_SRC="$CONFIG_SITE_DIR/config_site_android.h"
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

echo "[3/5] Configuring pjproject for Android $TARGET_ABI (API $API_LEVEL)..."

cd "$PJPROJECT_DIR"

# Clean any previous build
make distclean 2>/dev/null || true

export CFLAGS="-fPIC"
export CXXFLAGS="-fPIC -std=c++14"
export LDFLAGS=""

./configure \
    --host="$TARGET_HOST" \
    $OPENSSL_FLAGS \
    --enable-shared \
    --disable-video \
    --disable-v4l2 \
    --with-external-pa=no

echo "[3/5] Building pjproject..."

make dep
make -j"$(nproc 2>/dev/null || sysctl -n hw.logicalcpu 2>/dev/null || echo 4)"

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

echo "[5/5] Copying libpjsua2.so to native package runtimes folder..."

# Locate the built .so
BUILT_SO=""
for candidate in \
    "$PJPROJECT_DIR/pjsip-apps/lib/libpjsua2.so" \
    "$PJPROJECT_DIR/lib/libpjsua2.so" \
    "$PJPROJECT_DIR/pjsip-apps/build/output/lib/libpjsua2.so"; do
    if [[ -f "$candidate" ]]; then
        BUILT_SO="$candidate"
        break
    fi
done

if [[ -z "$BUILT_SO" ]]; then
    echo "WARNING: Could not locate built libpjsua2.so."
    echo "  You may need to copy it manually to: $RUNTIME_DIR"
else
    mkdir -p "$RUNTIME_DIR"
    cp "$BUILT_SO" "$RUNTIME_DIR/libpjsua2.so"
    echo "  -> $RUNTIME_DIR/libpjsua2.so"

    # Strip debug symbols to reduce size
    "$STRIP" "$RUNTIME_DIR/libpjsua2.so" 2>/dev/null || true
fi

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

cd "$REPO_ROOT"

echo ""
echo "=== Build complete ==="
echo "Native binary location: $RUNTIME_DIR"
echo "SWIG output location  : $SWIG_OUTPUT_DIR"
