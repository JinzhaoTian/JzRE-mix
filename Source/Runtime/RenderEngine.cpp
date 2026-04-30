// JzRE.Runtime - bgfx render engine (object-oriented)
// The RenderEngine class owns the GPU device, shader program, and mesh buffers.
// The flat C API at the bottom delegates to RenderEngine::Get() for P/Invoke.

#include "RenderEngine.h"
#include "MeshLoader.h"

#include <bgfx/platform.h>
#include <bx/bx.h>
#include <bx/math.h>

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <cstdio>
#include <vector>

// ── RenderEngine singleton ──────────────────────────────────────────────────

RenderEngine& RenderEngine::Get()
{
    static RenderEngine instance;
    return instance;
}

RenderEngine::~RenderEngine()
{
    Destroy();
}

// ── BgfxCallback ────────────────────────────────────────────────────────────

void RenderEngine::BgfxCallback::fatal(const char* filePath, uint16_t line,
                                        bgfx::Fatal::Enum /*code*/, const char* str)
{
    if (str && _owner->_error.empty())
    {
        char buf[512];
        std::snprintf(buf, sizeof(buf), "%s(%u): %s", filePath, (unsigned)line, str);
        _owner->_error = buf;
    }
}

// ── Helpers ─────────────────────────────────────────────────────────────────

void RenderEngine::SetError(const char* msg)
{
    _error = msg ? msg : "";
}

const char* RenderEngine::GetLastError() const
{
    return _error.empty() ? nullptr : _error.c_str();
}

bgfx::ShaderHandle RenderEngine::LoadShader(const char* path)
{
    FILE* f = std::fopen(path, "rb");
    if (!f) return BGFX_INVALID_HANDLE;

    std::fseek(f, 0, SEEK_END);
    long size = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);

    std::vector<char> buf(size);
    std::fread(buf.data(), 1, size, f);
    std::fclose(f);

    const bgfx::Memory* mem = bgfx::copy(buf.data(), uint32_t(size));
    return bgfx::createShader(mem);
}

bool RenderEngine::LoadProgram(const char* vsPath, const char* fsPath)
{
    bgfx::ShaderHandle vsh = LoadShader(vsPath);
    bgfx::ShaderHandle fsh = LoadShader(fsPath);

    if (!bgfx::isValid(vsh) || !bgfx::isValid(fsh))
    {
        if (bgfx::isValid(vsh)) bgfx::destroy(vsh);
        if (bgfx::isValid(fsh)) bgfx::destroy(fsh);
        SetError("Failed to load shader binary");
        return false;
    }

    _program = bgfx::createProgram(vsh, fsh, true);
    return bgfx::isValid(_program);
}

// ── Public API ─────────────────────────────────────────────────────────────

bool RenderEngine::Create(void* nativeWindow, int x, int y, int width, int height)
{
    if (_initialized) return true;

    if (!nativeWindow)      { SetError("Null native window handle"); return false; }
    if (width <= 0 || height <= 0) { SetError("Invalid render size"); return false; }

    _width = width;
    _height = height;

    bgfx::Init init;
    init.type               = bgfx::RendererType::Direct3D11;
    init.platformData.nwh   = nativeWindow;
    init.resolution.width   = uint32_t(width);
    init.resolution.height  = uint32_t(height);
    init.resolution.reset   = BGFX_RESET_VSYNC;
    init.callback           = &_callback;

    _error.clear();
    if (!bgfx::init(init))
    {
        if (_error.empty()) SetError("bgfx::init failed");
        return false;
    }

    bgfx::setViewClear(0, BGFX_CLEAR_COLOR | BGFX_CLEAR_DEPTH, 0x1a1e26ff, 1.0f, 0);

    _vtxLayout.begin()
        .add(bgfx::Attrib::Position, 3, bgfx::AttribType::Float)
        .add(bgfx::Attrib::Normal,   3, bgfx::AttribType::Float)
        .end();

    if (!LoadProgram("Shaders/vs_basic.bin", "Shaders/fs_basic.bin"))
    {
        bgfx::shutdown();
        return false;
    }

    _uMvp       = bgfx::createUniform("u_mvp",       bgfx::UniformType::Mat4);
    _uNormalMtx = bgfx::createUniform("u_normalMtx", bgfx::UniformType::Mat4);

    _initialized = true;
    return true;
}

void RenderEngine::Destroy()
{
    if (!_initialized) return;

    if (bgfx::isValid(_program))    bgfx::destroy(_program);
    if (bgfx::isValid(_uMvp))       bgfx::destroy(_uMvp);
    if (bgfx::isValid(_uNormalMtx)) bgfx::destroy(_uNormalMtx);
    if (bgfx::isValid(_vb))         bgfx::destroy(_vb);
    if (bgfx::isValid(_ib))         bgfx::destroy(_ib);

    bgfx::shutdown();
    _initialized = false;
}

bool RenderEngine::LoadFile(const char* path)
{
    if (!_initialized) { SetError("Render engine not initialized"); return false; }

    std::vector<MeshVertex> mv;
    std::vector<uint32_t>   mi;
    if (!LoadOBJ(path, mv, mi)) { SetError("Failed to parse OBJ file"); return false; }

    if (bgfx::isValid(_vb)) bgfx::destroy(_vb);
    if (bgfx::isValid(_ib)) bgfx::destroy(_ib);

    std::vector<Vertex> gpuVerts(mi.size());
    float minX = mv[0].x, minY = mv[0].y, minZ = mv[0].z;
    float maxX = mv[0].x, maxY = mv[0].y, maxZ = mv[0].z;

    for (size_t i = 1; i < mv.size(); i++)
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

    const bgfx::Memory* vbMem = bgfx::copy(gpuVerts.data(),
        uint32_t(gpuVerts.size() * sizeof(Vertex)));
    _vb = bgfx::createVertexBuffer(vbMem, _vtxLayout);
    _ib = BGFX_INVALID_HANDLE;

    if (!bgfx::isValid(_vb))
    {
        if (bgfx::isValid(_vb)) { bgfx::destroy(_vb); _vb = BGFX_INVALID_HANDLE; }
        SetError("Failed to create GPU buffers");
        return false;
    }

    _centerX = (minX + maxX) * 0.5f;
    _centerY = (minY + maxY) * 0.5f;
    _centerZ = (minZ + maxZ) * 0.5f;
    _extentX = (maxX - minX) * 0.5f;
    _extentY = (maxY - minY) * 0.5f;
    _extentZ = (maxZ - minZ) * 0.5f;

    const float radius = bx::sqrt(_extentX * _extentX + _extentY * _extentY + _extentZ * _extentZ);
    const float fovY   = 60.0f * bx::kPi / 180.0f;
    _distance = bx::max(0.1f, radius / bx::sin(fovY * 0.5f) + radius * 0.5f);

    _vertexCount = uint32_t(gpuVerts.size());
    _indexCount  = 0;
    _error.clear();
    return true;
}

void RenderEngine::Render()
{
    if (!_initialized) return;

    float aspect = _width / (float)_height;

    float ortho[16];
    bx::mtxIdentity(ortho);
    bgfx::setViewTransform(0, nullptr, ortho);
    bgfx::setViewRect(0, 0, 0, uint16_t(_width), uint16_t(_height));
    bgfx::touch(0);

    float model[16];
    bx::mtxTranslate(model, -_centerX, -_centerY, -_centerZ);

    const float clipScale = bx::max(0.01f, 1.0f / bx::max(_distance, 0.01f));

    float rotation[16], scaleMtx[16], rotatedModel[16], modelViewProj[16];
    bx::mtxRotateXY(rotation, _pitch, _yaw);
    bx::mtxScale(scaleMtx, clipScale / aspect, clipScale, clipScale);
    bx::mtxMul(rotatedModel, rotation, model);
    bx::mtxMul(modelViewProj, scaleMtx, rotatedModel);

    if (!bgfx::isValid(_vb) || !bgfx::isValid(_program))
    {
        bgfx::frame();
        return;
    }

    bgfx::setUniform(_uMvp, modelViewProj);
    bgfx::setUniform(_uNormalMtx, rotation);
    bgfx::setVertexBuffer(0, _vb);
    if (bgfx::isValid(_ib))
        bgfx::setIndexBuffer(_ib);
    bgfx::setState(kState);
    bgfx::submit(0, _program);

    bgfx::frame();
}

void RenderEngine::Resize(int x, int y, int width, int height)
{
    if (!_initialized || width <= 0 || height <= 0) return;
    _width  = width;
    _height = height;
    bgfx::reset(uint32_t(width), uint32_t(height), BGFX_RESET_VSYNC);
}

void RenderEngine::SetViewAngle(float distance, float pitch, float yaw)
{
    _distance = distance;
    _pitch    = pitch;
    _yaw      = yaw;
}
