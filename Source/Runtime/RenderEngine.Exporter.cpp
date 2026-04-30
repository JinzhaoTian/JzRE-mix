// JzRE-mix - RenderEngine flat C API (P/Invoke boundary).
// Thin wrappers that delegate to RenderEngine::Get().

#include "RenderEngine.h"

bool RenderEngine_Create(void* nativeWindow, int x, int y, int width, int height)
{
    return RenderEngine::Get().Create(nativeWindow, x, y, width, height);
}

void RenderEngine_Destroy()
{
    RenderEngine::Get().Destroy();
}

bool RenderEngine_LoadFile(const char* path)
{
    return RenderEngine::Get().LoadFile(path);
}

void RenderEngine_Render()
{
    RenderEngine::Get().Render();
}

void RenderEngine_Resize(int x, int y, int width, int height)
{
    RenderEngine::Get().Resize(x, y, width, height);
}

void RenderEngine_SetViewAngle(float distance, float pitch, float yaw)
{
    RenderEngine::Get().SetViewAngle(distance, pitch, yaw);
}

float RenderEngine_GetSuggestedDistance()
{
    return RenderEngine::Get().GetSuggestedDistance();
}

const char* RenderEngine_GetLastError()
{
    return RenderEngine::Get().GetLastError();
}
