#include "ScriptingEngine.h"
#include "JzRE.Runtime.Bindings.Gen.h"
#include <algorithm>
#include <cstdio>
#include <cstdint>

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
            if (_interop.FreeGCHandle && (*it)->HasManagedInstance())
                _interop.FreeGCHandle((*it)->GetManagedInstance());
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

    if (!script->HasManagedInstance())
    {
        void* gcHandle = Script_CreateManagedPeer(script, script->GetObjectId());
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
