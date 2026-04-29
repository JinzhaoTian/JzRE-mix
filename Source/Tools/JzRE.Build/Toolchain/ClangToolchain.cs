namespace JzRE.Build;

/// <summary>
/// Clang toolchain for macOS (and optionally Linux).
/// Produces .dylib on macOS, .so on Linux.
/// </summary>
public class ClangToolchain : ToolchainInfo
{
    public ClangToolchain(BuildOptions opts, string root) : base(opts, root) { }

    public override string CompilerPath => "clang++";

    protected override string OutputExtension =>
        OperatingSystem.IsMacOS() ? ".dylib" : ".so";

    // -g always present: debug symbols are generated in all configs for crash
    // dump symbolication and joint C++/C# debugging — mirrors FlaxEngine policy.
    protected override string DebugFlags       => "-Og -g -D_DEBUG";
    protected override string DevelopFlags => "-O2 -g -D_DEBUG -DBUILD_DEVELOP";
    protected override string ReleaseFlags     => "-O2 -g -DNDEBUG";

    public override string[] CompilerArgs(Module module, string[] sources, string includes, string outDir)
    {
        var ext     = OutputExtension;
        var prefix  = "lib";
        var outLib  = Path.Combine(outDir, $"{prefix}{module.BinaryModuleName}{ext}");
        var cfg     = ConfigurationFlags;
        var rsp     = SourcesRsp(sources);
        var libDir  = ThirdPartyLibPath;

        var args = new List<string>
        {
            "-std=c++17",
            "-fPIC",
            "-shared",
            "-fvisibility=hidden",
            cfg,
            "-DJzRE_RUNTIME_EXPORTS",
            includes,
            $"@{rsp}",
            "-o", outLib,
            $"-L\"{libDir}\"",
            $"-L\"{outDir}\"",
            "-l:libbgfx.a",
            "-l:libbx.a",
            "-l:libbimg.a",
        };

        if (OperatingSystem.IsMacOS())
        {
            args.Add("-framework");
            args.Add("Cocoa");
            args.Add("-framework");
            args.Add("Metal");
            args.Add("-framework");
            args.Add("QuartzCore");
        }
        else
        {
            args.Add("-lX11");
            args.Add("-lGL");
            args.Add("-ldl");
            args.Add("-lpthread");
        }

        return args.ToArray();
    }
}
