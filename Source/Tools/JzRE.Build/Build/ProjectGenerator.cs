namespace JzRE.Build;

/// <summary>
/// Project file generators for different IDEs.
/// Each generator produces the platform-appropriate project files
/// that the IDE uses to open, build, and debug the project.
/// </summary>
public static class ProjectGeneratorFactory
{
    /// <summary>
    /// Returns the platform-default project format.
    /// Windows → Visual Studio, Linux → VSCode, macOS → VSCode.
    /// Mirrors FlaxEngine's Platform.DefaultProjectFormat pattern.
    /// </summary>
    public static ProjectFormat GetPlatformDefault()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return VSLocator.FindVSInstallPath() != null
                ? ProjectFormat.VisualStudio
                : ProjectFormat.VisualStudioCode;
        }
        return ProjectFormat.VisualStudioCode;
    }
}
