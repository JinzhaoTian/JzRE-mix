#pragma once
#include "API.h"

// Public C API for the render engine — P/Invoke boundary consumed by C# (NativeRuntime.cs).
// All strings are UTF-8. nativeWindow is the platform-native window handle
// (HWND on Windows, X11 Window on Linux, NSView* on macOS) cast to void*.

API_EXPORT() bool        RenderEngine_Create(void* nativeWindow, int x, int y, int width, int height);
API_EXPORT() void        RenderEngine_Destroy();
API_EXPORT() bool        RenderEngine_LoadFile(const char* path);
API_EXPORT() void        RenderEngine_Render();
API_EXPORT() void        RenderEngine_Resize(int x, int y, int width, int height);
API_EXPORT() void        RenderEngine_SetViewAngle(float distance, float pitch, float yaw);
API_EXPORT() float       RenderEngine_GetSuggestedDistance();
API_EXPORT() const char* RenderEngine_GetLastError();
