#pragma once
#include "API.h"
#include <cstdint>

// ── Object — root class of the engine object hierarchy ───────────────────────
// Every engine object that has a managed (C#) peer derives from Object.
// Mirrors FlaxEngine's ScriptingObject.
//
// Lifecycle:
//   Native-owned:  C++ creates the object; the C# peer is created on demand
//                  via GetOrCreateManagedInstance().  The C++ destructor
//                  frees the GCHandle.
//   Managed-owned: C# creates the object (new Script()); the C++ peer is
//                  created by ObjectInternal_Create and the GCHandle pins
//                  the managed object.

API_CLASS(Abstract)
class Object
{
public:
    Object();
    virtual ~Object();

    Object(const Object&) = delete;
    Object& operator=(const Object&) = delete;

    // ── Managed peer ─────────────────────────────────────────────────

    API_FUNCTION()
    void* GetManagedInstance() const { return _gcHandle; }

    API_PROPERTY()
    bool HasManagedInstance() const { return _gcHandle != nullptr; }

    API_FUNCTION()
    virtual void OnManagedInstanceDeleted();

    // ── Object identity ──────────────────────────────────────────────

    API_PROPERTY()
    uint32_t GetObjectId() const { return _objectId; }

    API_PROPERTY()
    const char* GetTypeName() const { return _typeName; }

    // ── Static helpers ───────────────────────────────────────────────

    API_FUNCTION()
    static Object* FromManaged(void* managedObj);

    API_FUNCTION()
    void SetManagedInstance(void* gcHandle);

    API_FUNCTION()
    void DestroyManaged();

protected:
    void*       _gcHandle = nullptr;
    uint32_t    _objectId = 0;
    const char* _typeName = "Object";

private:
    static uint32_t s_nextId;
};
