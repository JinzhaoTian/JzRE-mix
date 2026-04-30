namespace JzRE.Build;

/// <summary>
/// MSVC toolchain for Windows.  Produces .dll with PDB debug info.
/// Inherits the VC environment from the caller (vcvarsall.bat or equivalent).
/// </summary>
public class MSVCToolchain : ToolchainInfo
{
    public MSVCToolchain(BuildOptions opts, string root) : base(opts, root) { }

    public override string CompilerPath => VSLocator.FindClExe() ?? "cl.exe";

    protected override string OutputExtension => ".dll";
    protected override string DebugFlags       => "/Od /MDd /D_DEBUG";
    protected override string DevelopFlags => "/O2 /MD /D_DEBUG /DBUILD_DEVELOP";
    protected override string ReleaseFlags     => "/O2 /MD /DNDEBUG";

    // Debug info is always generated (including Release) so PDBs are available
    // for crash dumps and joint C++/C# debugging — mirrors FlaxEngine's policy.
    private string DebugInfoFlags => "/Zi /FS";

    protected override string[] PlatformIncludes(string srcDir) => new[]
    {
        $"/I\"{srcDir}\"",
        $"/I\"{srcDir}\\Core\"",
        $"/I\"{srcDir}\\Rendering\"",
        $"/I\"{srcDir}\\Scripting\"",
    };

    public override string[] CompilerArgs(Module module, string[] sources, string includes, string outDir)
    {
        var outDll  = Path.Combine(outDir, $"{module.BinaryModuleName}.dll");
        var outPdb  = Path.Combine(outDir, $"{module.BinaryModuleName}.pdb");
        var cfg     = ConfigurationFlags;
        var rsp     = SourcesRsp(sources);

        var bxDebug = _opts.Configuration == TargetConfiguration.Debug ? "1" : "0";

        // bgfx prebuilt libs + system SDK libs for D3D11 + Windows API
        var libDir  = ThirdPartyLibPath;
        var libs    = $"/LIBPATH:\"{libDir}\" bgfx.lib bx.lib bimg.lib d3d11.lib dxgi.lib d3dcompiler.lib user32.lib gdi32.lib ole32.lib advapi32.lib";

        return new[]
        {
            "/nologo",
            "/std:c++20",
            "/Zc:__cplusplus",
            "/Zc:preprocessor",
            "/utf-8",
            "/EHsc",
            DebugInfoFlags,
            cfg,
            "/DJzRE_RUNTIME_EXPORTS",
            $"/DBX_CONFIG_DEBUG={bxDebug}",
            "/DNOMINMAX",
            "/D_CRT_SECURE_NO_WARNINGS",
            includes,
            $"@{rsp}",
            $"/Fe:\"{outDll}\"",
            $"/Fo{outDir.TrimEnd('\\')}\\",
            $"/Fd:\"{outPdb}\"",
            "/LD",
            "/link", "/DEBUG:FULL", libs
        };
    }
}
