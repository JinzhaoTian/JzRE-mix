#include "ScriptEngine.h"
#include "JzRE.Runtime.Bindings.Gen.h"
#include <algorithm>
#include <cstdio>
#include <cstdint>

// ── ScriptEngine singleton ──────────────────────────────────────────────────

ScriptEngine& ScriptEngine::Get()
{
    static ScriptEngine instance;
    return instance;
}

// ── Static API (P/Invoke boundary) ──────────────────────────────────────────

void ScriptEngine::Init()
{
    Get().InitializeImpl();
}

void ScriptEngine::Update(float deltaTime)
{
    Get().UpdateImpl(deltaTime);
}

void ScriptEngine::Shutdown()
{
    Get().ShutdownImpl();
}

void ScriptEngine::RegisterInteropCallbacks(void* freeGCHandle_fn, void* log_fn)
{
    Get().RegisterInteropCallbacksImpl(freeGCHandle_fn, log_fn);
}

// ── Instance implementation ─────────────────────────────────────────────────

void ScriptEngine::InitializeImpl()
{
    if (_initialized) return;

    _scripts.clear();
    _deltaTime  = 0.0f;
    _frameCount = 0;
    _initialized = true;

    std::fprintf(stderr, "[ScriptEngine] Initialized.\n");
}

void ScriptEngine::UpdateImpl(float deltaTime)
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

void ScriptEngine::ShutdownImpl()
{
    if (!_initialized) return;

    // Call OnDestroy on all scripts (in reverse registration order)
    for (auto it = _scripts.rbegin(); it != _scripts.rend(); ++it)
    {
        if (*it)
        {
            (*it)->OnDestroy();
            if (_interop.FreeGCHandle && (*it)->HasManagedInstance())
                _interop.FreeGCHandle((*it)->GetManagedInstance());
            delete *it;
        }
    }

    _scripts.clear();
    _initialized = false;

    std::fprintf(stderr, "[ScriptEngine] Shutdown complete (frames: %llu).\n",
                 (unsigned long long)_frameCount);
}

void ScriptEngine::RegisterScript(Script* script)
{
    if (!script) return;

    if (!script->HasManagedInstance())
    {
        void* gcHandle = Script_CreateManagedPeer(script, script->GetObjectId());
        if (gcHandle)
            script->SetManagedInstance(gcHandle);
    }

    _scripts.push_back(script);
    script->OnEnable();
}

void ScriptEngine::UnregisterScript(Script* script)
{
    if (!script) return;
    auto it = std::find(_scripts.begin(), _scripts.end(), script);
    if (it != _scripts.end())
    {
        (*it)->OnDisable();
        _scripts.erase(it);
    }
}

Script* ScriptEngine::FindScript(uint32_t objectId) const
{
    for (auto* s : _scripts)
    {
        if (s && s->GetObjectId() == objectId)
            return s;
    }
    return nullptr;
}
