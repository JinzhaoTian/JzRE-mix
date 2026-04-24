@echo off
echo Building JzRE.Build tool...
dotnet build "Source\Tools\JzRE.Build\JzRE.Build.csproj" -c Release -o "Binaries\Tools\JzRE.Build" --nologo -v quiet
if %errorlevel% neq 0 ( echo FAILED & exit /b 1 )

echo Generating project files...
"Binaries\Tools\JzRE.Build\JzRE.Build.exe" --generate-project-files

echo Done. Open JzRE.sln in Visual Studio.
