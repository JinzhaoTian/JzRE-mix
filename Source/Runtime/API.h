#pragma once
#include "Platform.h"

// ── DLL export / import ───────────────────────────────────────────────────────
// These exported C functions are the P/Invoke boundary: the "scripting API"
// surface that mirrors FlaxEngine's generated binding exports.

#ifndef JzRE_API
    #define JzRE_API JzRE_EXPORT
#endif

// ── API annotation macros ─────────────────────────────────────────────────────
// These are parse-time markers for the bindings generator (Phase 3) and the
// managed interop bridge.  With the exception of API_EXPORT(), all macros
// expand to nothing — they exist purely for the header parser.
//
// Usage in C++ headers:
//   API_CLASS() class MyObject : public JzObject { … };
//   API_STRUCT() struct MyData { int x, y; };
//   API_ENUM() enum class MyEnum { A, B };
//   API_EXPORT() void SomeExportedFunction();  // standalone C function

// Marker for a class method / standalone function exposed to managed code.
// Inside a class body this is a parse-time annotation only (no expansion).
// For standalone functions, use the separate API_EXPORT() macro.
#define API_FUNCTION(...)

// Exported standalone function with C linkage (P/Invoke boundary).
// Use this for free functions only — NOT for class member functions.
#define API_EXPORT()  extern "C" JzRE_API

// Marker macros — parse-time only
#define API_CLASS(...)                           // class eligible for binding
#define API_STRUCT(...)                          // POD struct eligible for binding
#define API_ENUM(...)                            // enum eligible for binding
#define API_FIELD(...)                           // field exposed to managed code
#define API_PROPERTY(...)                        // getter/setter pair
#define API_INTERFACE(...)                       // abstract interface
#define API_PARAM(refKind)                       // ref/out parameter hint
#define API_EVENT(...)                           // scripting event
#define API_INJECT_CODE(...)                     // raw code injected into bindings
#define API_AUTO_SERIALIZATION()                 // auto-generate serialization
