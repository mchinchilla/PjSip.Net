#!/usr/bin/env bash
# build-macos.sh — Build PJSIP 2.16 native libpjsua2.dylib for macOS (x64 + arm64)
# Prerequisites: Xcode Command Line Tools, SWIG 4.x, OpenSSL via Homebrew
#
# Usage: ./native/build-macos.sh [--arch arm64|x64|universal]

set -euo pipefail

ARCH="${1:---arch}"
ARCH_VAL="${2:-universal}"
if [[ "$ARCH" == "--arch" ]]; then ARCH_VAL="${2:-universal}"; fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PJ_DIR="$REPO_ROOT/pjproject"
NATIVE_DIR="$REPO_ROOT/src/PjSip.Net.Native.MacOS/runtimes"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# --- Clone pjproject ---
if [ ! -d "$PJ_DIR" ]; then
    echo "Cloning pjproject 2.16..."
    git clone --branch 2.16 --depth 1 https://github.com/pjsip/pjproject.git "$PJ_DIR"
fi

# --- Copy config_site.h ---
cp "$SCRIPT_DIR/config_site.h" "$PJ_DIR/pjlib/include/pj/config_site.h"
echo "Copied config_site.h"

# --- OpenSSL from Homebrew ---
OPENSSL_PREFIX="$(brew --prefix openssl@3 2>/dev/null || echo "")"
if [ -z "$OPENSSL_PREFIX" ]; then
    echo "WARNING: OpenSSL not found via Homebrew. Install with: brew install openssl@3"
    echo "Building without TLS support."
    SSL_FLAGS=""
else
    SSL_FLAGS="--with-ssl=$OPENSSL_PREFIX"
fi

build_arch() {
    local target_arch="$1"
    local rid="$2"
    local output_dir="$NATIVE_DIR/$rid/native"

    echo ""
    echo "=== Building for $target_arch ($rid) ==="

    cd "$PJ_DIR"
    make distclean 2>/dev/null || true

    if [ "$target_arch" = "arm64" ]; then
        CFLAGS="-arch arm64" LDFLAGS="-arch arm64" \
            ./configure --host=arm-apple-darwin $SSL_FLAGS
    else
        CFLAGS="-arch x86_64" LDFLAGS="-arch x86_64" \
            ./configure --host=x86_64-apple-darwin $SSL_FLAGS
    fi

    make dep
    make -j"$(sysctl -n hw.logicalcpu)"

    # --- SWIG wrapper ---
    echo "Running SWIG..."
    cd "$PJ_DIR/pjsip-apps/src/swig"
    make -j1

    # --- Build shared library ---
    echo "Linking libpjsua2.dylib..."
    cd "$PJ_DIR/pjsip-apps/src/swig/csharp"

    local pj_libs=$(find "$PJ_DIR/lib" -name "*.a" | tr '\n' ' ')

    mkdir -p "$output_dir"

    g++ -shared -o "$output_dir/libpjsua2.dylib" \
        pjsua2_wrap.o \
        $pj_libs \
        -framework CoreAudio -framework AudioToolbox -framework AVFoundation \
        -framework CoreMedia -framework Security -framework SystemConfiguration \
        -framework Foundation -framework Network \
        $([ -n "$OPENSSL_PREFIX" ] && echo "-L$OPENSSL_PREFIX/lib -lssl -lcrypto") \
        -lpthread

    echo "SUCCESS: $output_dir/libpjsua2.dylib"
    ls -lh "$output_dir/libpjsua2.dylib"
}

# --- Build requested architecture(s) ---
case "$ARCH_VAL" in
    arm64)
        build_arch "arm64" "osx-arm64"
        ;;
    x64)
        build_arch "x86_64" "osx-x64"
        ;;
    universal)
        build_arch "arm64" "osx-arm64"
        build_arch "x86_64" "osx-x64"
        ;;
    *)
        echo "Usage: $0 --arch [arm64|x64|universal]"
        exit 1
        ;;
esac

echo ""
echo "=== macOS build complete ==="
