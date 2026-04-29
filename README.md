# JzRE-mix

A cross-platform rendering engine inspired by [FlaxEngine](https://github.com/FlaxEngine/FlaxEngine).

- **C++ Runtime**: bgfx renderer (D3D11 / Vulkan / Metal) + Wavefront OBJ loader
- **C# Editor**: [Avalonia](https://avaloniaui.net/) desktop UI with orbit camera
- **Build System**: FlaxEngine-style `JzRE.Build` with MSVC / GCC / Clang support

## Architecture

```
Source/
├── Tools/JzRE.Build/       # Build tool (C#, .NET 8) — mirrors Flax.Build
│   ├── Toolchain/          # MSVCToolchain, GCCToolchain, ClangToolchain
│   └── Build/              # Builder, Module, ProjectGenerator, ShaderCompiler
├── Runtime/                # Native library (C++17, bgfx)
│   ├── Core/               # Platform.h, Core.h — portable types & macros
│   ├── Rendering/          # Renderer.cpp (bgfx), MeshLoader.cpp (OBJ)
│   └── Scripting/          # API.h — P/Invoke export boundary
├── Editor/                 # Editor UI (C#, Avalonia, .NET 8)
│   └── Interop/            # NativeRuntime.cs — P/Invoke bindings
├── Shaders/                # bgfx shader sources (.sc)
└── ThirdParty/             # bgfx + bx + bimg (setup via SetupDeps)
```

### Data flow

```
MainWindow (C# / Avalonia)
  └─ RenderControl.cs  ──[NativeControlHost]──►  native window handle

NativeRuntime.cs  ──[LibraryImport P/Invoke]──►  libJzRE.Runtime (.dll / .so / .dylib)
                                                      ├── Renderer.cpp   (bgfx device, swap chain, shaders)
                                                      └── MeshLoader.cpp (Wavefront OBJ parser)
```

### Platform defaults

| Platform | Default IDE | Project files | Render backend |
|---|---|---|---|
| **Windows** | Visual Studio 2022 | `JzRE.sln` + `.vcxproj` | D3D11 |
| **Linux** | Visual Studio Code | `.vscode/` JSON configs | Vulkan |
| **macOS** | Visual Studio Code | `.vscode/` JSON configs | Metal |

## Requirements

### All platforms

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [CMake](https://cmake.org/) 3.20+ (for building bgfx)

### Windows

- [Visual Studio 2022](https://visualstudio.microsoft.com/) with **Desktop development with C++**
- Or Visual Studio Code + C# Dev Kit + C/C++ extensions

### Linux

- GCC 11+ (`sudo apt install build-essential`) or Clang
- X11 development headers (`sudo apt install libx11-dev libgl-dev`)

### macOS

- [Xcode Command Line Tools](https://developer.apple.com/xcode/resources/) (`xcode-select --install`)

## Getting started

### 1. Clone and setup dependencies

```bash
git clone https://github.com/your-org/JzRE-mix.git
cd JzRE-mix
```

Build bgfx (one-time setup):

```bash
# Linux / macOS
./SetupDeps.sh Debug

# Windows (PowerShell)
.\SetupDeps.ps1 Debug
```

This clones [bgfx.cmake](https://github.com/bkaradzic/bgfx.cmake) into `Source/ThirdParty/` and compiles bgfx + bx + bimg for your platform.

### 2. Generate project files

```bash
# Linux / macOS
./GenerateProjectFiles.sh

# macOS (double-clickable in Finder)
open GenerateProjectFiles.command

# Windows
GenerateProjectFiles.bat
```

Optional format overrides (mirrors FlaxEngine CLI):

| Flag | Result |
|---|---|
| *(default)* | Platform-appropriate IDE |
| `-vs2022` | Visual Studio 2022 (.sln + .vcxproj) |
| `-vscode` | Visual Studio Code (.vscode/) |

### 3. Build

```bash
# Linux / macOS
./Build.sh Debug

# Windows (PowerShell)
.\Build.ps1 Debug
```

Output lands in `Binaries/<Platform>/Debug/`.

## Running

```bash
# All platforms
dotnet run --project Source/Editor
```

Or launch directly:

| Platform | Command |
|---|---|
| Windows | `Binaries\Windows\Debug\JzRE.Editor.exe` |
| Linux | `dotnet Binaries/Linux/Debug/JzRE.Editor.dll` |
| macOS | `dotnet Binaries/MacOS/Debug/JzRE.Editor.dll` |

## Controls

| Action | Result |
|---|---|
| **File › Open OBJ Model…** | Opens a file dialog, loads the mesh, uploads to GPU |
| Left-drag in viewport | Orbit camera |
| Scroll wheel | Zoom in / out |

## Debugging

### Windows — Visual Studio

Open `JzRE.sln` in Visual Studio 2022. Mixed-mode debugging (C# + C++) works natively — set breakpoints on both sides and press F5.

### All platforms — Visual Studio Code

Open the workspace after running `GenerateProjectFiles.sh -vscode`. The `.vscode/launch.json` includes:

- **Launch Editor (Debug)** — builds and runs with managed debugger attached
- **Attach to Editor** — attach to a running editor process

### Linux / macOS — JetBrains Rider

Rider supports mixed managed+native debugging natively. Open the project folder and use the built-in run configurations.

## Project structure

```
JzRE-mix/
├── Source/
│   ├── Tools/JzRE.Build/       # Build tool (C#)
│   ├── Runtime/                # Native library (C++17)
│   │   ├── Core/               # Platform detection, type aliases, macros
│   │   ├── Rendering/          # Renderer + MeshLoader
│   │   └── Scripting/          # P/Invoke API export macros
│   ├── Editor/                 # Avalonia editor application (C#)
│   │   └── Interop/            # Native runtime P/Invoke bindings
│   ├── Shaders/                # bgfx shader source files (.sc)
│   └── ThirdParty/             # bgfx + bx + bimg (populated by SetupDeps)
├── Binaries/                   # Build output (generated)
├── Cache/                      # Intermediate build artifacts (generated)
├── Build.sh / Build.ps1        # Build scripts
├── SetupDeps.sh / SetupDeps.ps1  # Dependency setup
└── GenerateProjectFiles.*      # Project file generation entry points
```
