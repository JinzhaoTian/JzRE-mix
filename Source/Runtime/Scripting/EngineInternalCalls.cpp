// EngineInternalCalls.cpp — initial set of internal calls exposed to C#.
// These are DEFINE_INTERNAL_CALL functions consumed by the managed side
// via [LibraryImport("JzRE.Runtime")].
//
// Mirrors FlaxEngine's EngineInternalCalls.cpp.

#ifndef JzRE_RUNTIME_EXPORTS
#define JzRE_RUNTIME_EXPORTS
#endif
#include "InternalCalls.h"
#include "Object.h"
#include "MUtils.h"
#include <cstdio>
#include <cstring>

// ── Logging ───────────────────────────────────────────────────────────────────

DEFINE_INTERNAL_CALL(void) RuntimeInternal_Log(int level, const char* message)
{
    if (!message) return;
    // In a full engine this would route through the logging system.
    // For now, stderr ensures it's visible even without a console.
    std::fprintf(stderr, "[C++] %s\n", message);
}

// ── Object lifecycle ──────────────────────────────────────────────────────────

DEFINE_INTERNAL_CALL(void) ObjectInternal_Destroy(void* obj, float /*timeLeft*/)
{
    INTERNAL_CALL_CHECK(obj);
    JzObject* native = JzObject::FromManaged(obj);
    if (native)
    {
        native->OnManagedInstanceDeleted();
        delete native;
    }
}

DEFINE_INTERNAL_CALL(void*) ObjectInternal_FindObject(void* managedPtr)
{
    if (!managedPtr) return nullptr;
    JzObject* native = JzObject::FromManaged(managedPtr);
    return native ? native->GetManagedInstance() : nullptr;
}

DEFINE_INTERNAL_CALL(void) ObjectInternal_ManagedInstanceDeleted(void* obj)
{
    INTERNAL_CALL_CHECK(obj);
    JzObject* native = JzObject::FromManaged(obj);
    if (native)
        native->OnManagedInstanceDeleted();
}

// ── Engine time ───────────────────────────────────────────────────────────────

// Simple frame counter and delta time for the scripting engine.
// In a full engine this belongs to the ScriptingService singleton.
static float s_deltaTime = 0.0f;
static uint64_t s_frameCount = 0;

DEFINE_INTERNAL_CALL(float) RuntimeInternal_GetDeltaTime()
{
    return s_deltaTime;
}

DEFINE_INTERNAL_CALL(uint64_t) RuntimeInternal_GetFrameCount()
{
    return s_frameCount;
}

DEFINE_INTERNAL_CALL(void) RuntimeInternal_BeginFrame(float dt)
{
    s_deltaTime = dt;
    s_frameCount++;
}

// ── Command-line ──────────────────────────────────────────────────────────────

DEFINE_INTERNAL_CALL(void) RuntimeInternal_ParseCommandLine(const char* args)
{
    // Forward to the native CommandLine parser (Phase 1.4)
    if (args && *args)
    {
        // Simple parse: look for --debug and --debugwait
        if (std::strstr(args, "--debug") || std::strstr(args, "-debug"))
        {
            // Debug mode enabled — could set a global flag here
        }
    }
}
