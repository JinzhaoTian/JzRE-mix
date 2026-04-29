// NativeRuntime.cs — P/Invoke bindings to the native renderer library.
//
// Mirrors FlaxEngine's pattern: BindingsGenerator auto-generates these stubs
// from C++ API_FUNCTION() annotations.  Here we write them manually for clarity.
//
// Uses .NET 8 LibraryImport (source-generated P/Invoke) with cross-platform
// DLL resolution via NativeLibrary.SetDllImportResolver.

using System.Runtime.InteropServices;

namespace JzRE.Editor.Interop;

public static partial class NativeRuntime
{
    private const string Lib = "JzRE.Runtime";

    static NativeRuntime()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeRuntime).Assembly,
            (name, assembly, path) =>
            {
                if (name == Lib)
                {
                    string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll"
                               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib"
                               : ".so";
                    return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, Lib + ext));
                }
                return IntPtr.Zero;
            });
    }

    /// <summary>Initialize renderer bound to the given native window handle.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Create")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Renderer_Create(IntPtr nativeWindow, int x, int y, int width, int height);

    /// <summary>Release all GPU resources.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Destroy")]
    public static partial void Renderer_Destroy();

    /// <summary>Load a Wavefront OBJ file and upload geometry to GPU.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_LoadFile", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Renderer_LoadFile(string path);

    /// <summary>Render one frame.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Render")]
    public static partial void Renderer_Render();

    /// <summary>Resize the render target (call after window resize).</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_Resize")]
    public static partial void Renderer_Resize(int x, int y, int width, int height);

    /// <summary>Set orbit camera parameters.</summary>
    [LibraryImport(Lib, EntryPoint = "Renderer_SetViewAngle")]
    public static partial void Renderer_SetViewAngle(float distance, float pitch, float yaw);

    [LibraryImport(Lib, EntryPoint = "Renderer_GetSuggestedDistance")]
    public static partial float Renderer_GetSuggestedDistance();

    [LibraryImport(Lib, EntryPoint = "Renderer_GetLastError")]
    private static partial IntPtr Renderer_GetLastError_Native();

    /// <summary>Returns the last error string from the native renderer.</summary>
    public static string Renderer_GetLastError() =>
        Marshal.PtrToStringAnsi(Renderer_GetLastError_Native()) ?? string.Empty;

    // ── Scripting Engine (Phase 4) ─────────────────────────────────────────

    [LibraryImport(Lib, EntryPoint = "ScriptingEngine_Init")]
    public static partial void ScriptingEngine_Init();

    [LibraryImport(Lib, EntryPoint = "ScriptingEngine_Update")]
    public static partial void ScriptingEngine_Update(float deltaTime);

    [LibraryImport(Lib, EntryPoint = "ScriptingEngine_Shutdown")]
    public static partial void ScriptingEngine_Shutdown();
}
