// ApiTypeInfo.cs — data model representing the parsed C++ API surface.
// Mirrors FlaxEngine's BindingsGenerator ApiTypeInfo.cs hierarchy.
//
// These types are built by HeaderParser and consumed by CppGenerator / CSharpGenerator.

namespace JzRE.Build.Bindings;

// ── Type kind enum ────────────────────────────────────────────────────────

public enum ApiTypeKind
{
    Class,
    Struct,
    Enum,
    Interface
}

// ── Parameter direction ───────────────────────────────────────────────────

public enum ParamKind
{
    In,
    Out,
    Ref
}

// ── Base type info ────────────────────────────────────────────────────────

public abstract class ApiTypeInfo
{
    public string Name          = "";
    public string Namespace     = "JzRE";
    public string? ParentName   = null;
    public string NativeModule  = "";  // e.g. "JzRE.Runtime"
    public string Comment       = "";
    public List<string> Attributes = new();

    /// <summary>Fully qualified C++ type name.</summary>
    public string FullName =>
        string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
}

// ── Function / method ─────────────────────────────────────────────────────

public class FunctionInfo
{
    public string Name          = "";
    public string ReturnType    = "void";
    public bool   IsVirtual     = false;
    public bool   IsStatic      = false;
    public bool   IsConstructor = false;
    public string ParentName    = "";  // Set by HeaderParser to the owning class name
    public List<string> Attributes = new();
    public List<ParameterInfo> Parameters = new();

    /// <summary>Internal call entry point name (e.g. "Renderer_Create").</summary>
    public string InternalCallName =>
        IsConstructor ? $"{ParentName}_Constructor"
                      : $"{ParentName}_{Name}";
}

public class ParameterInfo
{
    public string Name         = "";
    public string Type         = "void";
    public ParamKind Direction = ParamKind.In;
    public bool   HasDefault   = false;
    public string DefaultValue = "";
}

// ── Field ─────────────────────────────────────────────────────────────────

public class FieldInfo
{
    public string Name       = "";
    public string Type       = "";
    public bool   IsReadOnly = false;
    public List<string> Attributes = new();
}

// ── Property (getter/setter pair) ─────────────────────────────────────────

public class PropertyInfo
{
    public string Name       = "";
    public string Type       = "";
    public bool   HasGetter  = true;
    public bool   HasSetter  = true;
    public List<string> Attributes = new();
}

// ── Class ─────────────────────────────────────────────────────────────────

public class ClassInfo : ApiTypeInfo
{
    public ApiTypeKind Kind = ApiTypeKind.Class;
    public List<FunctionInfo> Methods    = new();
    public List<PropertyInfo> Properties = new();
    public List<FieldInfo> Fields        = new();
    public string? ManagedBaseType;  // e.g. "Object" → JzRE.Object

    public ClassInfo()
    {
        ManagedBaseType = "Object";
    }
}

// ── Struct ────────────────────────────────────────────────────────────────

public class StructInfo : ApiTypeInfo
{
    public List<FieldInfo> Fields = new();
}

// ── Enum ──────────────────────────────────────────────────────────────────

public class EnumInfo : ApiTypeInfo
{
    public List<(string Name, int Value)> Values = new();
    public bool IsFlags = false;
}

// ── Interface ─────────────────────────────────────────────────────────────

public class InterfaceInfo : ApiTypeInfo
{
    public List<FunctionInfo> Methods    = new();
    public List<PropertyInfo> Properties = new();
}

// ── File ──────────────────────────────────────────────────────────────────

public class FileInfo
{
    public string Path = "";
    public List<ClassInfo> Classes       = new();
    public List<StructInfo> Structs       = new();
    public List<EnumInfo> Enums           = new();
    public List<InterfaceInfo> Interfaces = new();
}

// ── Module ────────────────────────────────────────────────────────────────

public class ModuleInfo
{
    public string Name = "";
    public string Namespace = "JzRE";
    public List<FileInfo> Files = new();

    /// <summary>All classes across all files in this module.</summary>
    public IEnumerable<ClassInfo> AllClasses =>
        Files.SelectMany(f => f.Classes);

    /// <summary>All structs across all files in this module.</summary>
    public IEnumerable<StructInfo> AllStructs =>
        Files.SelectMany(f => f.Structs);
}
