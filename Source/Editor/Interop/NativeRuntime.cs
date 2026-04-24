// NativeRuntime.cs — P/Invoke bindings to JzRE.Runtime.dll
//
// Mirrors FlaxEngine's pattern: BindingsGenerator auto-generates these stubs
// from C++ API_FUNCTION() annotations.  Here we write them manually for clarity.
//
// Uses .NET 8 LibraryImport (source-generated P/Invoke) — same approach
// FlaxEngine's generated C# bindings use (see Editor.cs in FlaxEngine).

using System.Runtime.InteropServices;

// Search for JzRE.Runtime.dll next to the editor executable (assembly directory).
// [DefaultDllImportSearchPaths] is only valid at assembly or method level, not class level.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]

namespace JzRE.Editor.Interop;

public static partial class NativeRuntime
{
    private const string Lib = "JzRE.Runtime";

    /// <summary>Initialize D3D11 device + swap chain bound to the given Win32 HWND.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Create")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Renderer_Create(IntPtr hwnd, int width, int height);

    /// <summary>Release all D3D11 resources.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Destroy")]
    public static partial void Renderer_Destroy();

    /// <summary>Load a Wavefront OBJ file and upload geometry to GPU.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_LoadFile", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Renderer_LoadFile(string path);

    /// <summary>Render one frame and call Present.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Render")]
    public static partial void Renderer_Render();

    /// <summary>Resize the swap chain (call after panel resize).</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Resize")]
    public static partial void Renderer_Resize(int width, int height);

    /// <summary>Set orbit camera parameters.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_SetViewAngle")]
    public static partial void Renderer_SetViewAngle(float distance, float pitch, float yaw);

    [LibraryImport(Lib, EntryPoint = "Renderer_GetLastError")]
    private static partial IntPtr Renderer_GetLastError_Native();

    /// <summary>Returns the last error string from the C++ renderer.</summary>
    public static string Renderer_GetLastError() =>
        Marshal.PtrToStringAnsi(Renderer_GetLastError_Native()) ?? string.Empty;
}
