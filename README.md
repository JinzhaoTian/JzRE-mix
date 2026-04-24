# JzRE-mix

A minimal rendering engine demonstrating how to wire together a custom build system, a C++/C# interop layer, and a native Direct3D 11 renderer behind a WinForms editor window.

## Architecture

```
JzRE-mix/
├── Source/
│   ├── Tools/JzRE.Build/      # Build tool (C#, .NET 8) — mirrors Flax.Build
│   ├── Runtime/               # Native DLL (C++17, D3D11)
│   └── Editor/                # Editor UI (C#, WinForms, .NET 8)
└── Binaries/Windows/Debug/    # Build output (generated)
```

### Data flow

```
MainForm (C#)
  └─ NativeRuntime.cs  ──[LibraryImport P/Invoke]──► JzRE.Runtime.dll (C++)
                                                         ├── Renderer.cpp   (D3D11 device, swap chain, shaders)
                                                         └── MeshLoader.cpp (Wavefront OBJ parser)
```

## Requirements

- Windows 10/11 x64
- [Visual Studio 2019/2022](https://visualstudio.microsoft.com/) with **Desktop development with C++** workload
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Build

```bat
Build.bat
```

This script:
1. Compiles `JzRE.Build` (the build tool) with `dotnet build`
2. Invokes MSVC (`cl.exe`) to produce `JzRE.Runtime.dll`
3. Runs `dotnet build` to produce `JzRE.Editor.exe`

Output lands in `Binaries\Windows\Debug\`.

## Visual Studio integration

```bat
GenerateProjectFiles.bat
```

Generates `JzRE.sln` and `Source\Runtime\JzRE.Runtime.vcxproj` with the correct platform toolset for the installed VS version, then opens both projects in the solution with the right build-order dependency (Runtime before Editor).

## Running

```bat
Binaries\Windows\Debug\JzRE.Editor.exe
```

| Action | Result |
|---|---|
| **File › Open OBJ Model…** | Opens a file dialog, loads the mesh, uploads to GPU |
| Left-drag in viewport | Orbit camera |
| Scroll wheel | Zoom in / out |
