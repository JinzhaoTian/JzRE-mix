// ApiTypeInfo.cs — data model representing the parsed C++ API surface.
// Mirrors FlaxEngine's BindingsGenerator ApiTypeInfo.cs hierarchy.
//
// Built by HeaderParser, consumed by CppGenerator / CSharpGenerator.

namespace JzRE.Build.Bindings;

// ── Type kind ─────────────────────────────────────────────────────────────────

public enum ApiTypeKind { Class, Struct, Enum, Interface }
public enum ParamKind   { In, Out, Ref }

// ── Standalone exported C function (API_EXPORT()) ────────────────────────────
// Represents free functions like: API_EXPORT() bool RenderEngine_Create(...)
// These get a flat [LibraryImport] stub rather than a class wrapper.

public class FreeFunctionInfo
{
    public string Name       = "";
    public string ReturnType = "void";
    public List<ParameterInfo> Parameters = new();

    /// Extern "C" symbol name — same as the function name.
    public string EntryPoint => Name;
}

// ── Base type info ────────────────────────────────────────────────────────────

public abstract class ApiTypeInfo
{
    public string  Name         = "";
    public string  Namespace    = "JzRE";
    public string? ParentName   = null;
    public string  NativeModule = "";
    public string  Comment      = "";
    public List<string> Attributes = new();

    public string FullName =>
        string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
}

// ── Function / method ─────────────────────────────────────────────────────────

public class FunctionInfo
{
    public string Name          = "";
    public string ReturnType    = "void";
    public bool   IsVirtual     = false;
    public bool   IsStatic      = false;
    public bool   IsConstructor = false;
    public string ParentName    = "";
    public List<string>        Attributes = new();
    public List<ParameterInfo> Parameters = new();

    // Mirrors FlaxEngine's FunctionInfo.GlueInfo: tracks the actual exported
    // symbol name so C++ and C# sides always agree on the entry point.
    public struct GlueInfo { public string LibraryEntryPoint; }
    public GlueInfo Glue;

    public string InternalCallName =>
        !string.IsNullOrEmpty(Glue.LibraryEntryPoint) ? Glue.LibraryEntryPoint
        : IsConstructor ? $"{ParentName}_Constructor"
        : $"{ParentName}_{Name}";
}

public class ParameterInfo
{
    public string    Name         = "";
    public string    Type         = "void";
    public ParamKind Direction    = ParamKind.In;
    public bool      HasDefault   = false;
    public string    DefaultValue = "";

    public bool IsRef => Direction == ParamKind.Ref;
    public bool IsOut => Direction == ParamKind.Out;
}

// ── Field ─────────────────────────────────────────────────────────────────────

public class FieldInfo
{
    public string Name       = "";
    public string Type       = "";
    public bool   IsReadOnly = false;
    public List<string> Attributes = new();
}

// ── Property (getter/setter pair) ─────────────────────────────────────────────

public class PropertyInfo
{
    public string Name      = "";
    public string Type      = "";
    public bool   HasGetter = true;
    public bool   HasSetter = true;
    public List<string> Attributes = new();
}

// ── Class ─────────────────────────────────────────────────────────────────────

public class ClassInfo : ApiTypeInfo
{
    public ApiTypeKind         Kind       = ApiTypeKind.Class;
    public List<FunctionInfo>  Methods    = new();
    public List<PropertyInfo>  Properties = new();
    public List<FieldInfo>     Fields     = new();
    public string?             ManagedBaseType;

    /// <summary>
    /// When true, the bindings generator emits a managed peer factory so the
    /// native side can create a managed wrapper on demand.  The C# side gets
    /// a <c>CreateManagedPeer</c> callback registered via <c>SetManagedPeerFactory</c>,
    /// and the C++ side gets a <c>{ClassName}_CreateManagedPeer</c> export.
    /// Defaults to true for API_CLASS types.
    /// </summary>
    public bool NeedsManagedPeer = true;

    /// <summary>
    /// When true (set via API_CLASS(Static) annotation), the class contains only
    /// static methods and is not part of the Object hierarchy.  Generators skip
    /// the managed peer factory, managed vtable, and Object base class.
    /// </summary>
    public bool IsStaticClass = false;

    /// <summary>
    /// When true (set via API_CLASS(Abstract) annotation), the class cannot be
    /// directly instantiated.  Generators skip the managed peer factory and
    /// suppress the base-class declaration in the generated partial (since the
    /// hand-written partial already carries the full base-class hierarchy).
    /// </summary>
    public bool IsAbstract = false;

    public ClassInfo() { ManagedBaseType = "Object"; }
}

// ── Struct ────────────────────────────────────────────────────────────────────

public class StructInfo : ApiTypeInfo
{
    public List<FieldInfo> Fields = new();
}

// ── Enum ──────────────────────────────────────────────────────────────────────

public class EnumInfo : ApiTypeInfo
{
    public List<(string Name, int Value)> Values = new();
    public bool IsFlags = false;
}

// ── Interface ─────────────────────────────────────────────────────────────────

public class InterfaceInfo : ApiTypeInfo
{
    public List<FunctionInfo>  Methods    = new();
    public List<PropertyInfo>  Properties = new();
}

// ── File ──────────────────────────────────────────────────────────────────────

public class FileInfo
{
    public string Path = "";
    public List<ClassInfo>        Classes       = new();
    public List<StructInfo>       Structs       = new();
    public List<EnumInfo>         Enums         = new();
    public List<InterfaceInfo>    Interfaces    = new();
    public List<FreeFunctionInfo> FreeFunctions = new();  // API_EXPORT() entries
}

// ── Module ────────────────────────────────────────────────────────────────────

public class ModuleInfo
{
    public string Name              = "";
    public string Namespace         = "JzRE";
    /// DLL name used in [LibraryImport] — defaults to the module binary name.
    public string NativeLibraryName = "JzRE.Runtime";
    public List<FileInfo> Files     = new();

    public IEnumerable<ClassInfo>        AllClasses       => Files.SelectMany(f => f.Classes);
    public IEnumerable<StructInfo>       AllStructs       => Files.SelectMany(f => f.Structs);
    public IEnumerable<FreeFunctionInfo> AllFreeFunctions => Files.SelectMany(f => f.FreeFunctions);
}
