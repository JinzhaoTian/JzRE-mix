#include "Object.h"
#include <unordered_map>
#include <cstdlib>

// ── Global object registry ────────────────────────────────────────────────────
// Maps managed GCHandle → native JzObject*.  This allows the internal call
// layer to resolve a native pointer from the managed peer.
//
// In a full engine this would be integrated with the Scripting service;
// for Phase 2 a simple static map suffices.

static std::unordered_map<void*, JzObject*> s_managedToNative;

uint32_t JzObject::s_nextId = 1;

JzObject::JzObject()
    : _objectId(s_nextId++)
{
}

JzObject::~JzObject()
{
    DestroyManaged();
}

void JzObject::SetManagedInstance(void* gcHandle)
{
    _gcHandle = gcHandle;
    if (gcHandle)
        s_managedToNative[gcHandle] = this;
}

void JzObject::DestroyManaged()
{
    if (_gcHandle)
    {
        s_managedToNative.erase(_gcHandle);
        // The GCHandle itself is freed by the managed side (Object.Dispose)
        // or by the native interop bridge (NativeInterop.FreeGCHandle).
        _gcHandle = nullptr;
    }
}

void JzObject::OnManagedInstanceDeleted()
{
    // The managed peer is gone — clear our reference but don't free the
    // handle (C# already did).  Subclasses may override for cleanup.
    if (_gcHandle)
    {
        s_managedToNative.erase(_gcHandle);
        _gcHandle = nullptr;
    }
}

JzObject* JzObject::FromManaged(void* managedObj)
{
    auto it = s_managedToNative.find(managedObj);
    return it != s_managedToNative.end() ? it->second : nullptr;
}

// ── Exported C API (P/Invoke boundary) ─────────────────────────────────────

API_EXPORT() void ObjectInternal_Destroy(void* obj, float /*timeLeft*/)
{
    if (!obj) return;
    auto* native = static_cast<JzObject*>(obj);
    native->OnManagedInstanceDeleted();
    delete native;
}

API_EXPORT() void* ObjectInternal_FindObject(void* nativePtr)
{
    if (!nativePtr) return nullptr;
    return static_cast<JzObject*>(nativePtr)->GetManagedInstance();
}

API_EXPORT() void ObjectInternal_ManagedInstanceDeleted(void* obj)
{
    if (!obj) return;
    static_cast<JzObject*>(obj)->OnManagedInstanceDeleted();
}
