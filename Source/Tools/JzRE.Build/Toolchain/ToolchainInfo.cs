namespace JzRE.Build;

/// <summary>
/// Abstract toolchain — provides compiler flags and build logic for a specific
/// platform/compiler pair (MSVC, GCC, Clang).  Each subclass knows how to
/// translate the common BuildOptions into its compiler's native command line.
/// </summary>
public abstract class ToolchainInfo
{
    protected readonly BuildOptions _opts;
    protected readonly string       _root;

    protected ToolchainInfo(BuildOptions opts, string root)
    {
        _opts = opts;
        _root = root;
    }

    public abstract string CompilerPath { get; }
    public abstract string[] CompilerArgs(Module module, string[] sources, string includes, string outDir);

    public void Compile(Module module)
    {
        var srcDir  = module.ModuleDirectory!;
        var outDir  = Path.Combine(_root, "Binaries", _opts.Platform, _opts.Configuration.ToString());
        Directory.CreateDirectory(outDir);

        var sources   = Directory.GetFiles(srcDir, "*.cpp", SearchOption.AllDirectories);
        var includes  = string.Join(" ", PlatformIncludes(srcDir).Concat(ThirdPartyIncludes));
        var args      = CompilerArgs(module, sources, includes, outDir);

        // Flatten args into a single command-line string.  Multi-token flags
        // (e.g. "/Zi /FS", "/Od /D_DEBUG") are passed as individual entries
        // in the args array, but ProcessStartInfo's IEnumerable<string> ctor
        // passes each entry as a separate argument.  We flatten everything
        // back into a single string so cl.exe/g++/clang++ parse it correctly.
        var cmdLine = string.Join(" ", args);
        if (_opts.Verbose) Console.WriteLine($"  {CompilerPath} {cmdLine}");

        var psi = new System.Diagnostics.ProcessStartInfo(CompilerPath, cmdLine)
        {
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(CompilerPath)} exited with {proc.ExitCode}");

        var ext = OutputExtension;
        Console.WriteLine($"  -> {module.BinaryModuleName}{ext}");
    }

    // ── Abstract properties (override per toolchain) ──────────────────────

    protected abstract string OutputExtension { get; }    // .dll / .so / .dylib
    protected abstract string DebugFlags { get; }
    protected abstract string DevelopFlags { get; }
    protected abstract string ReleaseFlags { get; }

    // ── Common helpers ────────────────────────────────────────────────────

    protected string ConfigurationFlags => _opts.Configuration switch
    {
        TargetConfiguration.Debug       => DebugFlags,
        TargetConfiguration.Develop => DevelopFlags,
        TargetConfiguration.Release     => ReleaseFlags,
        _                               => DebugFlags,
    };

    /// <summary>Include flags for the JzRE Runtime source tree.</summary>
    protected virtual string[] PlatformIncludes(string srcDir) => new[]
    {
        $"-I\"{srcDir}\"",
        $"-I\"{srcDir}/Core\"",
        $"-I\"{srcDir}/Rendering\"",
        $"-I\"{srcDir}/Scripting\"",
    };

    /// <summary>Include flags for ThirdParty libraries (bgfx, bx, bimg).</summary>
    protected string[] ThirdPartyIncludes
    {
        get
        {
            var tp = Path.Combine(_root, "Source", "ThirdParty", "bgfx.cmake");
            return new[]
            {
                $"-I\"{tp}/bgfx/include\"",
                $"-I\"{tp}/bx/include\"",
                $"-I\"{tp}/bimg/include\"",
            };
        }
    }

    /// <summary>Library search path containing prebuilt ThirdParty libs.</summary>
    protected string ThirdPartyLibPath =>
        Path.Combine(_root, "Source", "ThirdParty", "lib", _opts.Platform);

    protected string SourcesRsp(string[] sources)
    {
        var rsp = Path.Combine(Path.GetTempPath(), "jzre_sources.rsp");
        File.WriteAllLines(rsp, sources.Select(s => $"\"{s}\""));
        return rsp;
    }
}
