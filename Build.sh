#!/bin/bash
set -e

# ── JzRE-mix cross-platform build script ────────────────────────────────────
# Usage: ./Build.sh [Debug|Develop|Release]

CONFIG="${1:-Debug}"

echo "===== JzRE-mix Build System ====="
echo ""

# Step 1: Build the JzRE.Build tool
echo "[1/3] Building JzRE.Build tool..."
dotnet build Source/Tools/JzRE.Build/JzRE.Build.csproj -c Release -o Binaries/Tools/JzRE.Build --nologo -v quiet

# Step 2: Build everything via JzRE.Build
echo "[2/3] Building all targets (${CONFIG})..."
dotnet run --project Source/Tools/JzRE.Build -- --target JzREEditor --config "$CONFIG" --workspace "$(pwd)"

echo ""
echo "===== Build Succeeded ====="
echo "Run: dotnet run --project Source/Editor"
