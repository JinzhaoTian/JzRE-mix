using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Text;

namespace JzRE.Build;

/// <summary>
/// Core build orchestrator. Discovers *.Build.cs module definitions via Roslyn,
/// builds the dependency graph, then compiles native C++ and managed C# targets.
/// This mirrors Flax.Build's Builder class.
/// </summary>
public class Builder
{
    private readonly BuildOptions _opts;
    private readonly string       _root;

    public Builder(BuildOptions opts)
    {
        _opts = opts;
        _root = Path.GetFullPath(opts.WorkspaceDir);
    }

    public void Build()
    {
        var modules = DiscoverModules();
        Console.WriteLine($"Discovered {modules.Count} module(s): {string.Join(", ", modules.Select(m => m.Name))}");

        // Native modules build before managed ones so the C# editor can find
        // its P/Invoke target. Editor.Build.cs declares this dependency
        // (JzRE.Runtime), but a name-based pre-pass keeps the order stable
        // even when no PublicDependencies are declared.
        modules = OrderModulesForBuild(modules);

        // Apply --target filter. Honors module names and a few aliases (the
        // PowerShell scripts pass "JzREEditor" as the umbrella target).
        var selected = SelectModules(modules, _opts.Target);
        Console.WriteLine($"Building {selected.Count} module(s) for target '{_opts.Target}': {string.Join(", ", selected.Select(m => m.Name))}");

        // Generate bindings for modules with HasBindings before compiling.
        // This writes .Gen.cpp and .Gen.cs files to the source tree so the
        // existing toolchain and dotnet build pick them up automatically.
        foreach (var m in selected.Where(m => m.HasBindings))
        {
            GenerateBindings(m);
        }

        foreach (var m in selected)
        {
            m.Setup(_opts);

            if (m.BuildNativeCode)
            {
                Console.WriteLine($"[C++] {m.Name}");
                var tc = ToolchainLocator.Resolve(_opts, _root);
                tc.Compile(m);
            }

            if (m.BuildCSharp)
            {
                Console.WriteLine($"[C#]  {m.Name}");
                BuildCSharpModule(m);
            }
        }

        // Compile shaders for the target platform
        Console.WriteLine($"[SHD] Compiling shaders for {_opts.Platform}...");
        ShaderCompiler.Compile(_opts, _root);

        Console.WriteLine("Build complete.");
    }

    /// <summary>
    /// Run the bindings generator for a module that has both native and managed code.
    /// Parses C++ headers for API_* annotations and generates files into the
    /// module's directory (next to its .Build.cs file):
    ///   {ModuleDir}/{Module}.Bindings.Gen.cpp  — internal call stubs
    ///   {ModuleDir}/{Module}.Bindings.Gen.cs   — partial class bindings
    /// </summary>
    private void GenerateBindings(Module module)
    {
        var moduleDir = module.ModuleDirectory!;
        Directory.CreateDirectory(moduleDir);

        // Collect headers that may have API annotations
        var headers = Directory.GetFiles(moduleDir, "*.h", SearchOption.AllDirectories)
            .Where(f => HasApiAnnotations(f))
            .ToList();

        if (headers.Count == 0)
        {
            Console.WriteLine($"  [BND] No API annotations found for {module.Name}, skipping bindings.");
            return;
        }

        Console.WriteLine($"  [BND] Generating bindings for {module.Name} ({headers.Count} header(s))...");

        var parser = new Bindings.HeaderParser(module.Name);
        var moduleInfo = parser.Parse(headers);

        // Generate C++ glue code
        var cppGen = new Bindings.CppGenerator(moduleInfo);
        var cppPath = Path.Combine(moduleDir, $"{module.BinaryModuleName}.Bindings.Gen.cpp");
        File.WriteAllText(cppPath, cppGen.Generate());
        Console.WriteLine($"    -> {Path.GetRelativePath(_root, cppPath)}");

        // Generate C# bindings
        var csGen = new Bindings.CSharpGenerator(moduleInfo);
        var csPath = Path.Combine(moduleDir, $"{module.BinaryModuleName}.Bindings.Gen.cs");
        File.WriteAllText(csPath, csGen.Generate());
        Console.WriteLine($"    -> {Path.GetRelativePath(_root, csPath)}");
    }

    /// <summary>Quick check if a header file contains any API_* annotations.</summary>
    private static bool HasApiAnnotations(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return content.Contains("API_CLASS(")
            || content.Contains("API_STRUCT(")
            || content.Contains("API_ENUM(")
            || content.Contains("API_FUNCTION(")
            || content.Contains("API_INTERFACE(");
    }

    /// <summary>Native (C++) modules build before managed (C#) modules.</summary>
    private static List<Module> OrderModulesForBuild(List<Module> modules) =>
        modules.OrderBy(m => m.BuildCSharp ? 1 : 0)
               .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>
    /// Selects modules matching the target. The target string can be a
    /// module name ("JzRE.Runtime"), the binary name ("JzRE.Runtime"), the
    /// generated class name ("JzRERuntime"), or one of the umbrella aliases
    /// (Editor / Runtime / All) used by the helper scripts.
    /// </summary>
    private static List<Module> SelectModules(List<Module> modules, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Equals("All", StringComparison.OrdinalIgnoreCase))
            return modules;

        // Umbrella aliases must be checked BEFORE exact-match because the
        // generated module class names (JzRERuntime, JzREEditor) also match
        // these aliases via MatchesModule -> GetType().Name.
        if (target.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("JzREEditor", StringComparison.OrdinalIgnoreCase))
            return modules; // editor needs the runtime built too

        if (target.Equals("Runtime", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("JzRERuntime", StringComparison.OrdinalIgnoreCase))
            return modules.Where(m => m.BuildNativeCode).ToList();

        // Exact-match by Name / BinaryModuleName / class name.
        var exact = modules.Where(m => MatchesModule(m, target)).ToList();
        if (exact.Count > 0) return exact;

        Console.WriteLine($"  Warning: no module matches target '{target}', building all.");
        return modules;
    }

    private static bool MatchesModule(Module m, string target) =>
        m.Name.Equals(target,             StringComparison.OrdinalIgnoreCase) ||
        m.BinaryModuleName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
        m.GetType().Name.Equals(target,   StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds C# bindings only — parses API-annotated headers and generates
    /// .Gen.cpp / .Gen.cs files for all modules with HasBindings. Called after
    /// GenerateProjectFiles so the IDE opens with bindings already in place.
    /// Mirrors FlaxEngine's -BuildBindingsOnly mode.
    /// </summary>
    public void BuildBindingsOnly()
    {
        var modules = DiscoverModules();
        Console.WriteLine($"Discovered {modules.Count} module(s): {string.Join(", ", modules.Select(m => m.Name))}");

        var bindingsModules = modules.Where(m => m.HasBindings).ToList();
        if (bindingsModules.Count == 0)
        {
            Console.WriteLine("No modules with bindings found.");
            return;
        }

        Console.WriteLine($"Building bindings for {bindingsModules.Count} module(s)...");
        foreach (var m in bindingsModules)
        {
            GenerateBindings(m);
        }
        Console.WriteLine("Bindings build complete.");
    }

    public void GenerateProjectFiles()
    {
        var modules = DiscoverModules();

        // Determine project format: explicit CLI flag or platform default.
        // Mirrors FlaxEngine: -vscode / -vs2022 flags override Platform.DefaultProjectFormat.
        var format = _opts.ProjectFormat ?? ProjectGeneratorFactory.GetPlatformDefault();
        Console.WriteLine($"  Project format: {format}");

        switch (format)
        {
            case ProjectFormat.VisualStudio:
            case ProjectFormat.VisualStudio2022:
                GenerateVSProjects(modules);
                break;

            case ProjectFormat.VisualStudioCode:
                GenerateVSCodeProjects(modules);
                break;
        }

        // Always generate a Makefile on non-Windows platforms for CLI builds
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            new MakefileGenerator(_opts, _root).Generate(modules);
        }
    }

    // ── Visual Studio (.sln + .vcxproj) ──────────────────────────────────

    private void GenerateVSProjects(List<Module> modules)
    {
        var slnProjects = new List<(string name, string relPath, string typeGuid, string projGuid, string projPlatform)>();

        foreach (var m in modules)
        {
            m.Setup(_opts);
        }

        foreach (var m in modules)
        {
            if (m.BuildNativeCode && m.CustomExternalProjectFilePath == null)
            {
                var vcxPath = GenerateVcxproj(m);
                var rel     = Path.GetRelativePath(_root, vcxPath).Replace('/', '\\');
                slnProjects.Add((m.Name, rel, CppProjectTypeGuid, ModuleGuid(m.Name), "x64"));
                Console.WriteLine($"  Generated {rel}");

                // Generate .vcxproj.user with debugger launch settings so that
                // F5 on the C++ DLL project launches the editor with --debug.
                GenerateVcxprojUser(vcxPath);
                Console.WriteLine($"  Generated {Path.GetRelativePath(_root, vcxPath + ".user")}");
            }

            if (m.BuildCSharp)
            {
                if (m.CustomExternalProjectFilePath != null)
                {
                    // Reference the pre-existing project file without generating one.
                    // Mirrors FlaxEngine's CustomExternalProjectFilePath pattern for tool projects.
                    var rel = Path.GetRelativePath(_root, m.CustomExternalProjectFilePath).Replace('/', '\\');
                    var typeGuid = m.CustomExternalProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        ? CsProjectTypeGuid : CppProjectTypeGuid;
                    var platform = "x64";
                    slnProjects.Add((m.Name, rel, typeGuid, ModuleGuid(m.Name), platform));
                    Console.WriteLine($"  Referenced {rel}");
                }
                else
                {
                    // Generate .csproj next to the .Build.cs file.
                    var csproj = GenerateCsproj(m, modules);
                    var rel    = Path.GetRelativePath(_root, csproj).Replace('/', '\\');
                    slnProjects.Add((m.Name, rel, CsProjectTypeGuid, ModuleGuid(m.Name), "x64"));
                    Console.WriteLine($"  Generated {rel}");

                    // Generate Properties/launchSettings.json for Visual Studio
                    // mixed-mode (.NET Core) debugging — C# and C++ in one session.
                    GenerateLaunchSettings(Path.GetDirectoryName(csproj)!);
                    Console.WriteLine($"  Generated Properties/launchSettings.json");
                }
            }
        }

        // Ensure C++ projects appear before C# projects in the .sln.
        // VS builds projects in declaration order, so the native DLL is
        // ready before the managed editor tries to resolve its P/Invoke.
        // Also avoids "ignored" status caused by ProjectDependencies
        // cross-referencing mixed platform types (AnyCPU vs x64).
        slnProjects.Sort((a, b) =>
        {
            bool aIsCpp = a.typeGuid == CppProjectTypeGuid;
            bool bIsCpp = b.typeGuid == CppProjectTypeGuid;
            if (aIsCpp && !bIsCpp) return -1;
            if (!aIsCpp && bIsCpp) return 1;
            return 0;
        });

        var slnPath = Path.Combine(_root, "JzRE.sln");
        GenerateSln(slnPath, slnProjects);
        Console.WriteLine($"  Generated JzRE.sln");
        Console.WriteLine($"\nDone. Open {slnPath} in Visual Studio.");
    }

    // ── Visual Studio Code (.vscode/ tasks, launch, IntelliSense) ──────────

    private void GenerateVSCodeProjects(List<Module> modules)
    {
        foreach (var m in modules)
            m.Setup(_opts);

        var vsCodeDir = Path.Combine(_root, ".vscode");
        Directory.CreateDirectory(vsCodeDir);

        GenerateTasksJson(vsCodeDir, modules);
        GenerateLaunchJson(vsCodeDir);
        GenerateCppPropertiesJson(vsCodeDir);
        GenerateVSCodeExtensionsJson(vsCodeDir);

        Console.WriteLine($"  Generated .vscode/ (tasks.json, launch.json, c_cpp_properties.json)");
        Console.WriteLine($"\nDone. Open the workspace in Visual Studio Code.");
    }

    private void GenerateTasksJson(string vsCodeDir, List<Module> modules)
    {
        var buildToolDir = Path.GetFullPath(Path.Combine(_root, "Source", "Tools", "JzRE.Build"));
        var rootNorm = _root.Replace('\\', '/');
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  \"version\": \"2.0.0\",");
        sb.AppendLine("  \"tasks\": [");

        // Build tasks — Debug / Develop / Release
        var configs = new[] { ("Debug", true), ("Develop", false), ("Release", false) };
        for (int i = 0; i < configs.Length; i++)
        {
            var (cfg, isDefault) = configs[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"label\": \"JzRE-mix: Build ({cfg})\",");
            sb.AppendLine("      \"type\": \"shell\",");
            sb.AppendLine("      \"command\": \"dotnet\",");
            sb.AppendLine("      \"args\": [");
            sb.AppendLine("        \"run\",");
            sb.AppendLine("        \"--project\",");
            sb.AppendLine($"        \"{buildToolDir.Replace('\\', '/')}\",");
            sb.AppendLine("        \"--\",");
            sb.AppendLine("        \"--target\", \"JzREEditor\",");
            sb.AppendLine($"        \"--config\", \"{cfg}\",");
            sb.AppendLine($"        \"--workspace\", \"{rootNorm}\"");
            sb.AppendLine("      ],");
            if (isDefault)
                sb.AppendLine("      \"group\": { \"kind\": \"build\", \"isDefault\": true },");
            else
                sb.AppendLine("      \"group\": { \"kind\": \"build\" },");
            sb.AppendLine("      \"problemMatcher\": []");
            sb.AppendLine(i < configs.Length - 1 ? "    }," : "    }");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(vsCodeDir, "tasks.json"), sb.ToString(), Encoding.UTF8);
    }

    private void GenerateLaunchJson(string vsCodeDir)
    {
        var editorProj = Path.GetFullPath(Path.Combine(_root, "Source", "Editor"));
        var isWindows  = System.Runtime.InteropServices.RuntimeInformation
                         .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var isMac      = System.Runtime.InteropServices.RuntimeInformation
                         .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
        var workspaceRoot = _root.Replace('\\', '/');

        // Debug type selection mirrors FlaxEngine's dual-debugger pattern:
        // cppvsdbg + coreclr on Windows; cppdbg + coreclr on Linux/macOS.
        var cppType   = isWindows ? "cppvsdbg" : "cppdbg";
        var cppMIMode = isMac ? "lldb" : "gdb";
        var editorProjNorm = editorProj.Replace('\\', '/');

        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  \"version\": \"0.2.0\",");
        sb.AppendLine("  \"compounds\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Launch Editor (C# + C++)\",");
        sb.AppendLine("      \"configurations\": [\"Launch Editor (C#)\", \"Launch Editor (C++)\"],");
        sb.AppendLine("      \"stopAll\": true,");
        sb.AppendLine("      \"preLaunchTask\": \"JzRE-mix: Build (Debug)\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"configurations\": [");

        // 1. C++ launch — native debugger drives the process
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Launch Editor (C++)\",");
        sb.AppendLine($"      \"type\": \"{cppType}\",");
        sb.AppendLine("      \"request\": \"launch\",");
        sb.AppendLine("      \"program\": \"dotnet\",");
        sb.AppendLine("      \"args\": [");
        sb.AppendLine($"        \"run\",");
        sb.AppendLine($"        \"--project\",");
        sb.AppendLine($"        \"{editorProjNorm}\",");
        sb.AppendLine("        \"--\",");
        sb.AppendLine("        \"--debug\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"cwd\": \"${workspaceFolder}\",");
        sb.AppendLine("      \"env\": {");
        sb.AppendLine("        \"LD_LIBRARY_PATH\": \"${workspaceFolder}/Binaries/Linux/Debug:${env:LD_LIBRARY_PATH}\",");
        sb.AppendLine("        \"DYLD_LIBRARY_PATH\": \"${workspaceFolder}/Binaries/MacOS/Debug:${env:DYLD_LIBRARY_PATH}\"");
        sb.AppendLine("      },");
        if (!isWindows)
        {
            sb.AppendLine($"      \"MIMode\": \"{cppMIMode}\",");
            sb.AppendLine("      \"setupCommands\": [");
            sb.AppendLine("        { \"text\": \"handle SIG34 nostop noprint\" },");
            sb.AppendLine("        { \"text\": \"handle SIG35 nostop noprint\" },");
            sb.AppendLine("        { \"text\": \"handle SIG36 nostop noprint\" },");
            sb.AppendLine("        { \"text\": \"handle SIG37 nostop noprint\" }");
            sb.AppendLine("      ],");
        }
        sb.AppendLine("      \"console\": \"internalConsole\",");
        sb.AppendLine("      \"stopAtEntry\": false");
        sb.AppendLine("    },");

        // 2. C# launch — managed debugger with nativeDebugging for mixed-mode.
        //    When nativeDebugging is true, the C# extension auto-attaches the
        //    C++ debugger (cppvsdbg on Windows) to the same process.
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Launch Editor (C#)\",");
        sb.AppendLine("      \"type\": \"coreclr\",");
        sb.AppendLine("      \"request\": \"launch\",");
        sb.AppendLine("      \"program\": \"dotnet\",");
        sb.AppendLine("      \"args\": [");
        sb.AppendLine($"        \"run\",");
        sb.AppendLine($"        \"--project\",");
        sb.AppendLine($"        \"{editorProjNorm}\",");
        sb.AppendLine("        \"--\",");
        sb.AppendLine("        \"--debug\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"cwd\": \"${workspaceFolder}\",");
        if (isWindows)
        {
            sb.AppendLine("      \"nativeDebugging\": true,");
        }
        sb.AppendLine("      \"env\": {");
        sb.AppendLine("        \"LD_LIBRARY_PATH\": \"${workspaceFolder}/Binaries/Linux/Debug:${env:LD_LIBRARY_PATH}\",");
        sb.AppendLine("        \"DYLD_LIBRARY_PATH\": \"${workspaceFolder}/Binaries/MacOS/Debug:${env:DYLD_LIBRARY_PATH}\"");
        sb.AppendLine("      },");
        sb.AppendLine("      \"console\": \"internalConsole\",");
        sb.AppendLine("      \"stopAtEntry\": false");
        sb.AppendLine("    },");

        // 3. C++ attach — attach native debugger to a running editor
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Attach C++ to Editor\",");
        sb.AppendLine($"      \"type\": \"{cppType}\",");
        sb.AppendLine("      \"request\": \"attach\",");
        sb.AppendLine("      \"processId\": \"${command:pickProcess}\",");
        if (!isWindows)
            sb.AppendLine($"      \"MIMode\": \"{cppMIMode}\",");
        sb.AppendLine("      \"console\": \"internalConsole\"");
        sb.AppendLine("    },");

        // 4. C# attach — attach managed debugger to a running editor
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Attach C# to Editor\",");
        sb.AppendLine("      \"type\": \"coreclr\",");
        sb.AppendLine("      \"request\": \"attach\",");
        sb.AppendLine("      \"processName\": \"JzRE.Editor\",");
        sb.AppendLine("      \"console\": \"internalConsole\"");
        sb.AppendLine("    }");

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(vsCodeDir, "launch.json"), sb.ToString(), Encoding.UTF8);
    }

    private void GenerateCppPropertiesJson(string vsCodeDir)
    {
        var srcDir = Path.GetFullPath(Path.Combine(_root, "Source", "Runtime"));
        var tpDir  = Path.GetFullPath(Path.Combine(_root, "Source", "ThirdParty", "bgfx.cmake"));
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  \"configurations\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Linux\",");
        sb.AppendLine("      \"includePath\": [");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Core\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Rendering\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Scripting\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bgfx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bimg/include\",");
        sb.AppendLine("        \"${default}\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"defines\": [");
        sb.AppendLine("        \"JzRE_RUNTIME_EXPORTS\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"cStandard\": \"c17\",");
        sb.AppendLine("      \"cppStandard\": \"c++17\",");
        sb.AppendLine("      \"intelliSenseMode\": \"linux-gcc-x64\",");
        sb.AppendLine("      \"compilerPath\": \"/usr/bin/g++\"");
        sb.AppendLine("    },");
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Mac\",");
        sb.AppendLine("      \"includePath\": [");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Core\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Rendering\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Scripting\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bgfx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bimg/include\",");
        sb.AppendLine("        \"${default}\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"defines\": [");
        sb.AppendLine("        \"JzRE_RUNTIME_EXPORTS\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"cStandard\": \"c17\",");
        sb.AppendLine("      \"cppStandard\": \"c++17\",");
        sb.AppendLine("      \"intelliSenseMode\": \"macos-clang-x64\",");
        sb.AppendLine("      \"compilerPath\": \"/usr/bin/clang++\"");
        sb.AppendLine("    },");
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"Win32\",");
        sb.AppendLine("      \"includePath\": [");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Core\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Rendering\",");
        sb.AppendLine($"        \"{srcDir.Replace('\\', '/')}/Scripting\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bgfx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bx/include\",");
        sb.AppendLine($"        \"{tpDir.Replace('\\', '/')}/bimg/include\",");
        sb.AppendLine("        \"${default}\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"defines\": [");
        sb.AppendLine("        \"JzRE_RUNTIME_EXPORTS\"");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"cStandard\": \"c17\",");
        sb.AppendLine("      \"cppStandard\": \"c++17\",");
        sb.AppendLine("      \"intelliSenseMode\": \"windows-msvc-x64\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"version\": 4");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(vsCodeDir, "c_cpp_properties.json"), sb.ToString(), Encoding.UTF8);
    }

    private void GenerateVSCodeExtensionsJson(string vsCodeDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"recommendations\": [");
        sb.AppendLine("    \"ms-dotnettools.csharp\",");
        sb.AppendLine("    \"ms-vscode.cpptools\"");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(vsCodeDir, "extensions.json"), sb.ToString(), Encoding.UTF8);
    }

    // ── Project type GUIDs (standard VS identifiers) ────────────────────────
    private const string CppProjectTypeGuid = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";
    private const string CsProjectTypeGuid  = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

    // Deterministic GUID from module name so repeated runs produce identical .sln files
    private static string ModuleGuid(string name)
    {
        var hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash).ToString("B").ToUpper();
    }

    // ── .sln writer ─────────────────────────────────────────────────────────

    private void GenerateSln(
        string slnPath,
        List<(string name, string relPath, string typeGuid, string projGuid, string projPlatform)> projects)
    {
        // When targeting VS 2022, find that installation's version for the .sln header.
        int? vsMajorCap  = _opts.ProjectFormat == ProjectFormat.VisualStudio2022 ? 17 : null;
        var vsVersion    = VSLocator.FindVSVersion(vsMajorCap) ?? "17.0.31903.59";
        var vsMajor      = vsVersion.Contains('.') ? vsVersion[..vsVersion.IndexOf('.')] : "17";
        var slnGuid      = ModuleGuid(_root); // stable solution GUID derived from workspace path

        // Solution configurations: Editor/Game × Debug/Develop/Release on x64.
        // C# projects (Any CPU) coexist with native x64 projects in a single platform.
        // Mirrors FlaxEngine's solution configuration matrix.
        var solutionTargets  = new[] { "Editor", "Game" };
        var solutionConfigs  = new[] { TargetConfiguration.Debug, TargetConfiguration.Develop, TargetConfiguration.Release };
        var solutionPlatforms = new[] { "x64" };

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine($"# Visual Studio Version {vsMajor}");
        sb.AppendLine($"VisualStudioVersion = {vsVersion}");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        // ── Solution folder hierarchy ──────────────────────────────────────
        // Mirrors FlaxEngine: derive folder from project directory relative to
        // workspace root, stripping the "Source/" prefix (e.g.
        // Source/Tools/JzRE.Build → Tools/JzRE.Build).
        var folderTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";
        var projFolders = new List<string?>(projects.Count);
        var folderGuids = new Dictionary<string, string>();

        foreach (var (_, relPath, _, _, _) in projects)
        {
            var projDir = Path.GetDirectoryName(relPath.Replace('\\', '/').TrimEnd('/'));
            string? folder = null;
            if (projDir != null)
            {
                projDir = projDir.Replace('\\', '/');
                if (projDir.StartsWith("Source/", StringComparison.OrdinalIgnoreCase))
                    folder = projDir.Substring("Source/".Length);
                else if (projDir != "." && projDir.Length > 0)
                    folder = projDir;
            }
            // Place Runtime/Editor modules directly under Engine folder,
            // and all tool projects directly under Tools folder.
            if (folder == "Runtime" || folder == "Editor")
                folder = "Engine";
            else if (folder != null && folder.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase))
                folder = "Tools";
            projFolders.Add(folder);

            // Collect all path segments for folder GUIDs
            if (folder != null)
            {
                var parts = folder.Split('/');
                var current = "";
                foreach (var part in parts)
                {
                    current = current.Length > 0 ? $"{current}/{part}" : part;
                    if (!folderGuids.ContainsKey(current))
                        folderGuids[current] = ModuleGuid(current);
                }
            }
        }

        // Write solution folder Project entries
        foreach (var (folderPath, folderGuid) in folderGuids)
        {
            var folderName = folderPath.Contains('/')
                ? folderPath.Substring(folderPath.LastIndexOf('/') + 1)
                : folderPath;
            sb.AppendLine($"Project(\"{folderTypeGuid}\") = \"{folderName}\", \"{folderName}\", \"{folderGuid}\"");
            sb.AppendLine("EndProject");
        }

        // Write project entries
        foreach (var (name, relPath, typeGuid, projGuid, _) in projects)
        {
            sb.AppendLine($"Project(\"{typeGuid}\") = \"{name}\", \"{relPath}\", \"{projGuid}\"");
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        foreach (var target in solutionTargets)
            foreach (var cfg in solutionConfigs)
                foreach (var solutionPlatform in solutionPlatforms)
                    sb.AppendLine($"\t\t{target}.{cfg}|{solutionPlatform} = {target}.{cfg}|{solutionPlatform}");
        sb.AppendLine("\tEndGlobalSection");

        // ProjectConfigurationPlatforms maps solution config+platform to project config+platform.
        // Solution: Editor.Debug|x64 → Project: Debug|x64
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var (_, _, _, projGuid, projPlatform) in projects)
        {
            foreach (var target in solutionTargets)
            {
                foreach (var cfg in solutionConfigs)
                {
                    foreach (var solutionPlatform in solutionPlatforms)
                    {
                        var mappedPlatform = projPlatform;
                        sb.AppendLine($"\t\t{projGuid}.{target}.{cfg}|{solutionPlatform}.ActiveCfg = {cfg}|{mappedPlatform}");
                        sb.AppendLine($"\t\t{projGuid}.{target}.{cfg}|{solutionPlatform}.Build.0 = {cfg}|{mappedPlatform}");
                    }
                }
            }
        }
        sb.AppendLine("\tEndGlobalSection");

        // NestedProjects: map project GUIDs to their solution folder GUIDs.
        // Mirrors FlaxEngine's VisualStudioProjectGenerator nested hierarchy.
        sb.AppendLine("\tGlobalSection(NestedProjects) = preSolution");
        for (int i = 0; i < projects.Count; i++)
        {
            var folder = projFolders[i];
            if (folder != null && folderGuids.TryGetValue(folder, out var folderGuid))
                sb.AppendLine($"\t\t{projects[i].projGuid} = {folderGuid}");
        }
        // Nest sub-folders inside their parent folders
        foreach (var (folderPath, folderGuid) in folderGuids)
        {
            var lastSlash = folderPath.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var parent = folderPath.Substring(0, lastSlash);
                if (folderGuids.TryGetValue(parent, out var parentGuid))
                    sb.AppendLine($"\t\t{folderGuid} = {parentGuid}");
            }
        }
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("\t\tHideSolutionNode = FALSE");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ExtensibilityGlobals) = postSolution");
        sb.AppendLine($"\t\tSolutionGuid = {slnGuid}");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");

        File.WriteAllText(slnPath, sb.ToString(), Encoding.UTF8);
    }

    // ── .vcxproj writer ─────────────────────────────────────────────────────

    private string GenerateVcxproj(Module module)
    {
        var projDir     = module.ModuleDirectory!;
        var projPath    = Path.Combine(projDir, $"{module.BinaryModuleName}.vcxproj");
        var projToRoot  = Path.GetRelativePath(projDir, _root).Replace('/', '\\');
        var projToSrc   = Path.GetRelativePath(projDir, Path.Combine(_root, "Source")).Replace('/', '\\');
        var guid        = ModuleGuid(module.Name);
        var outDir      = $@"$(ProjectDir){projToRoot}\Binaries\Windows\$(Configuration)\";
        var intDir      = $@"$(ProjectDir){projToRoot}\Cache\$(Configuration)\$(ProjectName)\";
        var tpDir       = $@"$(ProjectDir){projToSrc}\ThirdParty\bgfx.cmake";
        var includes    = string.Join(";",
            new[] { "$(ProjectDir)", @"$(ProjectDir)Core", @"$(ProjectDir)Rendering", @"$(ProjectDir)Scripting",
                    $@"{tpDir}\bgfx\include", $@"{tpDir}\bx\include", $@"{tpDir}\bimg\include" });

        // When targeting VS 2022 explicitly, constrain detection to VS 17.x so we
        // get its toolset even if a newer VS (18.x) is also installed.
        int? vsMajorCap = _opts.ProjectFormat == ProjectFormat.VisualStudio2022 ? 17 : null;
        var toolset        = VSLocator.DetectPlatformToolset(vsMajorCap);
        var msvcVersion    = VSLocator.FindMSVCVersion(vsMajorCap) ?? "unknown";
        var vcProjVersion  = VSLocator.DetectVCProjectVersion(vsMajorCap);
        Console.WriteLine($"  Toolset: {toolset} (MSVC {msvcVersion}, VCProjectVersion {vcProjVersion})");

        // Collect source files relative to the project directory (next to .Build.cs)
        var sources = Directory.GetFiles(projDir, "*.cpp", SearchOption.AllDirectories);
        var headers = Directory.GetFiles(projDir, "*.h",   SearchOption.AllDirectories);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");

        // Configurations
        var allConfigs = new[] { "Debug", "Develop", "Release" };
        sb.AppendLine("  <ItemGroup Label=\"ProjectConfigurations\">");
        foreach (var cfg in allConfigs)
        {
            sb.AppendLine($"    <ProjectConfiguration Include=\"{cfg}|x64\">");
            sb.AppendLine($"      <Configuration>{cfg}</Configuration>");
            sb.AppendLine($"      <Platform>x64</Platform>");
            sb.AppendLine("    </ProjectConfiguration>");
        }
        sb.AppendLine("  </ItemGroup>");

        // Globals — VCProjectVersion must match the installed VS major version
        sb.AppendLine("  <PropertyGroup Label=\"Globals\">");
        sb.AppendLine($"    <VCProjectVersion>{vcProjVersion}</VCProjectVersion>");
        sb.AppendLine($"    <ProjectGuid>{guid}</ProjectGuid>");
        sb.AppendLine($"    <RootNamespace>{module.BinaryModuleName.Replace(".", "_")}</RootNamespace>");
        sb.AppendLine("    <WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>");
        sb.AppendLine("  </PropertyGroup>");

        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.Default.props\" />");

        // Pass 1: Configuration PropertyGroups (toolset/type) — must precede Microsoft.Cpp.props
        foreach (var (cfg, useDebug) in new[]
        {
            ("Debug",       "true"),
            ("Develop", "false"),
            ("Release",     "false"),
        })
        {
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\" Label=\"Configuration\">");
            sb.AppendLine("    <ConfigurationType>DynamicLibrary</ConfigurationType>");
            sb.AppendLine($"    <UseDebugLibraries>{useDebug}</UseDebugLibraries>");
            sb.AppendLine($"    <PlatformToolset>{toolset}</PlatformToolset>");
            sb.AppendLine("    <CharacterSet>Unicode</CharacterSet>");
            sb.AppendLine("  </PropertyGroup>");
        }

        // Standard VS import order: Default.props → Configuration → Cpp.props →
        // ExtensionSettings → Shared → PropertySheets → UserMacros → OutDir →
        // ItemDefinitionGroup → ItemGroups → Cpp.targets → ExtensionTargets
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.props\" />");
        sb.AppendLine("  <ImportGroup Label=\"ExtensionSettings\">");
        sb.AppendLine("  </ImportGroup>");
        sb.AppendLine("  <ImportGroup Label=\"Shared\">");
        sb.AppendLine("  </ImportGroup>");
        foreach (var cfg in allConfigs)
        {
            sb.AppendLine($"  <ImportGroup Label=\"PropertySheets\" Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\">");
            sb.AppendLine("    <Import Project=\"$(UserRootDir)\\Microsoft.Cpp.$(Platform).user.props\"");
            sb.AppendLine("            Condition=\"exists('$(UserRootDir)\\Microsoft.Cpp.$(Platform).user.props')\"");
            sb.AppendLine("            Label=\"LocalAppDataPlatform\" />");
            sb.AppendLine("  </ImportGroup>");
        }
        sb.AppendLine("  <PropertyGroup Label=\"UserMacros\" />");

        // Pass 2: OutDir/IntDir and compiler settings
        foreach (var (cfg, opt, rt) in new[]
        {
            ("Debug",       "Disabled",  "MultiThreadedDebugDLL"),
            ("Develop", "MaxSpeed",  "MultiThreadedDLL"),
            ("Release",     "MaxSpeed",  "MultiThreadedDLL"),
        })
        {
            var bxConfig = cfg == "Debug" ? "BX_CONFIG_DEBUG=1" : "BX_CONFIG_DEBUG=0";
            var defines = cfg switch
            {
                "Debug"       => $"JzRE_RUNTIME_EXPORTS;NOMINMAX;{bxConfig};_CRT_SECURE_NO_WARNINGS;_DEBUG;BUILD_DEBUG;_WINDOWS;_USRDLL;%(PreprocessorDefinitions)",
                "Develop" => $"JzRE_RUNTIME_EXPORTS;NOMINMAX;{bxConfig};_CRT_SECURE_NO_WARNINGS;_DEBUG;BUILD_DEVELOP;_WINDOWS;_USRDLL;%(PreprocessorDefinitions)",
                "Release"     => $"JzRE_RUNTIME_EXPORTS;NOMINMAX;{bxConfig};_CRT_SECURE_NO_WARNINGS;NDEBUG;BUILD_RELEASE;_WINDOWS;_USRDLL;%(PreprocessorDefinitions)",
                _             => $"JzRE_RUNTIME_EXPORTS;NOMINMAX;{bxConfig};_CRT_SECURE_NO_WARNINGS;%(PreprocessorDefinitions)",
            };

            var tpLibDir = $@"{tpDir}\build\cmake";
            var debugInfo = cfg != "Release" ? "true" : "false";

            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\">");
            sb.AppendLine($"    <OutDir>{outDir}</OutDir>");
            sb.AppendLine($"    <IntDir>{intDir}</IntDir>");
            sb.AppendLine("  </PropertyGroup>");

            sb.AppendLine($"  <ItemDefinitionGroup Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\">");
            sb.AppendLine("    <ClCompile>");
            sb.AppendLine("      <WarningLevel>Level3</WarningLevel>");
            sb.AppendLine($"      <Optimization>{opt}</Optimization>");
            sb.AppendLine($"      <PreprocessorDefinitions>{defines}</PreprocessorDefinitions>");
            sb.AppendLine($"      <RuntimeLibrary>{rt}</RuntimeLibrary>");
            sb.AppendLine("      <LanguageStandard>stdcpp20</LanguageStandard>");
            sb.AppendLine("      <ConformanceMode>true</ConformanceMode>");
            sb.AppendLine("      <ExceptionHandling>Sync</ExceptionHandling>");
            sb.AppendLine("      <DebugInformationFormat>ProgramDatabase</DebugInformationFormat>");
            sb.AppendLine("      <AdditionalOptions>/utf-8 /Zc:__cplusplus /Zc:preprocessor %(AdditionalOptions)</AdditionalOptions>");
            sb.AppendLine($"      <AdditionalIncludeDirectories>{includes};%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>");
            sb.AppendLine("    </ClCompile>");
            sb.AppendLine("    <Link>");
            sb.AppendLine("      <GenerateDebugInformation>true</GenerateDebugInformation>");
            if (debugInfo == "true")
                sb.AppendLine("      <GenerateFullDebugInformation>true</GenerateFullDebugInformation>");
            sb.AppendLine($"      <AdditionalLibraryDirectories>{tpLibDir}\\bgfx\\$(Configuration);{tpLibDir}\\bx\\$(Configuration);{tpLibDir}\\bimg\\$(Configuration);%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories>");
            sb.AppendLine("      <AdditionalDependencies>bgfx.lib;bx.lib;bimg.lib;d3d11.lib;dxgi.lib;d3dcompiler.lib;%(AdditionalDependencies)</AdditionalDependencies>");
            sb.AppendLine("    </Link>");
            sb.AppendLine("  </ItemDefinitionGroup>");
        }

        // Source files must appear before Microsoft.Cpp.targets
        sb.AppendLine("  <ItemGroup>");
        foreach (var f in sources)
        {
            var rel = Path.GetRelativePath(projDir, f).Replace('/', '\\');
            sb.AppendLine($"    <ClCompile Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("  <ItemGroup>");
        foreach (var f in headers)
        {
            var rel = Path.GetRelativePath(projDir, f).Replace('/', '\\');
            sb.AppendLine($"    <ClInclude Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.targets\" />");
        sb.AppendLine("  <ImportGroup Label=\"ExtensionTargets\">");
        sb.AppendLine("  </ImportGroup>");
        sb.AppendLine("</Project>");

        File.WriteAllText(projPath, sb.ToString(), Encoding.UTF8);
        return projPath;
    }

    /// <summary>
    /// Generates a .vcxproj.user file so that F5 on the C++ DLL project launches
    /// the editor executable with --debug. Mirrors FlaxEngine's
    /// IVisualStudioProjectCustomizer pattern (WindowsPlatform injects
    /// LocalDebuggerCommand into the .user file).
    /// </summary>
    private void GenerateVcxprojUser(string vcxprojPath)
    {
        var projDir = Path.GetDirectoryName(vcxprojPath)!;
        var projToRoot = Path.GetRelativePath(projDir, _root).Replace('/', '\\');

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project ToolsVersion=\"Current\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        foreach (var cfg in new[] { "Debug", "Develop" })
        {
            var editorExe = Path.Combine(_root, "Binaries", "Windows", cfg, "JzRE.Editor.exe");
            var editorExeRel = Path.GetRelativePath(projDir, editorExe).Replace('/', '\\');
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\">");
            sb.AppendLine($"    <LocalDebuggerCommand>{editorExeRel}</LocalDebuggerCommand>");
            sb.AppendLine("    <LocalDebuggerCommandArguments>--debug</LocalDebuggerCommandArguments>");
            sb.AppendLine($"    <LocalDebuggerWorkingDirectory>$(ProjectDir){projToRoot}</LocalDebuggerWorkingDirectory>");
            sb.AppendLine("    <DebuggerFlavor>WindowsLocalDebugger</DebuggerFlavor>");
            sb.AppendLine("  </PropertyGroup>");
        }
        sb.AppendLine("</Project>");

        File.WriteAllText(vcxprojPath + ".user", sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Generates Properties/launchSettings.json for a C# project so that
    /// Visual Studio can launch it with native debugging enabled (Mixed mode).
    /// When the user sets Debug Type to "Mixed (.NET Core)" in VS project
    /// properties, the nativeDebugging field is toggled to true and both the
    /// managed and native debuggers attach to the same process.
    /// Uses absolute paths like FlaxEngine — relative paths in launchSettings.json
    /// can resolve incorrectly depending on VS version and project layout.
    /// </summary>
    private void GenerateLaunchSettings(string projectDir)
    {
        var propsDir = Path.Combine(projectDir, "Properties");
        Directory.CreateDirectory(propsDir);

        var rootNorm = _root.Replace('\\', '/');

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"profiles\": {");
        sb.AppendLine("    \"JzRE.Editor\": {");
        sb.AppendLine("      \"commandName\": \"Executable\",");
        sb.AppendLine($"      \"executablePath\": \"{rootNorm}/Binaries/Windows/$(Configuration)/JzRE.Editor.exe\",");
        sb.AppendLine("      \"commandLineArgs\": \"--debug\",");
        sb.AppendLine($"      \"workingDirectory\": \"{rootNorm}\",");
        sb.AppendLine("      \"nativeDebugging\": true");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("");

        File.WriteAllText(Path.Combine(propsDir, "launchSettings.json"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Generates {ModuleDir}/{Module.BinaryModuleName}.csproj next to the
    /// module's .Build.cs file. The csproj includes all C# sources under the
    /// module directory and references bindings (.Gen.cs) from HasBindings modules.
    /// </summary>
    private string GenerateCsproj(Module module, List<Module> allModules)
    {
        var projDir    = module.ModuleDirectory!;
        var projPath   = Path.Combine(projDir, $"{module.BinaryModuleName}.csproj");
        var projToRoot = Path.GetRelativePath(projDir, _root).Replace('/', '\\');
        var projToSrc  = Path.GetRelativePath(projDir, Path.Combine(_root, "Source")).Replace('/', '\\');

        // Build Gen.cs includes for all modules that generate bindings
        var genCsIncludes = new List<string>();
        foreach (var m in allModules.Where(m => m.HasBindings && m.ModuleDirectory != null))
        {
            var rel = Path.GetRelativePath(projDir, m.ModuleDirectory!).Replace('/', '\\');
            genCsIncludes.Add($"{rel}\\*.Gen.cs");
        }

        var nugetPkgs = string.Join("\n", (module.NuGetPackages ?? new List<string>())
            .Select(p =>
            {
                var parts = p.Split(' ', 2);
                var name = parts[0];
                var version = parts.Length > 1 ? parts[1] : "*";
                return $"    <PackageReference Include=\"{name}\" Version=\"{version}\" />";
            }));

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>WinExe</OutputType>");
        sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
        sb.AppendLine($"    <AssemblyName>{module.BinaryModuleName}</AssemblyName>");
        sb.AppendLine($"    <RootNamespace>{module.Name}</RootNamespace>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine($"    <OutputPath>{projToRoot}\\Binaries\\Windows\\$(Configuration)\\</OutputPath>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine("    <Configurations>Debug;Develop;Release</Configurations>");
        sb.AppendLine("    <Platforms>x64</Platforms>");
        sb.AppendLine("    <ApplicationManifest>app.manifest</ApplicationManifest>");
        sb.AppendLine("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");
        sb.AppendLine("    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        if (!string.IsNullOrEmpty(nugetPkgs))
            sb.AppendLine(nugetPkgs);
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <!-- Compile C# sources in the module directory and generated bindings. -->");
        sb.AppendLine("    <Compile Include=\"**\\*.cs\" Exclude=\"obj\\**;bin\\**;*.Build.cs\" />");
        foreach (var inc in genCsIncludes)
            sb.AppendLine($"    <Compile Include=\"{inc}\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <!-- Embed app.manifest into the apphost exe (required by NativeControlHost). -->");
        sb.AppendLine("  <Target Name=\"EmbedWin32Manifest\" AfterTargets=\"CoreCompile\"");
        sb.AppendLine("          Condition=\"'$(OS)' == 'Windows_NT' And '$(ApplicationManifest)' != '' And Exists('$(AppHostIntermediatePath)')\">");
        sb.AppendLine("    <Exec Command=\"mt.exe -manifest &quot;$(ApplicationManifest)&quot; -outputresource:&quot;$(AppHostIntermediatePath)&quot;;1\"");
        sb.AppendLine("          WorkingDirectory=\"$(MSBuildProjectDirectory)\"");
        sb.AppendLine("          ContinueOnError=\"WarnAndContinue\" />");
        sb.AppendLine("  </Target>");
        sb.AppendLine("  <!-- Compile bgfx shaders to platform-specific .bin files. -->");
        sb.AppendLine("  <Target Name=\"CompileShaders\" BeforeTargets=\"BeforeBuild\"");
        sb.AppendLine("          Condition=\"'$(OS)' == 'Windows_NT'\">");
        sb.AppendLine("    <PropertyGroup>");
        sb.AppendLine($"      <ShaderSrcDir>$(MSBuildProjectDirectory)\\{projToSrc}\\Shaders</ShaderSrcDir>");
        sb.AppendLine("      <ShaderOutDir>$(OutputPath)Shaders</ShaderOutDir>");
        sb.AppendLine($"      <ShaderC>$(MSBuildProjectDirectory)\\{projToSrc}\\ThirdParty\\bgfx.cmake\\build\\cmake\\bgfx\\$(Configuration)\\shaderc.exe</ShaderC>");
        sb.AppendLine($"      <BgfxInclude>$(MSBuildProjectDirectory)\\{projToSrc}\\ThirdParty\\bgfx.cmake\\bgfx\\src</BgfxInclude>");
        sb.AppendLine("    </PropertyGroup>");
        sb.AppendLine("    <MakeDir Directories=\"$(ShaderOutDir)\" />");
        sb.AppendLine("    <Exec Command=\"&quot;$(ShaderC)&quot; -f &quot;$(ShaderSrcDir)\\vs_basic.sc&quot; -o &quot;$(ShaderOutDir)\\vs_basic.bin&quot; --varyingdef &quot;$(ShaderSrcDir)\\varying.def.sc&quot; --platform windows -p s_5_0 --type vertex -i &quot;$(BgfxInclude)&quot;\"");
        sb.AppendLine("          WorkingDirectory=\"$(MSBuildProjectDirectory)\" />");
        sb.AppendLine("    <Exec Command=\"&quot;$(ShaderC)&quot; -f &quot;$(ShaderSrcDir)\\fs_basic.sc&quot; -o &quot;$(ShaderOutDir)\\fs_basic.bin&quot; --varyingdef &quot;$(ShaderSrcDir)\\varying.def.sc&quot; --platform windows -p s_5_0 --type fragment -i &quot;$(BgfxInclude)&quot;\"");
        sb.AppendLine("          WorkingDirectory=\"$(MSBuildProjectDirectory)\" />");
        sb.AppendLine("  </Target>");
        sb.AppendLine("</Project>");

        File.WriteAllText(projPath, sb.ToString(), Encoding.UTF8);
        return projPath;
    }

    // ── Module Discovery ────────────────────────────────────────────────────

    private List<Module> DiscoverModules()
    {
        var srcDir     = Path.Combine(_root, "Source");
        var buildFiles = Directory.GetFiles(srcDir, "*.Build.cs", SearchOption.AllDirectories);
        var modules    = new List<Module>();

        foreach (var file in buildFiles)
        {
            if (_opts.Verbose) Console.WriteLine($"  Loading {Path.GetFileName(file)}");
            try
            {
                var asm = CompileBuildScript(file);
                if (asm == null) continue;
                foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Module))))
                    if (Activator.CreateInstance(t) is Module m)
                    {
                        m.ModuleDirectory = Path.GetDirectoryName(Path.GetFullPath(file))!;
                        modules.Add(m);
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not load {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Compiles a *.Build.cs module definition file in-memory using Roslyn,
    /// then loads the resulting assembly — same technique Flax.Build uses.
    /// </summary>
    private Assembly? CompileBuildScript(string csFile)
    {
        var src  = File.ReadAllText(csFile);
        var tree = CSharpSyntaxTree.ParseText(src);

        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var comp = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(csFile),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = comp.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new Exception($"Compilation failed:\n{errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }

    // ── C# Build ────────────────────────────────────────────────────────────

    private void BuildCSharpModule(Module module)
    {
        var projFile = FindCsproj(module);

        if (projFile == null)
        {
            Console.WriteLine($"  No .csproj found for {module.Name}, skipping.");
            return;
        }

        var outDir = Path.Combine(_root, "Binaries", _opts.Platform, _opts.Configuration.ToString());
        Directory.CreateDirectory(outDir);

        int exit = RunProcess("dotnet", $"build \"{projFile}\" -c {_opts.Configuration} -o \"{outDir}\" --nologo");
        if (exit != 0) throw new Exception($"C# build failed for {module.Name} (exit {exit})");
    }

    // Look for generated csproj in the module’s directory (next to its .Build.cs).
    private string? FindCsproj(Module module)
    {
        if (module.ModuleDirectory == null) return null;
        var path = Path.Combine(module.ModuleDirectory, $"{module.BinaryModuleName}.csproj");
        return File.Exists(path) ? path : null;
    }

    private static int RunProcess(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
