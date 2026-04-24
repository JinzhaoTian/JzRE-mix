using System.Diagnostics;

namespace JzRE.Build;

/// <summary>
/// Wraps MSVC (cl.exe) to compile C++ modules into native DLLs.
/// Mirrors FlaxEngine's Windows toolchain: locates VS via vswhere,
/// sets up x64 environment, then invokes cl.exe with appropriate flags.
/// </summary>
public class MSVCToolchain
{
    private readonly BuildOptions _opts;
    private readonly string       _root;

    public MSVCToolchain(BuildOptions opts, string root)
    {
        _opts = opts;
        _root = root;
    }

    public void Compile(Module module)
    {
        var clExe = FindClExe();
        if (clExe == null)
        {
            Console.WriteLine("  MSVC cl.exe not found — skipping native build.");
            Console.WriteLine("  Install Visual Studio with C++ workload.");
            return;
        }

        var srcDir = Path.Combine(_root, "Source", "Runtime");
        var outDir = Path.Combine(_root, "Binaries", _opts.Platform, _opts.Configuration);
        Directory.CreateDirectory(outDir);

        // Write source list to a response file to avoid command-line length limits
        var sources  = Directory.GetFiles(srcDir, "*.cpp", SearchOption.AllDirectories);
        var rspPath  = Path.Combine(Path.GetTempPath(), "jzre_cl.rsp");
        File.WriteAllLines(rspPath, sources.Select(s => $"\"{s}\""));

        var cfg     = _opts.Configuration == "Debug" ? "/Od /Zi /D_DEBUG" : "/O2 /DNDEBUG";
        var outDll  = Path.Combine(outDir, $"{module.BinaryModuleName}.dll");
        var outPdb  = Path.Combine(outDir, $"{module.BinaryModuleName}.pdb");
        var includes = $"/I\"{srcDir}\" /I\"{srcDir}\\Core\" /I\"{srcDir}\\Rendering\" /I\"{srcDir}\\Scripting\"";

        var clArgs = $"/nologo /std:c++17 /EHsc {cfg} /DJZRE_RUNTIME_EXPORTS {includes}" +
                     $" @\"{rspPath}\"" +
                     $" /Fe:\"{outDll}\" /Fo:\"{outDir}\\\\\" /Fd:\"{outPdb}\"" +
                     $" /LD /link d3d11.lib dxgi.lib d3dcompiler.lib";

        if (_opts.Verbose) Console.WriteLine($"  cl.exe {clArgs}");

        var psi = new ProcessStartInfo(clExe, clArgs) { UseShellExecute = false };
        // Inherit the VC environment — caller (Build.bat) already ran vcvarsall.bat
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();

        if (proc.ExitCode != 0) throw new Exception($"cl.exe exited with {proc.ExitCode}");
        Console.WriteLine($"  -> {Path.GetFileName(outDll)}");
    }

    // Delegate VS detection to the shared VSLocator so toolset version is
    // consistent between Build (cl.exe invocation) and GenerateProjectFiles (.vcxproj).
    private static string? FindClExe() => VSLocator.FindClExe();
}
