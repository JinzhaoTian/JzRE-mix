// NativeInterop.cs — managed entry points called from C++ via registered function pointers.
// C# passes these as [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })] function pointers to
// ScriptingEngine_RegisterInteropCallbacks before ScriptingEngine_Init.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JzRE.Scripting;

public static class NativeInterop
{
    // ── One-time initialization ──────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Init()
    {
        // Reserved for future use: cache reflection data, register assembly load hooks, etc.
    }

    // ── Logging bridge ───────────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
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
    /// Called by ScriptingEngine::RegisterScript to create a managed peer for a native Script.
    /// typeName is a UTF-8 string like "JzRE.Script".
    /// Returns a GCHandle IntPtr stored in JzObject::_gcHandle on the C++ side.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr CreateManagedObject(IntPtr typeNamePtr, IntPtr nativePtr, uint objectId)
    {
        string typeName = Marshalling.Utf8ToString(typeNamePtr);

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
            obj.SetInternalValues(nativePtr, objectId);
            return Marshalling.CreateGCHandle(obj);
        }

        return IntPtr.Zero;
    }

    // ── GCHandle management ──────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void FreeGCHandle(IntPtr handle)
    {
        Marshalling.FreeGCHandle(handle);
    }
}
