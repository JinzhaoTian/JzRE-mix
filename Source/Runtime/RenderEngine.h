#pragma once
#include "API.h"
#include "RenderEngine.Exporter.h"
#include <bgfx/bgfx.h>
#include <string>

/// GPU render engine backed by bgfx. Owns the D3D11 device (via bgfx), shader
/// program, and vertex/index buffers. The class is a singleton — only one
/// instance exists per process.
///
/// The flat C API in RenderEngine.Exporter.h is the P/Invoke boundary; each function
/// delegates to the singleton instance below.
class RenderEngine
{
public:
    static RenderEngine& Get();

    bool Create(void* nativeWindow, int x, int y, int width, int height);
    void Destroy();
    bool IsInitialized() const { return _initialized; }

    bool LoadFile(const char* path);
    void Render();
    void Resize(int x, int y, int width, int height);

    void SetViewAngle(float distance, float pitch, float yaw);
    float GetSuggestedDistance() const { return _distance; }
    const char* GetLastError() const;

private:
    RenderEngine() = default;
    ~RenderEngine();

    void SetError(const char* msg);
    bgfx::ShaderHandle LoadShader(const char* path);
    bool LoadProgram(const char* vsPath, const char* fsPath);

    // ── GPU vertex layout ────────────────────────────────────────────────
    struct Vertex { float x, y, z; float nx, ny, nz; };

    // ── bgfx callback ────────────────────────────────────────────────────
    class BgfxCallback : public bgfx::CallbackI
    {
    public:
        BgfxCallback(RenderEngine* owner) : _owner(owner) {}
        void fatal(const char* filePath, uint16_t line, bgfx::Fatal::Enum code, const char* str) override;
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
    private:
        RenderEngine* _owner;
    };

    // ── State ───────────────────────────────────────────────────────────

    bool        _initialized = false;
    int         _width  = 1, _height = 1;
    std::string _error;

    bgfx::ProgramHandle     _program    = BGFX_INVALID_HANDLE;
    bgfx::UniformHandle     _uMvp       = BGFX_INVALID_HANDLE;
    bgfx::UniformHandle     _uNormalMtx = BGFX_INVALID_HANDLE;
    bgfx::VertexBufferHandle _vb        = BGFX_INVALID_HANDLE;
    bgfx::IndexBufferHandle  _ib        = BGFX_INVALID_HANDLE;
    bgfx::VertexLayout      _vtxLayout;

    BgfxCallback _callback{this};

    // Camera / view
    float _distance  = 5.0f, _pitch = 0.3f, _yaw = 0.0f;
    float _centerX   = 0.0f, _centerY = 0.0f, _centerZ = 0.0f;
    float _extentX   = 0.0f, _extentY = 0.0f, _extentZ = 0.0f;
    uint32_t _vertexCount = 0, _indexCount = 0;

    static constexpr uint64_t kState =
        BGFX_STATE_WRITE_RGB | BGFX_STATE_WRITE_A | BGFX_STATE_WRITE_Z |
        BGFX_STATE_DEPTH_TEST_LESS | BGFX_STATE_MSAA;
};
