#!/bin/bash
set -e

# ── JzRE-mix Generate Project Files (Linux / macOS) ──────────────────────────
# Usage: ./GenerateProjectFiles.sh [-vscode|-vs2022]
# Mirrors FlaxEngine's GenerateProjectFiles.sh pattern.

echo "===== JzRE-mix Generate Project Files ====="
echo ""

# Change to the script's directory so it works when called from any location.
cd "$(dirname "$0")"

# Step 1: Build the JzRE.Build tool
echo "[1/3] Building JzRE.Build tool..."
dotnet build Source/Tools/JzRE.Build/JzRE.Build.csproj -c Release -o Binaries/Tools/JzRE.Build --nologo -v quiet

# Step 2: Generate project files
echo "[2/3] Generating project files..."
dotnet run --project Source/Tools/JzRE.Build -- -genproject --workspace "$(pwd)" "$@"

# Step 3: Build C# bindings so the IDE opens with generated glue code ready.
echo "[3/3] Building C# bindings..."
dotnet run --project Source/Tools/JzRE.Build -- -BuildBindingsOnly --workspace "$(pwd)"

echo ""
echo "===== Done ====="
echo ""
echo "Usage:"
echo "  ./GenerateProjectFiles.sh          - platform default (VSCode on Linux/macOS)"
echo "  ./GenerateProjectFiles.sh -vs2022  - force Visual Studio 2022"
echo "  ./GenerateProjectFiles.sh -vscode  - force Visual Studio Code"
