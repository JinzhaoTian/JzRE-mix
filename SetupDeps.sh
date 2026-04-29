#!/bin/bash
# SetupDeps.sh — Clone and build bgfx for JzRE-mix
# Usage: ./SetupDeps.sh [Debug|Release]

set -e
CONFIG="${1:-Debug}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
THIRDPARTY="$SCRIPT_DIR/Source/ThirdParty"
BGFX_DIR="$THIRDPARTY/bgfx.cmake"

echo "===== JzRE-mix: Dependency Setup ====="
echo ""

# ── Clone bgfx.cmake ─────────────────────────────────────────────────────
if [ ! -d "$BGFX_DIR" ]; then
    echo "[1/3] Cloning bgfx.cmake (this may take a minute)..."
    git clone --depth 1 https://github.com/bkaradzic/bgfx.cmake.git "$BGFX_DIR"
    cd "$BGFX_DIR"
    git submodule update --init --recursive --depth 1
    cd "$SCRIPT_DIR"
else
    echo "[1/3] bgfx.cmake already exists — skipping clone."
fi

# ── Build bgfx ────────────────────────────────────────────────────────────
echo "[2/3] Building bgfx (${CONFIG})..."

BUILD_DIR="$BGFX_DIR/build"
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

cmake .. -DCMAKE_BUILD_TYPE="$CONFIG" -DBGFX_BUILD_TOOLS=OFF -DBGFX_BUILD_EXAMPLES=OFF -DBGFX_BUILD_TESTS=OFF
cmake --build . --config "$CONFIG" --parallel

cd "$SCRIPT_DIR"

# ── Copy artifacts ────────────────────────────────────────────────────────
echo "[3/3] Staging bgfx artifacts..."

# Headers are used in-place from bgfx.cmake/{bgfx,bx,bimg}/include
# Libraries need to be findable by the toolchain
LIB_SRC="$BUILD_DIR"
LIB_DST="$THIRDPARTY/lib/$(uname -s)"
mkdir -p "$LIB_DST"

# CMake puts libraries in different locations depending on platform
if [ -f "$LIB_SRC/libbgfx.a" ]; then
    cp "$LIB_SRC/libbgfx.a" "$LIB_SRC/libbx.a" "$LIB_SRC/libbimg.a" "$LIB_DST/" 2>/dev/null || true
elif [ -f "$LIB_SRC/$CONFIG/libbgfx.a" ]; then
    cp "$LIB_SRC/$CONFIG/libbgfx.a" "$LIB_SRC/$CONFIG/libbx.a" "$LIB_SRC/$CONFIG/libbimg.a" "$LIB_DST/" 2>/dev/null || true
fi

echo ""
echo "===== Dependencies ready ====="
echo "  Headers : $BGFX_DIR"
echo "  Libs    : $LIB_DST"
echo ""
echo "Now run: ./Build.sh $CONFIG"
