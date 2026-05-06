// GCHandleInterop.cs — [UnmanagedCallersOnly] callbacks for GCHandle lifetime management.
// Registered with the native side via ScriptEngine.RegisterInteropCallbacks.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JzRE.Scripting;

public static class GarbageCollection
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void FreeGCHandle(IntPtr handle)
    {
        MarshallingUtils.FreeGCHandle(handle);
    }
}
