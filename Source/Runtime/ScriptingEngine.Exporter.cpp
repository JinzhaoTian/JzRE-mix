// JzRE-mix - ScriptingEngine flat C API (P/Invoke boundary).
// Thin wrappers that delegate to ScriptingEngine::Get().

#include "ScriptingEngine.h"

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

void ScriptingEngine_RegisterInteropCallbacks(void* freeGCHandle_fn, void* log_fn)
{
    ScriptingEngine::Get().RegisterInteropCallbacks(freeGCHandle_fn, log_fn);
}
