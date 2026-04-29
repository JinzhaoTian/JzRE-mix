// Editor.Build.cs — JzRE.Build module descriptor for the C# editor.
// JzRE.Build discovers this file and uses it to trigger `dotnet build Editor.csproj`.

using JzRE.Build;

public class JzREEditor : Module
{
    public override string Name             => "JzRE.Editor";
    public override string BinaryModuleName => "JzRE.Editor";
    public override bool   BuildNativeCode  => false;
    public override bool   BuildCSharp      => true;

    public override void Setup(BuildOptions options)
    {
        // Declares that the editor depends on the native runtime library.
        // JzRE.Build ensures JzRERuntime is compiled before JzREEditor.
        PublicDependencies.Add("JzRE.Runtime");

        NuGetPackages.AddRange(new[]
        {
            "Avalonia.Desktop 11.1.*",
            "Avalonia.Themes.Fluent 11.1.*",
            "Avalonia.Diagnostics 11.1.*"
        });
    }
}
