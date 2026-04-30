// JzRE.Object — root class of the managed object hierarchy.
// Mirrors FlaxEngine's FlaxEngine.Object: holds the unmanaged pointer
// and internal ID for the native peer.
//
// This is a partial class — the bindings generator (Phase 3) adds
// Internal_Create, Internal_Destroy, and other internal calls via
// generated partial declarations.

using System.Runtime.InteropServices;

namespace JzRE;

public abstract partial class Object : IDisposable
{
    /// <summary>Pointer to the native JzObject (or subclass).</summary>
    internal IntPtr __unmanagedPtr;

    /// <summary>Unique engine object identifier, mirrors JzObject::_objectId.</summary>
    internal uint __internalId;

    /// <summary>GCHandle that pins this managed object for native access.</summary>
    internal GCHandle __gcHandle;

    /// <summary>True once this object has been disposed or collected.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Called by the native side via NativeInterop when creating a managed peer
    /// for an existing native object.
    /// </summary>
    internal void SetInternalValues(IntPtr unmanagedPtr, uint id)
    {
        __unmanagedPtr = unmanagedPtr;
        __internalId = id;
    }

    /// <summary>
    /// Look up the managed wrapper for a native pointer.
    /// Returns null if no managed peer exists.
    /// </summary>
    public static Object? FromUnmanagedPtr(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        // Internal_FindObject returns the GCHandle IntPtr; resolve to managed object
        var handle = Internal_FindObject(ptr);
        if (handle == IntPtr.Zero) return null;
        return GCHandle.FromIntPtr(handle).Target as Object;
    }

    /// <summary>
    /// Get the unmanaged pointer for a managed object.
    /// Returns IntPtr.Zero if the object is null.
    /// </summary>
    public static IntPtr GetUnmanagedPtr(Object? obj)
        => obj?.__unmanagedPtr ?? IntPtr.Zero;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        if (__unmanagedPtr != IntPtr.Zero)
        {
            Internal_Destroy(__unmanagedPtr, 0.0f);
            __unmanagedPtr = IntPtr.Zero;
        }

        if (__gcHandle.IsAllocated)
            __gcHandle.Free();

        GC.SuppressFinalize(this);
    }

    ~Object()
    {
        if (!IsDisposed)
        {
            // Notify native side that the managed peer is going away
            if (__unmanagedPtr != IntPtr.Zero)
                Internal_ManagedInstanceDeleted(__unmanagedPtr);
        }
    }

    // ── Exported native calls (implemented in ObjectExporter.cpp) ─────────

    [LibraryImport("JzRE.Runtime", EntryPoint = "ObjectInternal_Destroy")]
    private static partial void Internal_Destroy(IntPtr obj, float timeLeft);

    [LibraryImport("JzRE.Runtime", EntryPoint = "ObjectInternal_FindObject")]
    private static partial IntPtr Internal_FindObject(IntPtr ptr);

    [LibraryImport("JzRE.Runtime", EntryPoint = "ObjectInternal_ManagedInstanceDeleted")]
    private static partial void Internal_ManagedInstanceDeleted(IntPtr obj);
}
