using System.Diagnostics;

namespace JzRE.Build;

/// <summary>
/// Invokes bgfx's shaderc tool to compile .sc shader sources into
/// platform-specific binary shader files (.bin).
/// </summary>
public static class ShaderCompiler
{
    /// <summary>
    /// Compile all .sc shaders in Source/Shaders/ for the given platform.
    /// Output .bin files go to Binaries/{platform}/Shaders/.
    /// </summary>
    public static void Compile(BuildOptions opts, string root)
    {
        var shaderDir = Path.Combine(root, "Source", "Shaders");
        if (!Directory.Exists(shaderDir))
        {
            Console.WriteLine("  No Source/Shaders/ directory — skipping shader compilation.");
            return;
        }

        // shaderc ships with bgfx but isn't built or staged by SetupDeps.*.
        // Skip silently if it's not on PATH; the runtime DLL still builds.
        if (!IsShadercAvailable())
        {
            Console.WriteLine("  shaderc not on PATH — skipping shader compilation.");
            return;
        }

        var outDir = Path.Combine(root, "Binaries", opts.Platform, "Shaders");
        Directory.CreateDirectory(outDir);

        var profile  = PlatformProfile(opts.Platform);
        var scFiles  = Directory.GetFiles(shaderDir, "*.sc")
                                .Where(f => !f.EndsWith("varying.def.sc"));

        foreach (var sc in scFiles)
        {
            var isVertex = Path.GetFileName(sc).StartsWith("vs_");
            var type     = isVertex ? "vertex" : "fragment";
            var outBin   = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sc) + ".bin");

            Console.WriteLine($"  shaderc {Path.GetFileName(sc)} -> {Path.GetFileName(outBin)}");

            var args = $"-f \"{sc}\" -o \"{outBin}\" --type {type} --platform {opts.Platform.ToLower()} " +
                       $"-p {profile} --varyingdef \"{shaderDir}/varying.def.sc\"" +
                       $" -i \"{shaderDir}\"";

            if (opts.Verbose) Console.WriteLine($"    shaderc {args}");

            var psi = new ProcessStartInfo("shaderc", args) { UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception($"shaderc failed for {Path.GetFileName(sc)} (exit {p.ExitCode})");
        }
    }

    private static string PlatformProfile(string platform) => platform switch
    {
        "Windows" => "vs_5_0",
        "Linux"   => "spirv",
        "MacOS"   => "metal",
        _         => "spirv"
    };

    private static bool IsShadercAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("shaderc", "--version")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
            return p != null;
        }
        catch
        {
            return false;
        }
    }
}
