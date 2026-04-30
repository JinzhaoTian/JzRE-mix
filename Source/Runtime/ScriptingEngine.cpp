#ifndef JzRE_RUNTIME_EXPORTS
#define JzRE_RUNTIME_EXPORTS
#endif
#include "ScriptingEngine.h"
#include <algorithm>
#include <cstdio>
#include <cstdint>

// ── NativeInterop callback types ──────────────────────────────────────────────
typedef void* (*NI_CreateManagedObject_Fn)(const char* typeName, void* nativePtr, uint32_t objectId);
typedef void  (*NI_FreeGCHandle_Fn)(void* gcHandle);
typedef void  (*NI_Log_Fn)(int level, const char* message);

struct NativeInteropCallbacks
{
    NI_CreateManagedObject_Fn CreateManagedObject = nullptr;
    NI_FreeGCHandle_Fn        FreeGCHandle        = nullptr;
    NI_Log_Fn                 Log                 = nullptr;
};

static NativeInteropCallbacks s_interop;

// ── Script implementation ────────────────────────────────────────────────────

Script::Script()
{
    _typeName = "Script";
}

void Script::OnEnable()  {}
void Script::OnDisable() {}
void Script::OnUpdate(float /*deltaTime*/) {}
void Script::OnDestroy() {}

// ── ScriptingEngine implementation ───────────────────────────────────────────

ScriptingEngine& ScriptingEngine::Get()
{
    static ScriptingEngine instance;
    return instance;
}

void ScriptingEngine::Initialize()
{
    if (_initialized) return;

    _scripts.clear();
    _deltaTime  = 0.0f;
    _frameCount = 0;
    _initialized = true;

    std::fprintf(stderr, "[ScriptingEngine] Initialized.\n");
}

void ScriptingEngine::Update(float deltaTime)
{
    if (!_initialized) return;

    _deltaTime = deltaTime;
    _frameCount++;

    // Iterate over a copy — scripts may unregister themselves during OnUpdate
    auto scripts = _scripts;
    for (auto* script : scripts)
    {
        if (script && script->GetEnabled())
        {
            script->OnUpdate(deltaTime);
        }
    }
}

void ScriptingEngine::Shutdown()
{
    if (!_initialized) return;

    // Call OnDestroy on all scripts (in reverse registration order)
    for (auto it = _scripts.rbegin(); it != _scripts.rend(); ++it)
    {
        if (*it)
        {
            (*it)->OnDestroy();
            if (s_interop.FreeGCHandle && (*it)->HasManagedInstance())
                s_interop.FreeGCHandle((*it)->GetManagedInstance());
            delete *it;
        }
    }

    _scripts.clear();
    _initialized = false;

    std::fprintf(stderr, "[ScriptingEngine] Shutdown complete (frames: %llu).\n",
                 (unsigned long long)_frameCount);
}

void ScriptingEngine::RegisterScript(Script* script)
{
    if (!script) return;

    if (s_interop.CreateManagedObject && !script->HasManagedInstance())
    {
        void* gcHandle = s_interop.CreateManagedObject(
            script->GetTypeName(), script, script->GetObjectId());
        if (gcHandle)
            script->SetManagedInstance(gcHandle);
    }

    _scripts.push_back(script);
    script->OnEnable();
}

void ScriptingEngine::UnregisterScript(Script* script)
{
    if (!script) return;
    auto it = std::find(_scripts.begin(), _scripts.end(), script);
    if (it != _scripts.end())
    {
        (*it)->OnDisable();
        _scripts.erase(it);
    }
}

Script* ScriptingEngine::FindScript(uint32_t objectId) const
{
    for (auto* s : _scripts)
    {
        if (s && s->GetObjectId() == objectId)
            return s;
    }
    return nullptr;
}

// ── Exported C API ───────────────────────────────────────────────────────────

void ScriptingEngine_Init()
{
    ScriptingEngine::Get().Initialize();
}

void ScriptingEngine_Update(float deltaTime)
{
    ScriptingEngine::Get().Update(deltaTime);
}

void ScriptingEngine_Shutdown()
{
    ScriptingEngine::Get().Shutdown();
}

void ScriptingEngine_RegisterInteropCallbacks(void* createManagedObject_fn, void* freeGCHandle_fn, void* log_fn)
{
    s_interop.CreateManagedObject = reinterpret_cast<NI_CreateManagedObject_Fn>(createManagedObject_fn);
    s_interop.FreeGCHandle        = reinterpret_cast<NI_FreeGCHandle_Fn>(freeGCHandle_fn);
    s_interop.Log                 = reinterpret_cast<NI_Log_Fn>(log_fn);
}
