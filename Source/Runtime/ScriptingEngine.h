#pragma once
#include "Script.h"
#include <vector>

// ── ScriptingEngine — manages the script lifecycle ─────────────────────────
// Mirrors FlaxEngine's ScriptingService.  Owns the list of active Script
// objects and invokes their lifecycle methods each frame.
//
// In a full engine this would also own the CLR host (hostfxr), assembly
// loading, and managed type discovery.  For Phase 4 it provides the core
// Script management that both native and managed scripts plug into.

class ScriptingEngine
{
public:
    // ── Singleton ────────────────────────────────────────────────────

    static ScriptingEngine& Get();

    // ── Lifecycle ────────────────────────────────────────────────────

    /// One-time initialization. Call after the native runtime is ready.
    void Initialize();

    /// One-frame tick. Calls OnUpdate on all enabled scripts.
    /// Must be called from the main thread (same thread as the render loop).
    void Update(float deltaTime);

    /// Shut down. Calls OnDestroy on all scripts and clears the registry.
    void Shutdown();

    // ── Script management ────────────────────────────────────────────

    /// Register a script for lifecycle updates.
    void RegisterScript(Script* script);

    /// Remove a script from lifecycle updates (does NOT delete it).
    void UnregisterScript(Script* script);

    /// Find a script by its object ID. Returns nullptr if not found.
    Script* FindScript(uint32_t objectId) const;

    /// Number of currently registered scripts.
    int ScriptCount() const { return (int)_scripts.size(); }

    // ── Delta time ───────────────────────────────────────────────────

    float GetDeltaTime() const { return _deltaTime; }
    uint64_t GetFrameCount() const { return _frameCount; }

private:
    ScriptingEngine() = default;

    std::vector<Script*> _scripts;
    float _deltaTime = 0.0f;
    uint64_t _frameCount = 0;
    bool _initialized = false;
};

// ── Exported API (called from C# via P/Invoke) ────────────────────────────

/// Initialize the scripting engine. Called once from the editor.
API_EXPORT() void ScriptingEngine_Init();

/// Tick the scripting engine. Called each frame before render.
API_EXPORT() void ScriptingEngine_Update(float deltaTime);

/// Shut down the scripting engine. Called on editor close.
API_EXPORT() void ScriptingEngine_Shutdown();

/// Register managed interop callbacks.
/// Must be called from C# before ScriptingEngine_Init so that managed peers
/// are created for every Script registered with the engine.
///   createManagedObject_fn : void* (const char* typeName, void* nativePtr, uint32_t objectId)
///   freeGCHandle_fn        : void  (void* gcHandle)
///   log_fn                 : void  (int level, const char* message)
API_EXPORT() void ScriptingEngine_RegisterInteropCallbacks(void* createManagedObject_fn, void* freeGCHandle_fn, void* log_fn);
