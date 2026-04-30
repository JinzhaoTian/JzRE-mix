// Runtime exporter — engine-level functions (logging, time).
// Consumed from C# via [LibraryImport("JzRE.Runtime", EntryPoint = "RuntimeInternal_*")].

#include "API.h"
#include <cstdint>
#include <cstdio>

// ── Logging ───────────────────────────────────────────────────────────────────

API_EXPORT() void RuntimeInternal_Log(int level, const char* message)
{
    if (!message) return;
    std::fprintf(stderr, "[C++] %s\n", message);
}

// ── Engine time ───────────────────────────────────────────────────────────────

static float    s_deltaTime  = 0.0f;
static uint64_t s_frameCount = 0;

API_EXPORT() float RuntimeInternal_GetDeltaTime()
{
    return s_deltaTime;
}

API_EXPORT() uint64_t RuntimeInternal_GetFrameCount()
{
    return s_frameCount;
}

API_EXPORT() void RuntimeInternal_BeginFrame(float dt)
{
    s_deltaTime = dt;
    s_frameCount++;
}
