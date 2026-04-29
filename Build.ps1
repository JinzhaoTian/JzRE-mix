#!/usr/bin/env pwsh
<#
.DESCRIPTION
  JzRE-mix cross-platform build script (PowerShell)
  Usage: .\Build.ps1 [Debug|Develop|Release]
#>
param([string]$Config = "Debug")

$ErrorActionPreference = "Stop"

Write-Host "===== JzRE-mix Build System ====="
Write-Host ""

# Step 1: Build the JzRE.Build tool
Write-Host "[1/3] Building JzRE.Build tool..."
dotnet build Source/Tools/JzRE.Build/JzRE.Build.csproj -c Release -o Binaries/Tools/JzRE.Build --nologo -v quiet

# Step 2: Build everything via JzRE.Build
Write-Host "[2/3] Building all targets (${Config})..."
dotnet run --project Source/Tools/JzRE.Build -- --target JzREEditor --config $Config --workspace $PWD

Write-Host ""
Write-Host "===== Build Succeeded ====="
Write-Host "Run: dotnet run --project Source/Editor"
