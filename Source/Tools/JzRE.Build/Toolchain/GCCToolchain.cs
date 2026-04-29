namespace JzRE.Build;

/// <summary>
/// GCC toolchain for Linux (and optionally macOS).
/// Produces .so shared libraries with DWARF debug info.
/// </summary>
public class GCCToolchain : ToolchainInfo
{
    public GCCToolchain(BuildOptions opts, string root) : base(opts, root) { }

    public override string CompilerPath => "g++";

    protected override string OutputExtension => ".so";
    // -g always present: debug symbols are generated in all configs for crash
    // dump symbolication and joint C++/C# debugging — mirrors FlaxEngine policy.
    protected override string DebugFlags       => "-Og -g -D_DEBUG";
    protected override string DevelopFlags => "-O2 -g -D_DEBUG -DBUILD_DEVELOP";
    protected override string ReleaseFlags     => "-O2 -g -DNDEBUG";

    public override string[] CompilerArgs(Module module, string[] sources, string includes, string outDir)
    {
        var outLib  = Path.Combine(outDir, $"lib{module.BinaryModuleName}.so");
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
            "-lX11",
            "-lGL",
            "-ldl",
            "-lpthread",
        };

        return args.ToArray();
    }
}
