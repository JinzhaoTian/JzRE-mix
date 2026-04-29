using JzRE.Build;

/// <summary>
/// JzRE.Build tool project descriptor. Mirrors FlaxEngine's Flax.Build.Build.cs pattern.
/// </summary>
public class JzREBuildTool : Module
{
    public override string Name => "JzRE.Build";
    public override string BinaryModuleName => "JzRE.Build";
    public override bool BuildNativeCode => false;
    public override bool BuildCSharp => true;

    public override void Setup(BuildOptions options)
    {
        CustomExternalProjectFilePath = System.IO.Path.Combine(ModuleDirectory!, "JzRE.Build.csproj");
    }
}
