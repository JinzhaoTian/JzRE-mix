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

        foreach (var m in modules)
        {
            m.Setup(_opts);

            if (m.BuildNativeCode)
            {
                Console.WriteLine($"[C++] {m.Name}");
                new MSVCToolchain(_opts, _root).Compile(m);
            }

            if (m.BuildCSharp)
            {
                Console.WriteLine($"[C#]  {m.Name}");
                BuildCSharpModule(m);
            }
        }

        Console.WriteLine("Build complete.");
    }

    public void GenerateProjectFiles()
    {
        var modules = DiscoverModules();

        // projPlatform: "x64" for C++ vcxproj, "AnyCPU" for C# SDK-style csproj.
        // VS reports "unknown configuration mapping" when a C# project is mapped
        // to x64 — SDK-style projects only define AnyCPU by default.
        var slnProjects = new List<(string name, string relPath, string typeGuid, string projGuid, string projPlatform)>();

        foreach (var m in modules)
        {
            if (m.BuildNativeCode)
            {
                var vcxPath = GenerateVcxproj(m);
                var rel     = Path.GetRelativePath(_root, vcxPath).Replace('/', '\\');
                slnProjects.Add((m.Name, rel, CppProjectTypeGuid, ModuleGuid(m.Name), "x64"));
                Console.WriteLine($"  Generated {rel}");
            }

            if (m.BuildCSharp)
            {
                var csproj = FindCsproj(m);
                if (csproj != null)
                {
                    var rel = Path.GetRelativePath(_root, csproj).Replace('/', '\\');
                    slnProjects.Add((m.Name, rel, CsProjectTypeGuid, ModuleGuid(m.Name), "AnyCPU"));
                    Console.WriteLine($"  Using   {rel}");
                }
            }
        }

        // Build dependency map: projGuid -> list of dependency projGuids
        // Derived from each module's PublicDependencies matched against known project names.
        var nameToGuid = slnProjects.ToDictionary(p => p.name, p => p.projGuid);
        var dependencies = new Dictionary<string, List<string>>();
        foreach (var m in modules)
        {
            m.Setup(_opts);
            var guid = ModuleGuid(m.Name);
            var deps = m.PublicDependencies
                        .Where(nameToGuid.ContainsKey)
                        .Select(dep => nameToGuid[dep])
                        .ToList();
            if (deps.Count > 0) dependencies[guid] = deps;
        }

        var slnPath = Path.Combine(_root, "JzRE.sln");
        GenerateSln(slnPath, slnProjects, dependencies);
        Console.WriteLine($"  Generated JzRE.sln");
        Console.WriteLine($"\nDone. Open {slnPath} in Visual Studio.");
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
        List<(string name, string relPath, string typeGuid, string projGuid, string projPlatform)> projects,
        Dictionary<string, List<string>> dependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        foreach (var (name, relPath, typeGuid, projGuid, _) in projects)
        {
            sb.AppendLine($"Project(\"{typeGuid}\") = \"{name}\", \"{relPath}\", \"{projGuid}\"");
            // ProjectSection(ProjectDependencies) tells VS to build dependencies first.
            // This ensures JzRE.Runtime.dll exists before the C# editor tries to load it.
            if (dependencies.TryGetValue(projGuid, out var depGuids) && depGuids.Count > 0)
            {
                sb.AppendLine("\tProjectSection(ProjectDependencies) = postProject");
                foreach (var dep in depGuids)
                    sb.AppendLine($"\t\t{dep} = {dep}");
                sb.AppendLine("\tEndProjectSection");
            }
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|x64 = Debug|x64");
        sb.AppendLine("\t\tRelease|x64 = Release|x64");
        sb.AppendLine("\tEndGlobalSection");
        // ProjectConfigurationPlatforms maps each solution config+platform to the
        // project's own config+platform.  C# SDK projects only define AnyCPU;
        // mapping them to x64 here is what causes VS's "unknown mapping" warning.
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var (_, _, _, projGuid, projPlatform) in projects)
        {
            sb.AppendLine($"\t\t{projGuid}.Debug|x64.ActiveCfg = Debug|{projPlatform}");
            sb.AppendLine($"\t\t{projGuid}.Debug|x64.Build.0 = Debug|{projPlatform}");
            sb.AppendLine($"\t\t{projGuid}.Release|x64.ActiveCfg = Release|{projPlatform}");
            sb.AppendLine($"\t\t{projGuid}.Release|x64.Build.0 = Release|{projPlatform}");
        }
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");

        File.WriteAllText(slnPath, sb.ToString(), Encoding.UTF8);
    }

    // ── .vcxproj writer ─────────────────────────────────────────────────────

    private string GenerateVcxproj(Module module)
    {
        var srcDir      = Path.Combine(_root, "Source", "Runtime");
        var projDir     = srcDir;
        var projPath    = Path.Combine(projDir, $"{module.BinaryModuleName}.vcxproj");
        var guid        = ModuleGuid(module.Name);
        var outDir      = @"$(SolutionDir)Binaries\Windows\$(Configuration)\";
        var intDir      = @"$(SolutionDir)Cache\$(Configuration)\$(ProjectName)\";
        var includes    = string.Join(";",
            new[] { "$(ProjectDir)", @"$(ProjectDir)Core", @"$(ProjectDir)Rendering", @"$(ProjectDir)Scripting" });

        // Detect the actual installed MSVC toolset (e.g. "v143", "v142", "v141")
        // instead of hardcoding v143 — avoids "missing platform toolset" errors in VS.
        var toolset     = VSLocator.DetectPlatformToolset();
        var msvcVersion = VSLocator.FindMSVCVersion() ?? "unknown";
        Console.WriteLine($"  Toolset: {toolset} (MSVC {msvcVersion})");

        // Collect source files relative to the project directory
        var sources = Directory.GetFiles(srcDir, "*.cpp", SearchOption.AllDirectories);
        var headers = Directory.GetFiles(srcDir, "*.h",   SearchOption.AllDirectories);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project DefaultTargets=\"Build\" ToolsVersion=\"17.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");

        // Configurations
        sb.AppendLine("  <ItemGroup Label=\"ProjectConfigurations\">");
        foreach (var cfg in new[] { "Debug", "Release" })
        {
            sb.AppendLine($"    <ProjectConfiguration Include=\"{cfg}|x64\">");
            sb.AppendLine($"      <Configuration>{cfg}</Configuration>");
            sb.AppendLine($"      <Platform>x64</Platform>");
            sb.AppendLine("    </ProjectConfiguration>");
        }
        sb.AppendLine("  </ItemGroup>");

        // Globals
        sb.AppendLine("  <PropertyGroup Label=\"Globals\">");
        sb.AppendLine("    <VCProjectVersion>17.0</VCProjectVersion>");
        sb.AppendLine($"    <ProjectGuid>{guid}</ProjectGuid>");
        sb.AppendLine($"    <RootNamespace>{module.BinaryModuleName.Replace(".", "_")}</RootNamespace>");
        sb.AppendLine("    <WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>");
        sb.AppendLine("  </PropertyGroup>");

        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.Default.props\" />");

        // Per-config: DLL, toolset
        foreach (var (cfg, useDebug, opt, rt) in new[]
        {
            ("Debug",   "true",  "Disabled",  "MultiThreadedDebugDLL"),
            ("Release", "false", "MaxSpeed",  "MultiThreadedDLL"),
        })
        {
            var defines = cfg == "Debug"
                ? "JZRE_RUNTIME_EXPORTS;NOMINMAX;_DEBUG;_WINDOWS;_USRDLL;%(PreprocessorDefinitions)"
                : "JZRE_RUNTIME_EXPORTS;NOMINMAX;NDEBUG;_WINDOWS;_USRDLL;%(PreprocessorDefinitions)";

            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{cfg}|x64'\" Label=\"Configuration\">");
            sb.AppendLine("    <ConfigurationType>DynamicLibrary</ConfigurationType>");
            sb.AppendLine($"    <UseDebugLibraries>{useDebug}</UseDebugLibraries>");
            sb.AppendLine($"    <PlatformToolset>{toolset}</PlatformToolset>");
            sb.AppendLine("    <CharacterSet>Unicode</CharacterSet>");
            sb.AppendLine("  </PropertyGroup>");

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
            sb.AppendLine("      <LanguageStandard>stdcpp17</LanguageStandard>");
            sb.AppendLine("      <ConformanceMode>true</ConformanceMode>");
            sb.AppendLine("      <ExceptionHandling>Sync</ExceptionHandling>");
            sb.AppendLine($"      <AdditionalIncludeDirectories>{includes};%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>");
            sb.AppendLine("    </ClCompile>");
            sb.AppendLine("    <Link>");
            sb.AppendLine("      <AdditionalDependencies>d3d11.lib;dxgi.lib;d3dcompiler.lib;%(AdditionalDependencies)</AdditionalDependencies>");
            sb.AppendLine("    </Link>");
            sb.AppendLine("  </ItemDefinitionGroup>");
        }

        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.props\" />");
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.targets\" />");

        // Source files
        sb.AppendLine("  <ItemGroup>");
        foreach (var f in sources)
        {
            var rel = Path.GetRelativePath(projDir, f).Replace('/', '\\');
            sb.AppendLine($"    <ClCompile Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        // Header files
        sb.AppendLine("  <ItemGroup>");
        foreach (var f in headers)
        {
            var rel = Path.GetRelativePath(projDir, f).Replace('/', '\\');
            sb.AppendLine($"    <ClInclude Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

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
                        modules.Add(m);
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

        var outDir = Path.Combine(_root, "Binaries", _opts.Platform, _opts.Configuration);
        Directory.CreateDirectory(outDir);

        int exit = RunProcess("dotnet", $"build \"{projFile}\" -c {_opts.Configuration} -o \"{outDir}\" --nologo");
        if (exit != 0) throw new Exception($"C# build failed for {module.Name} (exit {exit})");
    }

    // Match "Editor.csproj" when module.Name = "JzRE.Editor" (last dot-segment match)
    private string? FindCsproj(Module module) =>
        Directory.GetFiles(Path.Combine(_root, "Source"), "*.csproj", SearchOption.AllDirectories)
                 .FirstOrDefault(f =>
                 {
                     var baseName = Path.GetFileNameWithoutExtension(f);
                     return baseName.Equals(module.Name,             StringComparison.OrdinalIgnoreCase)
                         || baseName.Equals(module.BinaryModuleName, StringComparison.OrdinalIgnoreCase)
                         || module.Name.EndsWith("." + baseName,     StringComparison.OrdinalIgnoreCase);
                 });

    private static int RunProcess(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
