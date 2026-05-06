#include "Logger.h"
#include <cstdio>

// ── Managed callback storage ──────────────────────────────────────────────────

typedef void (*ManagedLogFn)(int level, const char* message);
static ManagedLogFn s_ManagedLog = nullptr;

// ── Helpers ───────────────────────────────────────────────────────────────────

static const char* LevelPrefix(int level)
{
    switch (level)
    {
        case 0: return "[DEBUG]";
        case 1: return "[INFO] ";
        case 2: return "[WARN] ";
        case 3: return "[ERROR]";
        default: return "[?]   ";
    }
}

// ── Logger static methods ─────────────────────────────────────────────────────

void Logger::Log(int level, const char* message)
{
    if (!message) return;
    if (s_ManagedLog)
    {
        s_ManagedLog(level, message);
    }
    else
    {
        std::FILE* out = (level >= 3) ? stderr : stdout;
        std::fprintf(out, "%s %s\n", LevelPrefix(level), message);
    }
}

void Logger::SetManagedLogCallback(void* fn)
{
    s_ManagedLog = reinterpret_cast<ManagedLogFn>(fn);
}
