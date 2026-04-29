using System.Diagnostics;

namespace JzRE.Build;

/// <summary>
/// Locates the Visual Studio installation and detects the MSVC platform toolset
/// version so that generated .vcxproj files use whatever is actually installed
/// rather than a hardcoded "v143".
/// Supports version-constrained lookups so that "-vs2022" generates toolset
/// settings for VS 2022 even when a newer VS is also installed.
/// </summary>
public static class VSLocator
{
    private static string? _vsPath;
    private static string? _vsVersion;
    private static string? _msvcVersion;

    // ── vswhere helper ───────────────────────────────────────────────────────

    private static string? RunVsWhere(string args)
    {
        var vsWhere = FindVsWhere();
        if (vsWhere == null) return null;

        var psi = new ProcessStartInfo(vsWhere, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var line = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch { return null; }
    }

    // ── VS install path ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the VS install path, optionally constrained to a maximum major version.
    /// Pass maxMajor=17 to find VS 2022 even when VS 2026+ is also installed.
    /// </summary>
    public static string? FindVSInstallPath(int? maxMajor = null)
    {
        if (maxMajor == null)
        {
            if (_vsPath != null) return _vsPath;
            _vsPath = RunVsWhere(
                "-latest -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationPath");
            return _vsPath;
        }
        return RunVsWhere(
            $"-latest -version \"[1.0,{maxMajor + 1}.0)\" -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationPath");
    }

    // ── VS installation version (e.g. "18.5.11709.299") ─────────────────────

    /// <summary>
    /// Returns the VS shell version string, optionally constrained to a maximum major.
    /// </summary>
    public static string? FindVSVersion(int? maxMajor = null)
    {
        if (maxMajor == null)
        {
            if (_vsVersion != null) return _vsVersion;
            _vsVersion = RunVsWhere(
                "-latest -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationVersion");
            return _vsVersion;
        }
        return RunVsWhere(
            $"-latest -version \"[1.0,{maxMajor + 1}.0)\" -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationVersion");
    }

    /// <summary>
    /// Returns the VCProjectVersion string for the target VS.
    /// VCProjectVersion is the .vcxproj schema version — one fixed value per VS
    /// generation, regardless of update number:
    ///   VS 2022 (shell 17.x) → "17.0"
    ///   VS 2026 (shell 18.x) → "18.0"
    ///   VS 2019 (shell 16.x) → "16.0"
    ///   VS 2017 (shell 15.x) → "15.0"
    /// </summary>
    public static string DetectVCProjectVersion(int? maxMajor = null)
    {
        var ver = FindVSVersion(maxMajor);
        if (ver != null)
        {
            var dot = ver.IndexOf('.');
            if (dot > 0 && int.TryParse(ver[..dot], out int major))
            {
                return $"{major}.0";
            }
        }
        return "17.0";
    }

    // ── MSVC folder version (e.g. "14.44.35207") ────────────────────────────

    /// <summary>
    /// Returns the highest MSVC compiler version installed under the given VS,
    /// optionally constrained to a maximum VS major version.
    /// </summary>
    public static string? FindMSVCVersion(int? maxMajor = null)
    {
        if (maxMajor == null)
        {
            if (_msvcVersion != null) return _msvcVersion;
            var vsPath = FindVSInstallPath();
            _msvcVersion = ReadMSVCVersion(vsPath);
            return _msvcVersion;
        }
        return ReadMSVCVersion(FindVSInstallPath(maxMajor));
    }

    private static string? ReadMSVCVersion(string? vsPath)
    {
        if (vsPath == null) return null;
        var toolsBase = Path.Combine(vsPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(toolsBase)) return null;
        return Directory.GetDirectories(toolsBase)
                        .Select(Path.GetFileName)
                        .Where(v => v != null)
                        .OrderByDescending(v => v)
                        .FirstOrDefault();
    }

    // ── Platform toolset string ──────────────────────────────────────────────

    /// <summary>
    /// Maps a VS shell major version to the platform toolset identifier registered
    /// in that VS installation's MSBuild targets. The toolset ID is a property of
    /// the VS *generation*, not the individual MSVC minor version:
    ///   VS 2026 (18) → "v145"
    ///   VS 2022 (17) → "v143"  (covers all MSVC 14.3x–14.4x in VS 2022)
    ///   VS 2019 (16) → "v142"
    ///   VS 2017 (15) → "v141"
    /// </summary>
    public static string VSMajorToToolset(int vsMajor) => vsMajor switch
    {
        18    => "v145",
        17    => "v143",
        16    => "v142",
        15    => "v141",
        _     => "v143",
    };

    /// <summary>
    /// Returns the platform toolset string for the target VS installation.
    /// Pass maxMajor=17 to get the VS 2022 toolset when VS 2026+ is also installed.
    /// Falls back to "v143" if detection fails.
    /// </summary>
    public static string DetectPlatformToolset(int? maxMajor = null)
    {
        var ver = FindVSVersion(maxMajor);
        if (ver != null)
        {
            var dot = ver.IndexOf('.');
            if (dot > 0 && int.TryParse(ver[..dot], out int major))
                return VSMajorToToolset(major);
        }
        return "v143";
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
