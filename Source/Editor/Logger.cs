// Logger.cs — managed half of the unified logging bridge.
// The generated partial (in JzRE.Runtime.Bindings.Gen.cs) provides the
// [LibraryImport] stubs for Log() and SetManagedLogCallback().
// This file adds:
//   - ManagedLog: [UnmanagedCallersOnly] sink registered with the native side
//   - RegisterManagedCallback: called once from ScriptEngine.Initialize()
//   - Convenience overloads: Debug / Info / Warn / Error

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JzRE.Scripting;

namespace JzRE;

public static partial class Logger
{
    // ── Managed log sink (C++ → C#) ──────────────────────────────────────────
    // Called by the native Logger::Log() once SetManagedLogCallback is registered.
    // Must use blittable types: string arrives as a raw UTF-8 pointer.

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ManagedLog(int level, IntPtr messagePtr)
    {
        string message = MarshallingUtils.Utf8ToString(messagePtr);
        switch ((LogLevel)level)
        {
            case LogLevel.Debug: Console.WriteLine($"[DEBUG] {message}"); break;
            case LogLevel.Info:  Console.WriteLine($"[INFO]  {message}"); break;
            case LogLevel.Warn:  Console.WriteLine($"[WARN]  {message}"); break;
            case LogLevel.Error: Console.Error.WriteLine($"[ERROR] {message}"); break;
            default:             Console.WriteLine($"[?]     {message}"); break;
        }
    }

    // ── Startup registration ─────────────────────────────────────────────────

    public static unsafe void RegisterManagedCallback()
    {
        SetManagedLogCallback(
            (IntPtr)(delegate* unmanaged[Cdecl]<int, IntPtr, void>)&ManagedLog);
    }

    // ── C# convenience overloads ─────────────────────────────────────────────

    public static void Debug(string message) => Log((int)LogLevel.Debug, message);
    public static void Info (string message) => Log((int)LogLevel.Info,  message);
    public static void Warn (string message) => Log((int)LogLevel.Warn,  message);
    public static void Error(string message) => Log((int)LogLevel.Error, message);
}
