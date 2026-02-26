#!/usr/bin/env bash
# build-ios.sh — Build PJSIP 2.16 native libpjsua2.dylib for iOS arm64
# Prerequisites: macOS with Xcode, SWIG 4.x
#
# Usage: ./native/build-ios.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PJ_DIR="$REPO_ROOT/pjproject"
OUTPUT_DIR="$REPO_ROOT/src/PjSip.Net.Native.iOS/runtimes/ios-arm64/native"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# --- Check we're on macOS ---
if [ "$(uname)" != "Darwin" ]; then
    echo "ERROR: iOS builds require macOS with Xcode."
    exit 1
fi

# --- Clone pjproject ---
if [ ! -d "$PJ_DIR" ]; then
    echo "Cloning pjproject 2.16..."
    git clone --branch 2.16 --depth 1 https://github.com/pjsip/pjproject.git "$PJ_DIR"
fi

# --- Copy config_site.h ---
cp "$SCRIPT_DIR/config_site.h" "$PJ_DIR/pjlib/include/pj/config_site.h"
echo "Copied config_site.h"

# --- Apply patches ---
PATCHES_DIR="$SCRIPT_DIR/patches"
if [ -d "$PATCHES_DIR" ]; then
    for patch in "$PATCHES_DIR"/*.patch; do
        [ -f "$patch" ] && echo "Applying patch: $(basename "$patch")" && git -C "$PJ_DIR" apply "$patch"
    done
fi

# --- Find iOS SDK ---
IOS_SDK="$(xcrun --sdk iphoneos --show-sdk-path)"
IOS_MIN_VERSION="16.0"
echo "Using iOS SDK: $IOS_SDK"

# --- Configure for iOS ---
cd "$PJ_DIR"
make distclean 2>/dev/null || true

ARCH="-arch arm64"
export CFLAGS="$ARCH -isysroot $IOS_SDK -miphoneos-version-min=$IOS_MIN_VERSION -fembed-bitcode"
export CXXFLAGS="$CFLAGS"
export LDFLAGS="$ARCH -isysroot $IOS_SDK -miphoneos-version-min=$IOS_MIN_VERSION"

./configure \
    --host=aarch64-apple-darwin \
    --disable-sdl \
    --disable-ffmpeg \
    --disable-v4l2 \
    --disable-openh264 \
    --disable-vpx \
    --disable-libyuv

make dep
make -j"$(sysctl -n hw.logicalcpu)"

# --- SWIG ---
# Note: SWIG runs on the host (macOS), not cross-compiled
echo "Running SWIG..."
cd "$PJ_DIR/pjsip-apps/src/swig"

# For iOS cross-compile, we run swig manually since the Makefile may try to link for host
cd csharp
swig -I"$PJ_DIR/pjlib/include" \
     -I"$PJ_DIR/pjlib-util/include" \
     -I"$PJ_DIR/pjmedia/include" \
     -I"$PJ_DIR/pjsip/include" \
     -I"$PJ_DIR/pjnath/include" \
     -w312 -c++ -csharp \
     -dllimport pjsua2 \
     -namespace PjSip.Net.Interop.Generated \
     -o pjsua2_wrap.cpp \
     ../pjsua2.i

# Compile wrapper object
xcrun -sdk iphoneos clang++ \
    -arch arm64 \
    -isysroot "$IOS_SDK" \
    -miphoneos-version-min="$IOS_MIN_VERSION" \
    -I"$PJ_DIR/pjlib/include" \
    -I"$PJ_DIR/pjlib-util/include" \
    -I"$PJ_DIR/pjmedia/include" \
    -I"$PJ_DIR/pjsip/include" \
    -I"$PJ_DIR/pjnath/include" \
    -c pjsua2_wrap.cpp -o pjsua2_wrap.o

# --- Link shared library ---
echo "Linking libpjsua2.dylib for iOS arm64..."

pj_libs=$(find "$PJ_DIR/lib" -name "*.a" | tr '\n' ' ')

mkdir -p "$OUTPUT_DIR"

xcrun -sdk iphoneos clang++ \
    -arch arm64 \
    -isysroot "$IOS_SDK" \
    -miphoneos-version-min="$IOS_MIN_VERSION" \
    -dynamiclib \
    -install_name @rpath/libpjsua2.dylib \
    -o "$OUTPUT_DIR/libpjsua2.dylib" \
    pjsua2_wrap.o \
    $pj_libs \
    -framework CoreAudio -framework AudioToolbox -framework AVFoundation \
    -framework CoreMedia -framework Security -framework SystemConfiguration \
    -framework Foundation -framework Network -framework UIKit \
    -lpthread

echo ""
echo "SUCCESS: $OUTPUT_DIR/libpjsua2.dylib"
ls -lh "$OUTPUT_DIR/libpjsua2.dylib"
