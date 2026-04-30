#pragma once
#include "Platform.h"

// ── Internal call macros ──────────────────────────────────────────────────────
// JzRE-mix supports only .NET CoreCLR (hostfxr), not Mono.  Internal calls are
// plain extern "C" DLLEXPORT functions — no registration table needed.
//
// On the C# side these are consumed via [LibraryImport("JzRE.Runtime")].
//
// Mirrors FlaxEngine's InternalCalls.h, simplified for CoreCLR-only.

#define DEFINE_INTERNAL_CALL(returnType) extern "C" JzRE_EXPORT returnType

/// Null-check guard for managed→native object pointer parameters.
/// Every internal call that receives a managed object pointer should
/// validate it before dereferencing.
#define INTERNAL_CALL_CHECK(obj) \
    if ((obj) == nullptr) return

/// Null-check guard with a default return value.
#define INTERNAL_CALL_CHECK_RETURN(obj, defaultValue) \
    if ((obj) == nullptr) return (defaultValue)
