#pragma once
#include "Script.h"
#include <vector>

// ── ScriptEngine — manages the script lifecycle ─────────────────────────────
// Mirrors FlaxEngine's ScriptingService.  Owns the list of active Script
// objects and invokes their lifecycle methods each frame.
//
// API_CLASS(Static) + API_FUNCTION() static methods form the P/Invoke boundary;
// the bindings generator produces the C++ glue and C# partial class automatically.
//
// Logging is handled separately by Logger (see Logger.h); only FreeGCHandle
// needs to be registered here.

API_CLASS(Static)
class ScriptEngine
{
public:
    // ── Singleton ────────────────────────────────────────────────────

    static ScriptEngine& Get();

    // ── P/Invoke API (generated glue calls these) ────────────────────

    API_FUNCTION() static void Init();
    API_FUNCTION() static void Update(float deltaTime);
    API_FUNCTION() static void Shutdown();
    API_FUNCTION() static void RegisterInteropCallbacks(void* freeGCHandle_fn);

    // ── Internal (non-API) ────────────────────────────────────────────

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

    // ── Managed interop callbacks ────────────────────────────────────

    typedef void (*FreeGCHandleFn)(void* gcHandle);

    FreeGCHandleFn GetFreeGCHandle() const { return _interop.FreeGCHandle; }

private:
    ScriptEngine() = default;

    void RegisterInteropCallbacksImpl(void* freeGCHandleFn)
    {
        _interop.FreeGCHandle = reinterpret_cast<FreeGCHandleFn>(freeGCHandleFn);
    }

    struct InteropCallbacks { FreeGCHandleFn FreeGCHandle = nullptr; };
    InteropCallbacks _interop;

    // Instance methods (called by static API wrappers)
    void InitializeImpl();
    void UpdateImpl(float deltaTime);
    void ShutdownImpl();

    std::vector<Script*> _scripts;
    float _deltaTime = 0.0f;
    uint64_t _frameCount = 0;
    bool _initialized = false;
};
