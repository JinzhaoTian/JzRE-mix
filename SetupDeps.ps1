#!/usr/bin/env pwsh
<#
.DESCRIPTION
  SetupDeps.ps1 — Clone and build bgfx for JzRE-mix (Windows)
  Usage: .\SetupDeps.ps1 [Debug|Release]
#>
param([string]$Config = "Debug")

$ErrorActionPreference = "Stop"

$ThirdParty = Join-Path $PSScriptRoot "Source\ThirdParty"
$BgfxDir    = Join-Path $ThirdParty "bgfx.cmake"

Write-Host "===== JzRE-mix: Dependency Setup ====="
Write-Host ""

# ── Clone bgfx.cmake ─────────────────────────────────────────────────────
if (-not (Test-Path $BgfxDir)) {
    Write-Host "[1/3] Cloning bgfx.cmake (this may take a minute)..."
    git clone --depth 1 https://github.com/bkaradzic/bgfx.cmake.git $BgfxDir
    Push-Location $BgfxDir
    git submodule update --init --recursive --depth 1
    Pop-Location
} else {
    Write-Host "[1/3] bgfx.cmake already exists - skipping clone."
}

# ── Build bgfx ────────────────────────────────────────────────────────────
Write-Host "[2/3] Building bgfx (${Config})..."

$BuildDir = Join-Path $BgfxDir "build"
New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
Push-Location $BuildDir

cmake .. -DCMAKE_BUILD_TYPE=$Config -DBGFX_BUILD_TOOLS=OFF -DBGFX_BUILD_EXAMPLES=OFF -DBGFX_BUILD_TESTS=OFF
cmake --build . --config $Config --parallel

Pop-Location

# ── Copy artifacts ────────────────────────────────────────────────────────
Write-Host "[3/3] Staging bgfx artifacts..."

$LibSrc  = Join-Path $BuildDir $Config
$LibDst  = Join-Path $ThirdParty "lib\Windows"
New-Item -ItemType Directory -Force -Path $LibDst | Out-Null

if (Test-Path (Join-Path $LibSrc "bgfx.lib")) {
    Copy-Item (Join-Path $LibSrc "bgfx.lib") $LibDst
    Copy-Item (Join-Path $LibSrc "bx.lib")   $LibDst
    Copy-Item (Join-Path $LibSrc "bimg.lib") $LibDst
}

# Also copy shaderc for shader compilation
$ShaderC = Join-Path $BgfxDir "bgfx\tools\shaderc"
Write-Host "  Note: shaderc is in $ShaderC (build separately if needed)"

Write-Host ""
Write-Host "===== Dependencies ready ====="
Write-Host "  Headers : $BgfxDir"
Write-Host "  Libs    : $LibDst"
Write-Host ""
Write-Host "Now run: .\Build.ps1 $Config"
