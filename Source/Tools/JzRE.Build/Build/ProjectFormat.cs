namespace JzRE.Build;

/// <summary>
/// Project file format — mirrors FlaxEngine's ProjectFormat concept.
/// Each platform has a default format; users can override via CLI flags.
/// </summary>
public enum ProjectFormat
{
    /// <summary>Visual Studio (auto-detect newest installed version).</summary>
    VisualStudio,

    /// <summary>Visual Studio 2022 specifically.</summary>
    VisualStudio2022,

    /// <summary>Visual Studio Code (tasks.json + launch.json + c_cpp_properties.json).</summary>
    VisualStudioCode,
}
