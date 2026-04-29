#pragma once

/// Minimal command-line parser for the native runtime.
/// Mirrors FlaxEngine's CommandLine.h — stores flags that the native side
/// needs before the managed editor takes over.
namespace CommandLine
{
    /// IP:port for the managed debugger agent. Empty = no debugger.
    extern const char* DebuggerAddress;

    /// When true, the runtime suspends startup until a debugger attaches.
    extern bool WaitForDebugger;

    /// Parse argc/argv. Called early in main() or via the scripting bridge.
    void Parse(int argc, const char** argv);

    /// Convenience: parse from a single space-delimited string (used when
    /// the editor passes flags through P/Invoke).
    void Parse(const char* args);
}
