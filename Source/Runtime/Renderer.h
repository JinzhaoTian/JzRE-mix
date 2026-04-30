#pragma once
#include "API.h"

// ── Public C API exported from JzRE.Runtime ──────────────────────────────────
// These functions are the P/Invoke boundary called from C# (NativeRuntime.cs).
// All strings are UTF-8. nativeWindow is the platform-native window handle
// (HWND on Windows, X11 Window on Linux, NSView* on macOS) cast to void*.
//
// Mirrors FlaxEngine's pattern: C++ exposes a flat C API that BindingsGenerator
// would normally auto-generate; here we write it manually for clarity.

API_EXPORT() bool        Renderer_Create(void* nativeWindow, int x, int y, int width, int height);
API_EXPORT() void        Renderer_Destroy();
API_EXPORT() bool        Renderer_LoadFile(const char* path);
API_EXPORT() void        Renderer_Render();
API_EXPORT() void        Renderer_Resize(int x, int y, int width, int height);
API_EXPORT() void        Renderer_SetViewAngle(float distance, float pitch, float yaw);
API_EXPORT() float       Renderer_GetSuggestedDistance();
API_EXPORT() const char* Renderer_GetLastError();
