#!/bin/sh
set -e

# ── JzRE-mix Generate Project Files (macOS double-clickable) ───────────────
# Mirrors FlaxEngine's GenerateProjectFiles.command pattern.
# Can be launched from Finder or Terminal:
#   ./GenerateProjectFiles.command              - platform default (VSCode)
#   ./GenerateProjectFiles.command -vs2022      - force Visual Studio 2022
#   ./GenerateProjectFiles.command -vscode      - force Visual Studio Code

echo "===== JzRE-mix Generate Project Files ====="
echo ""

# Change to the script's directory so relative paths work when
# double-clicked from Finder (which starts in $HOME).
cd "$(dirname "$0")"

# Step 1: Build the JzRE.Build tool
echo "[1/3] Building JzRE.Build tool..."
dotnet build Source/Tools/JzRE.Build/JzRE.Build.csproj -c Release -o Binaries/Tools/JzRE.Build --nologo -v quiet

# Step 2: Generate project files (platform-appropriate format)
echo "[2/3] Generating project files..."
dotnet run --project Source/Tools/JzRE.Build -- -genproject --workspace "$(pwd)" "$@"

# Step 3: Build C# bindings so the IDE opens with generated glue code ready.
echo "[3/3] Building C# bindings..."
dotnet run --project Source/Tools/JzRE.Build -- -BuildBindingsOnly --workspace "$(pwd)"

echo ""
echo "===== Done ====="
echo ""
echo "Open the workspace in Visual Studio Code:"
echo "  code ."
echo ""
echo "Usage:"
echo "  ./GenerateProjectFiles.command              - platform default"
echo "  ./GenerateProjectFiles.command -vs2022      - force Visual Studio 2022"
echo "  ./GenerateProjectFiles.command -vscode      - force Visual Studio Code"
