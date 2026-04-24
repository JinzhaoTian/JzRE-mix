namespace JzRE.Build;

/// <summary>
/// Base class for all JzRE build modules — mirrors FlaxEngine's Module pattern.
/// Each module has a corresponding *.Build.cs file in Source/ that subclasses this.
/// JzRE.Build discovers and compiles these at runtime to build the dependency graph.
/// </summary>
public abstract class Module
{
    public virtual string Name             => GetType().Name;
    public virtual string BinaryModuleName => Name;

    /// <summary>Whether this module has C++ source files to compile into a native DLL.</summary>
    public virtual bool BuildNativeCode => true;

    /// <summary>Whether this module has C# source files to compile.</summary>
    public virtual bool BuildCSharp => false;

    public List<string> PublicDependencies  { get; } = new();
    public List<string> PrivateDependencies { get; } = new();

    /// <summary>Called after discovery to configure dependencies and compiler options.</summary>
    public virtual void Setup(BuildOptions options) { }
}
