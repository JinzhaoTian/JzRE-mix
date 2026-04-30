// MarshallingUtils.cs — managed-side utilities for native interop.

using System.Runtime.InteropServices;

namespace JzRE.Scripting;

internal static class MarshallingUtils
{
    /// <summary>Convert a native UTF-8 string pointer to a managed string.</summary>
    internal static string Utf8ToString(IntPtr ptr)
        => Marshal.PtrToStringUTF8(ptr) ?? string.Empty;

    /// <summary>
    /// Allocate a native UTF-8 string (caller must free with FreeNativeMemory).
    /// </summary>
    internal static IntPtr StringToUtf8(string? s)
    {
        if (s is null) return IntPtr.Zero;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
        IntPtr ptr = Marshal.AllocCoTaskMem(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0); // null terminator
        return ptr;
    }

    /// <summary>Free native memory allocated by this class.</summary>
    internal static void FreeNativeMemory(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
            Marshal.FreeCoTaskMem(ptr);
    }

    /// <summary>
    /// Create a GCHandle for an object to pass to native code.
    /// Use GCHandleType.Normal (not Weak) for simplicity in v0.1.
    /// </summary>
    internal static IntPtr CreateGCHandle(object obj)
        => GCHandle.ToIntPtr(GCHandle.Alloc(obj, GCHandleType.Normal));

    /// <summary>Get the managed object from a GCHandle pointer.</summary>
    internal static object? GetGCHandleTarget(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        return GCHandle.FromIntPtr(handle).Target;
    }

    /// <summary>Free a GCHandle previously created by CreateGCHandle.</summary>
    internal static void FreeGCHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            GCHandle.FromIntPtr(handle).Free();
    }
}
