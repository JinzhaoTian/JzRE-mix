#pragma once
#include "../Scripting/API.h"

// ── Public C API exported from JzRE.Runtime.dll ────────────────────────────
// These functions are the P/Invoke boundary called from C# (NativeRuntime.cs).
// All strings are UTF-8. hwnd is a Win32 HWND cast to void*.
//
// Mirrors FlaxEngine's pattern: C++ exposes a flat C API that BindingsGenerator
// would normally auto-generate; here we write it manually for clarity.

API_FUNCTION() bool        Renderer_Create(void* hwnd, int width, int height);
API_FUNCTION() void        Renderer_Destroy();
API_FUNCTION() bool        Renderer_LoadFile(const char* path);
API_FUNCTION() void        Renderer_Render();
API_FUNCTION() void        Renderer_Resize(int width, int height);
API_FUNCTION() void        Renderer_SetViewAngle(float distance, float pitch, float yaw);
API_FUNCTION() const char* Renderer_GetLastError();
