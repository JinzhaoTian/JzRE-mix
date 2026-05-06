// NativeInterop.cs — global managed entry points called from C++ via registered function pointers.
// Per-class managed peer creation is handled by the generated bindings
// (CreateManagedPeer callback + SetManagedPeerFactory in the .Gen.cs file).
//
// C# passes these as [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })] function pointers to
// ScriptEngine.RegisterInteropCallbacks before ScriptEngine.Init.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JzRE.Scripting;

public static class NativeInterop
{
    // ── Logging bridge ───────────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Log(int level, IntPtr messagePtr)
    {
        string message = MarshallingUtils.Utf8ToString(messagePtr);
        switch (level)
        {
            case 0: Console.WriteLine($"[NATIVE] {message}"); break;
            case 1: Console.WriteLine($"[NATIVE/WARN] {message}"); break;
            case 2: Console.Error.WriteLine($"[NATIVE/ERR] {message}"); break;
            default: Console.WriteLine($"[NATIVE] {message}"); break;
        }
    }

    // ── GCHandle management ──────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void FreeGCHandle(IntPtr handle)
    {
        MarshallingUtils.FreeGCHandle(handle);
    }
}
