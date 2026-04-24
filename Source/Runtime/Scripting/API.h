#pragma once

// ── DLL export/import macros ────────────────────────────────────────────────
// When compiling JzRE.Runtime.dll define JZRE_RUNTIME_EXPORTS.
// The C# editor (and any other consumer) imports without that define.
//
// These exported C functions are the P/Invoke boundary: the "scripting API"
// surface that mirrors FlaxEngine's generated binding exports.

#ifdef JZRE_RUNTIME_EXPORTS
    #define JzRE_API __declspec(dllexport)
#else
    #define JzRE_API __declspec(dllimport)
#endif

// Marks a function as part of the public scripting API (C linkage, no mangling).
// Equivalent to FlaxEngine's API_FUNCTION() annotation on exported bindings.
#define API_FUNCTION() extern "C" JzRE_API
