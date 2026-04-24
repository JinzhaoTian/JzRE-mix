// Runtime.Build.cs — discovered and loaded at runtime by JzRE.Build.
// Mirrors FlaxEngine's *.Build.cs module pattern: inherit Module, override Setup().

using JzRE.Build;

public class JzRERuntime : Module
{
    public override string Name             => "JzRE.Runtime";
    public override string BinaryModuleName => "JzRE.Runtime";
    public override bool   BuildNativeCode  => true;
    public override bool   BuildCSharp      => false;

    public override void Setup(BuildOptions options)
    {
        // Native system libraries linked by the toolchain
        PublicDependencies.Add("d3d11");
        PublicDependencies.Add("dxgi");
        PublicDependencies.Add("d3dcompiler");
    }
}
