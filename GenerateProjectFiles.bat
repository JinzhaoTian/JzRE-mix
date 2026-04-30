@echo off
setlocal

echo ===== JzRE-mix Generate Project Files =====
echo.

REM Change to the script's directory so paths work from any location.
cd /d "%~dp0"

REM Step 1: Build the JzRE.Build tool
echo [1/3] Building JzRE.Build tool...
dotnet build "Source\Tools\JzRE.Build\JzRE.Build.csproj" -c Release -o "Binaries\Tools\JzRE.Build" --nologo -v quiet
if %errorlevel% neq 0 ( echo FAILED: JzRE.Build tool & exit /b 1 )

REM Step 2: Build C# bindings (must run before project generation so
REM that *.Bindings.Gen.cpp files are picked up by the vcxproj).
echo [2/3] Building C# bindings...
dotnet run --project Source\Tools\JzRE.Build -- -BuildBindingsOnly --workspace "%CD%"
if %errorlevel% neq 0 ( echo FAILED: Bindings generation & exit /b 1 )

REM Step 3: Generate project files (platform-appropriate format)
echo [3/3] Generating project files...
dotnet run --project Source\Tools\JzRE.Build -- -genproject --workspace "%CD%" %*
if %errorlevel% neq 0 ( echo FAILED: Project generation & exit /b 1 )

echo.
echo ===== Done =====
echo.
echo Usage:
echo   GenerateProjectFiles          - platform default (Visual Studio 2022)
echo   GenerateProjectFiles -vs2022  - force Visual Studio 2022
echo   GenerateProjectFiles -vscode  - force Visual Studio Code

exit /b 0
