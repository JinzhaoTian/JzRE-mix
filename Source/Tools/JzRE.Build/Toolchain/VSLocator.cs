using System.Diagnostics;

namespace JzRE.Build;

/// <summary>
/// Locates the Visual Studio installation and detects the MSVC platform toolset
/// version so that generated .vcxproj files use whatever is actually installed
/// rather than a hardcoded "v143".
/// </summary>
public static class VSLocator
{
    private static string? _vsPath;
    private static string? _msvcVersion;

    // ── VS install path ──────────────────────────────────────────────────────

    public static string? FindVSInstallPath()
    {
        if (_vsPath != null) return _vsPath;

        var vsWhere = FindVsWhere();
        if (vsWhere == null) return null;

        var psi = new ProcessStartInfo(
            vsWhere,
            "-latest -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationPath")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var line = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit();
            _vsPath = string.IsNullOrEmpty(line) ? null : line;
        }
        catch { _vsPath = null; }

        return _vsPath;
    }

    // ── MSVC folder version (e.g. "14.38.33130") ────────────────────────────

    public static string? FindMSVCVersion()
    {
        if (_msvcVersion != null) return _msvcVersion;

        var vsPath = FindVSInstallPath();
        if (vsPath == null) return null;

        var toolsBase = Path.Combine(vsPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(toolsBase)) return null;

        _msvcVersion = Directory.GetDirectories(toolsBase)
                                .Select(Path.GetFileName)
                                .Where(v => v != null)
                                .OrderByDescending(v => v)
                                .FirstOrDefault();
        return _msvcVersion;
    }

    // ── Platform toolset string ──────────────────────────────────────────────

    /// <summary>
    /// Converts an MSVC folder version to a VS platform toolset identifier:
    ///   "14.4x.xxxxx" -> "v144"  (VS 2022 17.x)
    ///   "14.3x.xxxxx" -> "v143"  (VS 2022 earlier)
    ///   "14.2x.xxxxx" -> "v142"  (VS 2019)
    ///   "14.1x.xxxxx" -> "v141"  (VS 2017)
    /// The rule: "vMAJOR + first digit of MINOR"
    /// </summary>
    public static string VersionToToolset(string msvcVersion)
    {
        var parts = msvcVersion.Split('.');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out int major)
            && parts[1].Length > 0
            && char.IsDigit(parts[1][0]))
        {
            return $"v{major}{parts[1][0]}";
        }
        return "v143"; // safe fallback
    }

    /// <summary>
    /// Returns the platform toolset string for the newest installed VS, e.g. "v143".
    /// Falls back to "v143" if detection fails.
    /// </summary>
    public static string DetectPlatformToolset()
    {
        var ver = FindMSVCVersion();
        return ver != null ? VersionToToolset(ver) : "v143";
    }

    // ── cl.exe path ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full path to cl.exe. First checks PATH (vcvarsall already run),
    /// then falls back to vswhere detection.
    /// </summary>
    public static string? FindClExe()
    {
        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';')
            .Select(dir => Path.Combine(dir.Trim(), "cl.exe"))
            .FirstOrDefault(File.Exists);
        if (fromPath != null) return fromPath;

        var vsPath = FindVSInstallPath();
        if (vsPath == null) return null;
        var ver = FindMSVCVersion();
        if (ver == null) return null;
        var cl = Path.Combine(vsPath, "VC", "Tools", "MSVC", ver, "bin", "Hostx64", "x64", "cl.exe");
        return File.Exists(cl) ? cl : null;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string? FindVsWhere() =>
        new[]
        {
            @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
            @"C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe",
        }.FirstOrDefault(File.Exists);
}
