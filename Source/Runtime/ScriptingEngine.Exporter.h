#pragma once
#include "API.h"

// Public C API for the scripting engine — P/Invoke boundary consumed by C#.
// freeGCHandle_fn : void (void* gcHandle)
// log_fn          : void (int level, const char* message)

API_EXPORT() void ScriptingEngine_Init();
API_EXPORT() void ScriptingEngine_Update(float deltaTime);
API_EXPORT() void ScriptingEngine_Shutdown();
API_EXPORT() void ScriptingEngine_RegisterInteropCallbacks(void* freeGCHandle_fn, void* log_fn);
