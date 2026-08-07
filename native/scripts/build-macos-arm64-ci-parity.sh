#!/usr/bin/env bash
#
# build-macos-arm64-ci-parity.sh — local macOS arm64 native build that mirrors the
# GitHub Actions recipe in .github/workflows/native-build.yml (job build-macos-arm64).
#
# Why this exists alongside build-macos-arm64.sh: that older script uses
# config_site/config_site_macos.h and passes --with-ssl=<homebrew openssl>, which swaps
# the TLS backend from Apple SecureTransport to OpenSSL and changes PJSUA_MAX_CALLS,
# PJ_LOG_MAX_LEVEL and more. The result is a materially different library from the one
# CI publishes. This script follows CI instead: native/config_site.h verbatim, no
# --with-ssl, bcg729 statically linked, per-module make, same SWIG invocation and link.
#
# One thing this script does that CI does not: it pins MACOSX_DEPLOYMENT_TARGET rather
# than inheriting the build machine's. CI lands on 14.0 because its runner is macos-14;
# a local build on macOS 26 would otherwise emit minos 26.0 and refuse to load anywhere
# older, while the app declares support from macOS 15. Verified against the produced
# binary at the end, along with G.729 and the txt_cnt patch — assume nothing.
#
# Usage:  bash build-macos-arm64-ci-parity.sh

set -euo pipefail

PJPROJECT_TAG="2.16"
BCG729_TAG="1.1.1"
ARCH="arm64"

# Pinned, not inherited. Without this the deployment target defaults to whatever macOS
# the build machine runs, so a dylib built here on macOS 26 gets minos 26.0 and refuses
# to load on anything older — while the app declares support from macOS 15. CI produces
# 14.0 only because its runner is macos-14; matching that keeps the two interchangeable.
export MACOSX_DEPLOYMENT_TARGET="14.0"
MIN_VERSION_FLAG="-mmacosx-version-min=$MACOSX_DEPLOYMENT_TARGET"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
NATIVE_ROOT="$REPO_ROOT/native"
PJ="$NATIVE_ROOT/pjproject"
SWIG_DIR="$PJ/pjsip-apps/src/swig/csharp"
OUT_DIR="$REPO_ROOT/src/PjSip.Net.Native.MacOS/runtimes/osx-arm64/native"

BUILD_ROOT="${TMPDIR:-/tmp}/pjsipnet-macos-arm64"
BCG729_SRC="$BUILD_ROOT/bcg729"
BCG729_PREFIX="$BUILD_ROOT/bcg729-install"

echo "=== CI-parity native build — macOS $ARCH, WITH bcg729 (G.729) ==="
echo "pjproject : $PJ"
echo "bcg729    : $BCG729_PREFIX"
echo "output    : $OUT_DIR"
echo ""

# --- 1. bcg729 (G.729) ------------------------------------------------------
# Required: native/config_site.h sets PJMEDIA_HAS_BCG729 1 for every platform, and
# G.729 is needed for interop with PBXs that offer nothing else. Never omit it.
if [[ -f "$BCG729_PREFIX/lib/libbcg729.a" ]]; then
    echo "[1/8] bcg729 already built, reusing."
else
    echo "[1/8] Building bcg729 $BCG729_TAG (cmake, static — same as CI) ..."
    command -v cmake >/dev/null || { echo "ERROR: cmake not found (brew install cmake)"; exit 1; }
    rm -rf "$BCG729_SRC" "$BCG729_PREFIX" "$BUILD_ROOT/bcg729-build"
    mkdir -p "$BUILD_ROOT"
    git clone --branch "$BCG729_TAG" --depth 1 -q \
        https://github.com/BelledonneCommunications/bcg729.git "$BCG729_SRC"

    cmake -S "$BCG729_SRC" -B "$BUILD_ROOT/bcg729-build" \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$MACOSX_DEPLOYMENT_TARGET" \
        -DCMAKE_INSTALL_PREFIX="$BCG729_PREFIX" \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -DBUILD_SHARED_LIBS=OFF \
        -DENABLE_TESTS=OFF > /tmp/bcg729-cmake.log 2>&1 \
        || { tail -25 /tmp/bcg729-cmake.log; exit 1; }
    cmake --build "$BUILD_ROOT/bcg729-build" --config Release >> /tmp/bcg729-cmake.log 2>&1 \
        || { tail -25 /tmp/bcg729-cmake.log; exit 1; }
    cmake --install "$BUILD_ROOT/bcg729-build" --config Release >> /tmp/bcg729-cmake.log 2>&1 \
        || { tail -25 /tmp/bcg729-cmake.log; exit 1; }

    # PjSip.Net's loader needs these six exported, or the phone silently falls back
    # to stub mode instead of failing loudly.
    # Symbols are captured once, then matched. Never `nm ... | grep -q`: grep exits on
    # the first hit, nm dies of SIGPIPE, and under `set -o pipefail` the pipeline reports
    # failure even though the symbol WAS found — a check that fails on success.
    BCG_SYMS=$(nm "$BCG729_PREFIX/lib/libbcg729.a")
    for sym in initBcg729DecoderChannel closeBcg729DecoderChannel bcg729Decoder \
               initBcg729EncoderChannel closeBcg729EncoderChannel bcg729Encoder; do
        grep -q "T _$sym\$" <<< "$BCG_SYMS" \
            || { echo "ERROR: bcg729 is missing symbol $sym"; exit 1; }
    done
    echo "   verified: all 6 bcg729 entry points present"
fi

# --- 2. Clean checkout ------------------------------------------------------
echo "[2/8] Resetting pjproject to $PJPROJECT_TAG ..."
git -C "$PJ" checkout -- .
git -C "$PJ" checkout "$PJPROJECT_TAG" 2>/dev/null || true
git -C "$PJ" clean -fdx -q

# --- 3. Patches (same loop as CI) -------------------------------------------
echo "[3/8] Applying patches ..."
for patch in "$NATIVE_ROOT"/patches/*.patch; do
    [[ -f "$patch" ]] || continue
    echo "   $(basename "$patch")"
    git -C "$PJ" apply "$patch"
done
grep -q "opt->txt_cnt = 0;" "$PJ/pjsip/src/pjsua-lib/pjsua_call.c" \
    || { echo "ERROR: txt_cnt patch is not present in the source tree"; exit 1; }
echo "   verified: opt->txt_cnt = 0 is in pjsua_call.c"

# --- 4. config_site.h — CI's, unmodified ------------------------------------
echo "[4/8] Installing native/config_site.h ..."
cp "$NATIVE_ROOT/config_site.h" "$PJ/pjlib/include/pj/config_site.h"

# --- 5. Configure (CI passes no --with-ssl: Apple SecureTransport) ----------
echo "[5/8] configure ..."
cd "$PJ"
./configure --with-bcg729="$BCG729_PREFIX" > /tmp/pj-configure.log 2>&1 \
    || { tail -30 /tmp/pj-configure.log; exit 1; }
grep -qi "bcg729.*yes\|Using bcg729" /tmp/pj-configure.log \
    && echo "   configure picked up bcg729" \
    || echo "   NOTE: configure log did not confirm bcg729 — verified again after link"
make dep > /tmp/pj-dep.log 2>&1 || { tail -30 /tmp/pj-dep.log; exit 1; }

# --- 6. Build each module (CI tolerates test-binary link failures) ----------
echo "[6/8] Building libraries ..."
CPUS=$(sysctl -n hw.logicalcpu)
for dir in pjlib pjlib-util pjnath third_party pjmedia pjsip; do
    echo "   === $dir"
    make -C "$dir/build" -j"$CPUS" > "/tmp/pj-build-$dir.log" 2>&1 \
        || echo "   (warnings/test-binary failures in $dir — continuing, as CI does)"
done
test -n "$(find . -name '*.a' -path '*/lib/*' -print -quit)" \
    || { echo "ERROR: no static libraries were produced"; exit 1; }

# --- 7. SWIG + compile + link ----------------------------------------------
echo "[7/8] SWIG, compile, link ..."
cd "$SWIG_DIR"
swig -I"$PJ/pjlib/include" -I"$PJ/pjlib-util/include" -I"$PJ/pjmedia/include" \
     -I"$PJ/pjsip/include" -I"$PJ/pjnath/include" \
     -w312 -c++ -csharp -dllimport pjsua2 \
     -namespace PjSip.Net.Interop.Generated \
     -o pjsua2_wrap.cpp ../pjsua2.i

g++ -c -std=c++14 -arch "$ARCH" $MIN_VERSION_FLAG -O2 \
    -I"$PJ/pjlib/include" -I"$PJ/pjlib-util/include" -I"$PJ/pjmedia/include" \
    -I"$PJ/pjsip/include" -I"$PJ/pjnath/include" \
    -DPJ_AUTOCONF=1 pjsua2_wrap.cpp -o pjsua2_wrap.o

# Same as CI: whatever order `find` returns. Tested both ways on 2026-08-06 — the raw
# order and an explicit dependency order both produce a library with G.729 linked in,
# because Apple's linker re-scans archives until no new members are pulled. An earlier
# revision of this script reordered them to chase a "G.729 missing" error that turned
# out to come from the verification below, not the link.
PJ_LIBS=$(find "$PJ" -name "*.a" -path "*/lib/*" | tr '\n' ' ')

g++ -shared -arch "$ARCH" $MIN_VERSION_FLAG -o libpjsua2.dylib \
    pjsua2_wrap.o $PJ_LIBS "$BCG729_PREFIX/lib/libbcg729.a" \
    -framework CoreAudio -framework AudioToolbox -framework AVFoundation \
    -framework CoreMedia -framework Security -framework SystemConfiguration \
    -framework Foundation -framework Network \
    -lpthread

# --- 8. Verify, then install ------------------------------------------------
echo "[8/8] Verifying ..."

# bcg729 must be linked IN, not referenced dynamically (CI asserts the same).
# Captured once — see the note above about `nm | grep -q` under pipefail.
DYLIB_SYMS=$(nm libpjsua2.dylib)
DYLIB_DEPS=$(otool -L libpjsua2.dylib)

grep -qi bcg729 <<< "$DYLIB_DEPS" \
    && { echo "ERROR: bcg729 is a dynamic dependency, expected static"; exit 1; }

for sym in bcg729Decoder bcg729Encoder pjmedia_codec_bcg729_init; do
    grep -q "$sym" <<< "$DYLIB_SYMS" \
        || { echo "ERROR: G.729 missing from the built library ($sym)"; exit 1; }
done
echo "   G.729: bcg729 statically linked, codec registered"

ACTUAL_MIN=$(vtool -show-build libpjsua2.dylib 2>/dev/null | awk '/minos/{print $2}')
[[ "$ACTUAL_MIN" == "$MACOSX_DEPLOYMENT_TARGET" ]] \
    || { echo "ERROR: deployment target is $ACTUAL_MIN, expected $MACOSX_DEPLOYMENT_TARGET"; exit 1; }
echo "   deployment target: macOS $ACTUAL_MIN"

mkdir -p "$OUT_DIR"
cp libpjsua2.dylib "$OUT_DIR/"

echo ""
echo "=== Done ==="
ls -la "$OUT_DIR/libpjsua2.dylib"
file "$OUT_DIR/libpjsua2.dylib"
