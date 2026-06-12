#!/usr/bin/env bash
# build-android.sh — Build PJSIP 2.16 native libpjsua2.so for Android arm64
# Prerequisites: Android NDK (via Android Studio or standalone)
#
# Usage: ./native/build-android.sh [--ndk /path/to/ndk]
#
# If --ndk is not provided, the script looks for ANDROID_NDK_ROOT or
# the default Android Studio NDK location.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PJ_DIR="$REPO_ROOT/pjproject"
OUTPUT_DIR="$REPO_ROOT/src/PjSip.Net.Native.Android/runtimes/android-arm64/native"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OPENSSL_INSTALL="$REPO_ROOT/openssl-install"
TARGET_ABI="arm64-v8a"
ANDROID_API=28
OPENSSL_VERSION="3.4.1"

# --- Parse args ---
NDK_ROOT=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --ndk) NDK_ROOT="$2"; shift 2 ;;
        *) echo "Unknown arg: $1"; exit 1 ;;
    esac
done

# --- Find NDK ---
if [ -z "$NDK_ROOT" ]; then
    NDK_ROOT="${ANDROID_NDK_ROOT:-""}"
fi
if [ -z "$NDK_ROOT" ]; then
    # Try default Android Studio locations
    for candidate in \
        "$HOME/Android/Sdk/ndk/"* \
        "$HOME/Library/Android/sdk/ndk/"* \
        "/usr/local/lib/android/sdk/ndk/"*; do
        if [ -d "$candidate" ]; then
            NDK_ROOT="$candidate"
            break
        fi
    done
fi
if [ -z "$NDK_ROOT" ] || [ ! -d "$NDK_ROOT" ]; then
    echo "ERROR: Android NDK not found."
    echo "Set ANDROID_NDK_ROOT or pass --ndk /path/to/ndk"
    exit 1
fi
echo "Using NDK: $NDK_ROOT"

# --- Build OpenSSL for Android arm64 ---
if [ ! -f "$OPENSSL_INSTALL/lib/libssl.a" ]; then
    echo "Building OpenSSL $OPENSSL_VERSION for Android arm64..."
    cd "$REPO_ROOT"
    if [ ! -d "openssl-${OPENSSL_VERSION}" ]; then
        curl -sL "https://github.com/openssl/openssl/releases/download/openssl-${OPENSSL_VERSION}/openssl-${OPENSSL_VERSION}.tar.gz" -o openssl.tar.gz
        tar xzf openssl.tar.gz
        rm openssl.tar.gz
    fi
    cd "openssl-${OPENSSL_VERSION}"
    export ANDROID_NDK_ROOT="$NDK_ROOT"
    export PATH="$NDK_ROOT/toolchains/llvm/prebuilt/$(uname -s | tr '[:upper:]' '[:lower:]')-x86_64/bin:$PATH"
    ./Configure android-arm64 -D__ANDROID_API__=$ANDROID_API \
        --prefix="$OPENSSL_INSTALL" \
        no-shared no-tests
    make -j"$(nproc 2>/dev/null || sysctl -n hw.logicalcpu 2>/dev/null || echo 4)"
    make install_sw
    echo "OpenSSL installed to $OPENSSL_INSTALL"
fi

# --- Clone pjproject ---
if [ ! -d "$PJ_DIR" ]; then
    echo "Cloning pjproject 2.16..."
    git clone --branch 2.16 --depth 1 https://github.com/pjsip/pjproject.git "$PJ_DIR"
fi

# --- Copy config_site.h ---
cp "$SCRIPT_DIR/config_site.h" "$PJ_DIR/pjlib/include/pj/config_site.h"
echo "Copied config_site.h"

# --- Configure for Android ---
cd "$PJ_DIR"
make distclean 2>/dev/null || true

export ANDROID_NDK_ROOT="$NDK_ROOT"
export TARGET_ABI="$TARGET_ABI"

# pjproject has a configure-android script
./configure-android \
    --use-ndk-cflags \
    --with-ssl="$OPENSSL_INSTALL"

make dep
make -j"$(nproc 2>/dev/null || sysctl -n hw.logicalcpu 2>/dev/null || echo 4)"

# --- SWIG ---
echo "Running SWIG..."
cd "$PJ_DIR/pjsip-apps/src/swig"
make

# --- Build shared library ---
echo "Linking libpjsua2.so..."
cd "$PJ_DIR/pjsip-apps/src/swig/csharp"

TOOLCHAIN="$NDK_ROOT/toolchains/llvm/prebuilt/$(uname -s | tr '[:upper:]' '[:lower:]')-x86_64"
CC="$TOOLCHAIN/bin/aarch64-linux-android${ANDROID_API}-clang++"

pj_libs=$(find "$PJ_DIR/lib" -name "*.a" | tr '\n' ' ')

mkdir -p "$OUTPUT_DIR"

# -z max-page-size=16384: Android 15+ devices can boot with 16 KB page-size kernels
# (e.g. recent OnePlus); a 4 KB-aligned .so fails to load there and the app crashes at
# startup. Also a Google Play requirement for targetSdk 35+. NDK r28+ defaults to this,
# but pass it explicitly so the output is correct on any NDK version.
$CC -shared -o "$OUTPUT_DIR/libpjsua2.so" \
    -Wl,-z,max-page-size=16384 \
    pjsua2_wrap.o \
    $pj_libs \
    -L"$OPENSSL_INSTALL/lib" -lssl -lcrypto \
    -lOpenSLES -llog -landroid -lmediandk \
    -lpthread -lm

echo ""
echo "SUCCESS: $OUTPUT_DIR/libpjsua2.so"
ls -lh "$OUTPUT_DIR/libpjsua2.so"
