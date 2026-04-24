@echo off
setlocal EnableDelayedExpansion

echo ===== JzRE-mix Build System =====
echo.

REM Step 1: Build the JzRE.Build tool itself
echo [1/3] Building JzRE.Build tool...
dotnet build "Source\Tools\JzRE.Build\JzRE.Build.csproj" -c Release -o "Binaries\Tools\JzRE.Build" --nologo -v quiet
if %errorlevel% neq 0 ( echo FAILED: JzRE.Build tool & exit /b 1 )

REM Step 2: Use JzRE.Build to compile the C++ Runtime DLL
echo [2/3] Building C++ Runtime (JzRE.Runtime.dll)...
call :BuildNative
if %errorlevel% neq 0 ( echo FAILED: C++ Runtime & exit /b 1 )

REM Step 3: Build the C# Editor
echo [3/3] Building C# Editor...
dotnet build "Source\Editor\Editor.csproj" -c Debug -o "Binaries\Windows\Debug" --nologo -v quiet
if %errorlevel% neq 0 ( echo FAILED: C# Editor & exit /b 1 )

echo.
echo ===== Build Succeeded =====
echo Run: Binaries\Windows\Debug\JzRE.Editor.exe
exit /b 0

:BuildNative
REM Find Visual Studio via vswhere
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" ( echo ERROR: vswhere.exe not found & exit /b 1 )

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -property installationPath`) do set "VS_PATH=%%i"
set "VCVARS=%VS_PATH%\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%VCVARS%" ( echo ERROR: vcvarsall.bat not found & exit /b 1 )

call "%VCVARS%" x64 >nul 2>&1

set "SRC=%~dp0Source\Runtime"
set "OUT=%~dp0Binaries\Windows\Debug"
mkdir "%OUT%" 2>nul

REM Collect all .cpp files into a response file
set "RSP=%TEMP%\jzre_cl_sources.rsp"
>"%RSP%" echo.
for /r "%SRC%" %%f in (*.cpp) do echo "%%f">>"%RSP%"

cl.exe /nologo /std:c++17 /EHsc /Od /Zi /MDd /D_DEBUG /DJZRE_RUNTIME_EXPORTS ^
    /I"%SRC%" /I"%SRC%\Core" /I"%SRC%\Rendering" /I"%SRC%\Scripting" ^
    @"%RSP%" ^
    /Fe:"%OUT%\JzRE.Runtime.dll" /Fo:"%OUT%\\" /Fd:"%OUT%\JzRE.Runtime.pdb" ^
    /LD /link d3d11.lib dxgi.lib d3dcompiler.lib
exit /b %errorlevel%
