// JzRE.Runtime - bgfx renderer
// Exposes a flat C API consumed by the C# editor via P/Invoke (NativeRuntime.cs).
// Architecture: this library owns the GPU device; the C# side provides the
// native window handle (HWND / X11 Window / NSView*) via void*.

#include "Renderer.h"
#include "MeshLoader.h"

#include <bgfx/bgfx.h>
#include <bgfx/platform.h>
#include <bx/bx.h>
#include <bx/math.h>

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <cstdarg>
#include <cstdio>
#include <string>
#include <vector>

// ── Vertex layout (matches bgfx shader $input a_position, a_normal) ──────────

struct PosNormalVertex
{
    float x, y, z;
    float nx, ny, nz;
};

static bgfx::VertexLayout g_layout;

static void InitLayout()
{
    g_layout.begin()
        .add(bgfx::Attrib::Position, 3, bgfx::AttribType::Float)
        .add(bgfx::Attrib::Normal,   3, bgfx::AttribType::Float)
        .end();
}

// ── Renderer state ───────────────────────────────────────────────────────────

static bgfx::ProgramHandle     g_program    = BGFX_INVALID_HANDLE;
static bgfx::UniformHandle     g_u_mvp      = BGFX_INVALID_HANDLE;
static bgfx::UniformHandle     g_u_normalMtx = BGFX_INVALID_HANDLE;
static bgfx::VertexBufferHandle g_vb        = BGFX_INVALID_HANDLE;
static bgfx::IndexBufferHandle  g_ib        = BGFX_INVALID_HANDLE;
static constexpr uint64_t kRenderState =
      BGFX_STATE_WRITE_RGB
    | BGFX_STATE_WRITE_A
    | BGFX_STATE_WRITE_Z
    | BGFX_STATE_DEPTH_TEST_LESS
    | BGFX_STATE_MSAA;

static int   g_width = 1, g_height = 1;
static float g_distance = 5.f, g_pitch = 0.3f, g_yaw = 0.f;
static float g_centerX = 0.f, g_centerY = 0.f, g_centerZ = 0.f;
static float g_extentX = 0.f, g_extentY = 0.f, g_extentZ = 0.f;
static uint32_t g_vertexCount = 0, g_indexCount = 0;
static bool  g_initialized = false;
static std::string g_err;

static void SetErr(const char* msg) { g_err = msg ? msg : ""; }

// bgfx callback — captures detailed init error messages
struct ErrorCallback : public bgfx::CallbackI
{
    void fatal(const char* _filePath, uint16_t _line, bgfx::Fatal::Enum /*_code*/, const char* _str) override
    {
        if (_str && g_err.empty())
        {
            char buf[512];
            snprintf(buf, sizeof(buf), "%s(%u): %s", _filePath, (unsigned)_line, _str);
            g_err = buf;
        }
    }
    void traceVargs(const char*, uint16_t, const char*, va_list) override {}
    void profilerBegin(const char*, uint32_t, const char*, uint16_t) override {}
    void profilerBeginLiteral(const char*, uint32_t, const char*, uint16_t) override {}
    void profilerEnd() override {}
    uint32_t cacheReadSize(uint64_t) override { return 0; }
    bool cacheRead(uint64_t, void*, uint32_t) override { return false; }
    void cacheWrite(uint64_t, const void*, uint32_t) override {}
    void screenShot(const char*, uint32_t, uint32_t, uint32_t, bgfx::TextureFormat::Enum, const void*, uint32_t, bool) override {}
    void captureBegin(uint32_t, uint32_t, uint32_t, bgfx::TextureFormat::Enum, bool) override {}
    void captureEnd() override {}
    void captureFrame(const void*, uint32_t) override {}
};
static ErrorCallback g_callback;

// ── Shader loading ───────────────────────────────────────────────────────────

static bgfx::ShaderHandle LoadShader(const char* path)
{
    FILE* f = fopen(path, "rb");
    if (!f) return BGFX_INVALID_HANDLE;

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);

    std::vector<char> buf(size);
    fread(buf.data(), 1, size, f);
    fclose(f);

    const bgfx::Memory* mem = bgfx::copy(buf.data(), uint32_t(size));
    return bgfx::createShader(mem);
}

static bgfx::ProgramHandle LoadProgram(const char* vsPath, const char* fsPath)
{
    bgfx::ShaderHandle vsh = LoadShader(vsPath);
    bgfx::ShaderHandle fsh = LoadShader(fsPath);

    if (!bgfx::isValid(vsh) || !bgfx::isValid(fsh))
    {
        if (bgfx::isValid(vsh)) bgfx::destroy(vsh);
        if (bgfx::isValid(fsh)) bgfx::destroy(fsh);
        SetErr("Failed to load shader binary");
        return BGFX_INVALID_HANDLE;
    }
    return bgfx::createProgram(vsh, fsh, true);
}

// ── Public API implementation ────────────────────────────────────────────────

bool Renderer_Create(void* nativeWindow, int x, int y, int width, int height)
{
    if (g_initialized)
        return true;

    if (nullptr == nativeWindow)
    {
        SetErr("Renderer_Create received a null native window handle");
        return false;
    }

    if (width <= 0 || height <= 0)
    {
        SetErr("Renderer_Create received an invalid render size");
        return false;
    }

    g_width = width; g_height = height;

    bgfx::Init init;
    init.type     = bgfx::RendererType::Direct3D11;
    init.platformData.nwh = nativeWindow;
    init.resolution.width  = uint32_t(width);
    init.resolution.height = uint32_t(height);
    init.resolution.reset  = BGFX_RESET_VSYNC;
    init.callback = &g_callback;

    g_err.clear();
    if (!bgfx::init(init))
    {
        if (g_err.empty()) SetErr("bgfx::init failed");
        return false;
    }

    bgfx::setViewClear(0, BGFX_CLEAR_COLOR | BGFX_CLEAR_DEPTH, 0x1a1e26ff, 1.0f, 0);

    InitLayout();

    // Load pre-compiled shaders (shaderc produces platform-specific .bin files)
    g_program = LoadProgram("Shaders/vs_basic.bin", "Shaders/fs_basic.bin");
    if (!bgfx::isValid(g_program))
    {
        bgfx::shutdown();
        return false;
    }

    g_u_mvp = bgfx::createUniform("u_mvp", bgfx::UniformType::Mat4);
    g_u_normalMtx = bgfx::createUniform("u_normalMtx", bgfx::UniformType::Mat4);

    g_initialized = true;
    return true;
}

void Renderer_Destroy()
{
    if (!g_initialized) return;

    if (bgfx::isValid(g_program))    bgfx::destroy(g_program);
    if (bgfx::isValid(g_u_mvp))      bgfx::destroy(g_u_mvp);
    if (bgfx::isValid(g_u_normalMtx)) bgfx::destroy(g_u_normalMtx);
    if (bgfx::isValid(g_vb))         bgfx::destroy(g_vb);
    if (bgfx::isValid(g_ib))         bgfx::destroy(g_ib);

    bgfx::shutdown();
    g_initialized = false;
}

bool Renderer_LoadFile(const char* path)
{
    if (!g_initialized)
    {
        if (g_err.empty())
            SetErr("Renderer is not initialized");
        return false;
    }

    std::vector<MeshVertex> mv;
    std::vector<uint32_t>   mi;
    if (!LoadOBJ(path, mv, mi)) { SetErr("Failed to parse OBJ file"); return false; }

    // Destroy old buffers
    if (bgfx::isValid(g_vb)) bgfx::destroy(g_vb);
    if (bgfx::isValid(g_ib)) bgfx::destroy(g_ib);

    // Flatten the indexed mesh to a triangle list. This keeps the viewer path
    // simple and avoids backend-specific surprises around index submission.
    std::vector<PosNormalVertex> gpuVerts(mi.size());
    float minX = mv[0].x, minY = mv[0].y, minZ = mv[0].z;
    float maxX = mv[0].x, maxY = mv[0].y, maxZ = mv[0].z;
    for (size_t i = 0; i < mv.size(); i++)
    {
        if (mv[i].x < minX) minX = mv[i].x;
        if (mv[i].y < minY) minY = mv[i].y;
        if (mv[i].z < minZ) minZ = mv[i].z;
        if (mv[i].x > maxX) maxX = mv[i].x;
        if (mv[i].y > maxY) maxY = mv[i].y;
        if (mv[i].z > maxZ) maxZ = mv[i].z;
    }

    for (size_t i = 0; i < mi.size(); ++i)
    {
        const MeshVertex& src = mv[mi[i]];
        gpuVerts[i].x  = src.x;  gpuVerts[i].y  = src.y;  gpuVerts[i].z  = src.z;
        gpuVerts[i].nx = src.nx; gpuVerts[i].ny = src.ny; gpuVerts[i].nz = src.nz;
    }

    const bgfx::Memory* vbMem = bgfx::copy(gpuVerts.data(), uint32_t(gpuVerts.size() * sizeof(PosNormalVertex)));
    g_vb = bgfx::createVertexBuffer(vbMem, g_layout);
    g_ib = BGFX_INVALID_HANDLE;

    if (!bgfx::isValid(g_vb))
    {
        if (bgfx::isValid(g_vb)) { bgfx::destroy(g_vb); g_vb = BGFX_INVALID_HANDLE; }
        SetErr("Failed to create GPU buffers for the model");
        return false;
    }

    g_centerX = (minX + maxX) * 0.5f;
    g_centerY = (minY + maxY) * 0.5f;
    g_centerZ = (minZ + maxZ) * 0.5f;

    g_extentX = (maxX - minX) * 0.5f;
    g_extentY = (maxY - minY) * 0.5f;
    g_extentZ = (maxZ - minZ) * 0.5f;
    const float radius = bx::sqrt(g_extentX * g_extentX + g_extentY * g_extentY + g_extentZ * g_extentZ);
    const float fovY = 60.0f * bx::kPi / 180.0f;
    g_distance = bx::max(0.1f, radius / bx::sin(fovY * 0.5f) + radius * 0.5f);
    g_vertexCount = uint32_t(gpuVerts.size());
    g_indexCount = 0;
    g_err.clear();
    return true;
}

void Renderer_Render()
{
    if (!g_initialized) return;

    float aspect = g_width / (float)g_height;
    float ortho[16];
    bx::mtxIdentity(ortho);
    bgfx::setViewTransform(0, nullptr, ortho);
    bgfx::setViewRect(0, 0, 0, uint16_t(g_width), uint16_t(g_height));
    bgfx::touch(0);

    float model[16];
    bx::mtxTranslate(model, -g_centerX, -g_centerY, -g_centerZ);

    float rotation[16];
    float scaleMtx[16];
    float rotatedModel[16];
    float modelViewProj[16];
    const float clipScale = bx::max(0.01f, 1.0f / bx::max(g_distance, 0.01f));
    bx::mtxRotateXY(rotation, g_pitch, g_yaw);
    bx::mtxScale(scaleMtx, clipScale / aspect, clipScale, clipScale);
    bx::mtxMul(rotatedModel, rotation, model);
    bx::mtxMul(modelViewProj, scaleMtx, rotatedModel);

    if (!bgfx::isValid(g_vb) || !bgfx::isValid(g_program))
    {
        bgfx::frame();
        return;
    }

    // Draw
    bgfx::setUniform(g_u_mvp, modelViewProj);
    bgfx::setUniform(g_u_normalMtx, rotation);
    bgfx::setVertexBuffer(0, g_vb);
    if (bgfx::isValid(g_ib))
        bgfx::setIndexBuffer(g_ib);
    bgfx::setState(kRenderState);
    bgfx::submit(0, g_program);

    bgfx::frame();
}

void Renderer_Resize(int x, int y, int width, int height)
{
    if (!g_initialized || width <= 0 || height <= 0) return;
    g_width = width; g_height = height;
    bgfx::reset(uint32_t(width), uint32_t(height), BGFX_RESET_VSYNC);
}

void Renderer_SetViewAngle(float distance, float pitch, float yaw)
{
    g_distance = distance;
    g_pitch    = pitch;
    g_yaw      = yaw;
}

float Renderer_GetSuggestedDistance()
{
    return g_distance;
}

const char* Renderer_GetLastError()
{
    return g_err.c_str();
}
