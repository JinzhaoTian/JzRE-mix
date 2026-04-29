using System.Runtime.InteropServices;

namespace JzRE.Build;

/// <summary>
/// Detects the current platform and returns the best available toolchain
/// (MSVC on Windows, GCC or Clang on Linux/macOS).
/// </summary>
public static class ToolchainLocator
{
    public static ToolchainInfo Resolve(BuildOptions opts, string root)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new MSVCToolchain(opts, root);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: prefer system Clang (xcrun clang++)
            if (FindInPath("clang++") != null)
                return new ClangToolchain(opts, root);
            if (FindInPath("g++") != null)
                return new GCCToolchain(opts, root);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (FindInPath("g++") != null)
                return new GCCToolchain(opts, root);
            if (FindInPath("clang++") != null)
                return new ClangToolchain(opts, root);
        }

        throw new Exception(
            "No C++ compiler found. Install Visual Studio (Windows), " +
            "Xcode Command Line Tools (macOS), or GCC (Linux).");
    }

    private static string? FindInPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
                   .Select(dir => Path.Combine(dir.Trim(), exe))
                   .FirstOrDefault(File.Exists);
    }
}
