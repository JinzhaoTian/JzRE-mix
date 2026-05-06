#include "Object.h"
#include <unordered_map>

// ── Global object registry ────────────────────────────────────────────────────
// Maps managed GCHandle → native Object*.  This allows the internal call
// layer to resolve a native pointer from the managed peer.
//
// In a full engine this would be integrated with the Scripting service;
// for Phase 2 a simple static map suffices.

static std::unordered_map<void*, Object*> s_managedToNative;

uint32_t Object::s_nextId = 1;

Object::Object()
    : _objectId(s_nextId++)
{
}

Object::~Object()
{
    DestroyManaged();
}

void Object::SetManagedInstance(void* gcHandle)
{
    _gcHandle = gcHandle;
    if (gcHandle)
        s_managedToNative[gcHandle] = this;
}

void Object::DestroyManaged()
{
    if (_gcHandle)
    {
        s_managedToNative.erase(_gcHandle);
        // The GCHandle itself is freed by the managed side (Object.Dispose)
        // or by the native interop bridge (NativeInterop.FreeGCHandle).
        _gcHandle = nullptr;
    }
}

void Object::OnManagedInstanceDeleted()
{
    // The managed peer is gone — clear our reference but don't free the
    // handle (C# already did).  Subclasses may override for cleanup.
    if (_gcHandle)
    {
        s_managedToNative.erase(_gcHandle);
        _gcHandle = nullptr;
    }
}

Object* Object::FromManaged(void* managedObj)
{
    auto it = s_managedToNative.find(managedObj);
    return it != s_managedToNative.end() ? it->second : nullptr;
}

