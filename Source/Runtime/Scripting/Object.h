#pragma once
#include <cstdint>

// ── JzObject — root class of the engine object hierarchy ─────────────────────
// Every engine object that has a managed (C#) peer derives from JzObject.
// Mirrors FlaxEngine's ScriptingObject.
//
// Lifecycle:
//   Native-owned:  C++ creates the object; the C# peer is created on demand
//                  via GetOrCreateManagedInstance().  The C++ destructor
//                  frees the GCHandle.
//   Managed-owned: C# creates the object (new Script()); the C++ peer is
//                  created by ObjectInternal_Create and the GCHandle pins
//                  the managed object.

class JzObject
{
public:
    JzObject();
    virtual ~JzObject();

    JzObject(const JzObject&) = delete;
    JzObject& operator=(const JzObject&) = delete;

    // ── Managed peer ─────────────────────────────────────────────────

    /// Returns the GCHandle to the managed peer, or nullptr if none exists.
    void* GetManagedInstance() const { return _gcHandle; }

    /// Returns true if this object has a live managed peer.
    bool HasManagedInstance() const { return _gcHandle != nullptr; }

    /// Called by the managed peer when the C# object is collected or disposed.
    /// The default implementation does nothing; subclasses override to clean up.
    virtual void OnManagedInstanceDeleted();

    // ── Object identity ──────────────────────────────────────────────

    uint32_t GetObjectId() const { return _objectId; }
    const char* GetTypeName() const { return _typeName; }

    // ── Static helpers ───────────────────────────────────────────────

    /// Look up a JzObject by its managed peer pointer.
    /// Returns nullptr if not found.
    static JzObject* FromManaged(void* managedObj);

protected:
    void*       _gcHandle = nullptr;
    uint32_t    _objectId = 0;
    const char* _typeName = "JzObject";

    /// Allocates a GCHandle for the given managed object and stores it.
    /// Called by the internal call layer when a managed peer is created.
    void SetManagedInstance(void* gcHandle);

    /// Frees the GCHandle. Called from ~JzObject or from OnManagedInstanceDeleted.
    void DestroyManaged();

private:
    static uint32_t s_nextId;
};
