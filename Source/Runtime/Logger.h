#pragma once
#include "API.h"

// ── Log levels ─────────────────────────────────────────────────────────────────

API_ENUM()
enum class LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 };

// ── Logger ─────────────────────────────────────────────────────────────────────
// Unified log bridge.  C++ code calls the static helpers (Debug/Info/Warn/Error);
// on the managed side the generated bindings expose Log() and SetManagedLogCallback().
//
// Startup sequence (managed):
//   1. Logger.RegisterManagedCallback()   ← registers ManagedLog [UnmanagedCallersOnly]
//   2. All subsequent Logger::Log() calls are routed to the managed sink.

API_CLASS(Static)
class Logger
{
public:
    // ── P/Invoke API (generated glue calls these) ────────────────────

    // Log a message at the given level.  If a managed callback is registered,
    // the call is forwarded to C#; otherwise it falls back to stderr.
    API_FUNCTION() static void Log(int level, const char* message);

    // Register the managed log sink (called once from C# at startup).
    API_FUNCTION() static void SetManagedLogCallback(void* fn);

    // ── C++-only helpers (not exported) ─────────────────────────────

    static void Debug(const char* message) { Log(0, message); }
    static void Info (const char* message) { Log(1, message); }
    static void Warn (const char* message) { Log(2, message); }
    static void Error(const char* message) { Log(3, message); }
};
