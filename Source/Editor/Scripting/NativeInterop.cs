// NativeInterop.cs — managed methods that the native runtime calls into.
// Mirrors FlaxEngine's FlaxEngine.Interop.NativeInterop class.
//
// These are exposed via [UnmanagedCallersOnly] so the native side can
// obtain function pointers through hostfxr and call them directly.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JzRE.Scripting;

public static class NativeInterop
{
    // ── One-time initialization ──────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_Init")]
    public static void Init()
    {
        // Placeholder for runtime initialization.
        // In a full engine this would cache reflection data, set up
        // assembly load hooks, etc.
    }

    // ── Logging bridge ───────────────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_Log")]
    public static void Log(int level, IntPtr messagePtr)
    {
        string message = Marshalling.Utf8ToString(messagePtr);
        switch (level)
        {
            case 0: Console.WriteLine($"[NATIVE] {message}"); break;
            case 1: Console.WriteLine($"[NATIVE/WARN] {message}"); break;
            case 2: Console.Error.WriteLine($"[NATIVE/ERR] {message}"); break;
            default: Console.WriteLine($"[NATIVE] {message}"); break;
        }
    }

    // ── Managed peer creation ────────────────────────────────────────────

    /// <summary>
    /// Called by the native runtime to create a managed peer for a native object.
    /// typeName is a UTF-8 string like "JzRE.Script" or "JzRE.Renderer".
    /// Returns a GCHandle IntPtr that the native side stores in JzObject::_gcHandle.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_CreateManagedObject")]
    public static IntPtr CreateManagedObject(IntPtr typeNamePtr, IntPtr unmanagedPtr, IntPtr idPtr)
    {
        string typeName = Marshalling.Utf8ToString(typeNamePtr);
        Guid id = Marshal.PtrToStructure<Guid>(idPtr);

        // Resolve type (simple name-based lookup for v0.1)
        Type? type = Type.GetType(typeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => a.GetType(typeName))
                           .FirstOrDefault(t => t != null);

        if (type is null)
        {
            Console.Error.WriteLine($"[NativeInterop] Unknown managed type: {typeName}");
            return IntPtr.Zero;
        }

        object? instance = Activator.CreateInstance(type);
        if (instance is JzRE.Object obj)
        {
            obj.SetInternalValues(unmanagedPtr, id);
            return Marshalling.CreateGCHandle(obj);
        }

        return IntPtr.Zero;
    }

    // ── GCHandle management (called from native) ─────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_FreeGCHandle")]
    public static void FreeGCHandle(IntPtr handle)
    {
        Marshalling.FreeGCHandle(handle);
    }

    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_GetGCHandleTarget")]
    public static IntPtr GetGCHandleTarget(IntPtr handle)
    {
        object? target = Marshalling.GetGCHandleTarget(handle);
        if (target is JzRE.Object obj)
            return obj.__unmanagedPtr;
        return IntPtr.Zero;
    }

    // ── Free native memory (called from C++ via function pointer) ───────

    [UnmanagedCallersOnly(EntryPoint = "NativeInterop_FreeNativeMemory")]
    public static void FreeNativeMemory(IntPtr ptr)
    {
        Marshalling.FreeNativeMemory(ptr);
    }
}
