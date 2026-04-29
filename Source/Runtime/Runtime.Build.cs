// Runtime.Build.cs — JzRE.Build module descriptor for the native runtime library.
// JzRE.Build discovers this file and compiles the C++ source via the platform toolchain.

using JzRE.Build;

public class JzRERuntime : Module
{
    public override string Name             => "JzRE.Runtime";
    public override string BinaryModuleName => "JzRE.Runtime";
    public override bool   BuildNativeCode  => true;
    public override bool   BuildCSharp      => false;

    /// <summary>
    /// The Runtime module has API-annotated C++ headers that the bindings
    /// generator parses to produce C# bindings for the Editor to consume.
    /// </summary>
    public override bool HasBindings => true;

    public override void Setup(BuildOptions options)
    {
        // Platform-specific native libraries for linking
        if (options.Platform == "Windows")
        {
            SystemLibraries.AddRange(new[] { "d3d11.lib", "dxgi.lib", "d3dcompiler.lib" });
        }
        else if (options.Platform == "Linux")
        {
            SystemLibraries.AddRange(new[] { "X11", "GL", "dl", "pthread", "bgfx", "bimg", "bx" });
        }
        else if (options.Platform == "MacOS")
        {
            SystemLibraries.AddRange(new[] { "bgfx", "bimg", "bx" });
            Frameworks.AddRange(new[] { "Cocoa", "Metal", "QuartzCore" });
        }
    }
}
