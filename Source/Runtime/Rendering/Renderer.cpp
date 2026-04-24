// JzRE.Runtime - Direct3D 11 renderer
// Exposes a flat C API consumed by the C# editor via P/Invoke (NativeRuntime.cs).
// Architecture: this DLL owns the GPU device and swap chain; the C# side
// provides only the Win32 HWND to render into.

// Build.bat / vcxproj pass /DJZRE_RUNTIME_EXPORTS on the command line.
// The #ifndef guard avoids C4005 "macro redefinition" when that is the case,
// while still letting IDEs (clangd / IntelliSense) see the correct dllexport
// declarations when they process this file without the command-line flag.
#ifndef JZRE_RUNTIME_EXPORTS
#define JZRE_RUNTIME_EXPORTS
#endif
#include "Renderer.h"
#include "MeshLoader.h"

// NOMINMAX is passed via compiler /D flag (Build.bat and vcxproj preprocessor definitions)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <d3dcompiler.h>
#include <DirectXMath.h>
#include <wrl/client.h>
#include <string>
#include <vector>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

using namespace DirectX;
using Microsoft::WRL::ComPtr;

// ── Inline HLSL shaders ─────────────────────────────────────────────────────

static const char* kShaderSrc = R"(
cbuffer CB : register(b0)
{
    float4x4 mvp;
    float4x4 model;
    float3   lightDir;
    float    _pad;
};

struct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; };
struct VSOut { float4 pos : SV_POSITION; float3 nrm : NORMAL0; float3 wpos : TEXCOORD0; };

VSOut VS(VSIn i)
{
    VSOut o;
    o.pos  = mul(float4(i.pos, 1.0f), mvp);
    o.nrm  = mul(float4(i.nrm, 0.0f), model).xyz;
    o.wpos = mul(float4(i.pos, 1.0f), model).xyz;
    return o;
}

float4 PS(VSOut i) : SV_TARGET
{
    float3 N      = normalize(i.nrm);
    float3 L      = normalize(-lightDir);
    float3 albedo = float3(0.82f, 0.72f, 0.60f);
    float  diff   = max(dot(N, L), 0.0f);
    float3 color  = albedo * (0.15f + diff * 0.85f);
    return float4(color, 1.0f);
}
)";

// ── GPU types ───────────────────────────────────────────────────────────────

struct GPUVertex { XMFLOAT3 pos; XMFLOAT3 normal; };

struct alignas(16) ConstantBuffer
{
    XMFLOAT4X4 mvp;
    XMFLOAT4X4 model;
    XMFLOAT3   lightDir;
    float      _pad;
};

// ── Renderer state ───────────────────────────────────────────────────────────

struct RendererState
{
    ComPtr<ID3D11Device>           device;
    ComPtr<ID3D11DeviceContext>    ctx;
    ComPtr<IDXGISwapChain>         sc;
    ComPtr<ID3D11RenderTargetView> rtv;
    ComPtr<ID3D11DepthStencilView> dsv;
    ComPtr<ID3D11VertexShader>     vs;
    ComPtr<ID3D11PixelShader>      ps;
    ComPtr<ID3D11InputLayout>      layout;
    ComPtr<ID3D11Buffer>           vb, ib, cb;
    UINT  indexCount = 0;
    int   width = 1, height = 1;
    float distance = 5.f, pitch = 0.3f, yaw = 0.f;
};

static RendererState* g_r   = nullptr;
static std::string    g_err;

static void SetErr(const char* msg) { g_err = msg ? msg : ""; }

// ── Internal helpers ─────────────────────────────────────────────────────────

static bool CreateRTV(RendererState& r)
{
    ComPtr<ID3D11Texture2D> buf;
    if (FAILED(r.sc->GetBuffer(0, __uuidof(ID3D11Texture2D), &buf))) return false;
    return SUCCEEDED(r.device->CreateRenderTargetView(buf.Get(), nullptr, &r.rtv));
}

static bool CreateDSV(RendererState& r, int w, int h)
{
    D3D11_TEXTURE2D_DESC dd{};
    dd.Width              = (UINT)w;
    dd.Height             = (UINT)h;
    dd.MipLevels          = 1;
    dd.ArraySize          = 1;
    dd.Format             = DXGI_FORMAT_D24_UNORM_S8_UINT;
    dd.SampleDesc.Count   = 1;
    dd.BindFlags          = D3D11_BIND_DEPTH_STENCIL;
    ComPtr<ID3D11Texture2D> dt;
    if (FAILED(r.device->CreateTexture2D(&dd, nullptr, &dt))) return false;
    return SUCCEEDED(r.device->CreateDepthStencilView(dt.Get(), nullptr, &r.dsv));
}

static bool CompileShaders(RendererState& r)
{
    ComPtr<ID3DBlob> vsBlob, psBlob, errBlob;
    UINT flags = D3DCOMPILE_ENABLE_STRICTNESS;

    if (FAILED(D3DCompile(kShaderSrc, strlen(kShaderSrc), nullptr, nullptr, nullptr,
                          "VS", "vs_5_0", flags, 0, &vsBlob, &errBlob)))
    {
        SetErr(errBlob ? (char*)errBlob->GetBufferPointer() : "VS compile failed");
        return false;
    }
    if (FAILED(D3DCompile(kShaderSrc, strlen(kShaderSrc), nullptr, nullptr, nullptr,
                          "PS", "ps_5_0", flags, 0, &psBlob, &errBlob)))
    {
        SetErr(errBlob ? (char*)errBlob->GetBufferPointer() : "PS compile failed");
        return false;
    }

    r.device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), nullptr, &r.vs);
    r.device->CreatePixelShader( psBlob->GetBufferPointer(), psBlob->GetBufferSize(), nullptr, &r.ps);

    D3D11_INPUT_ELEMENT_DESC elems[] =
    {
        { "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0,                            D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "NORMAL",   0, DXGI_FORMAT_R32G32B32_FLOAT, 0, D3D11_APPEND_ALIGNED_ELEMENT, D3D11_INPUT_PER_VERTEX_DATA, 0 },
    };
    r.device->CreateInputLayout(elems, 2, vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), &r.layout);
    return true;
}

// ── Public API implementation ─────────────────────────────────────────────────

bool Renderer_Create(void* hwnd, int width, int height)
{
    g_r = new RendererState();
    g_r->width = width; g_r->height = height;

    DXGI_SWAP_CHAIN_DESC scd{};
    scd.BufferCount                         = 2;
    scd.BufferDesc.Width                    = (UINT)width;
    scd.BufferDesc.Height                   = (UINT)height;
    scd.BufferDesc.Format                   = DXGI_FORMAT_R8G8B8A8_UNORM;
    scd.BufferDesc.RefreshRate.Numerator    = 60;
    scd.BufferDesc.RefreshRate.Denominator  = 1;
    scd.BufferUsage                         = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    scd.OutputWindow                        = (HWND)hwnd;
    scd.SampleDesc.Count                    = 1;
    scd.Windowed                            = TRUE;
    scd.SwapEffect                          = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    D3D_FEATURE_LEVEL fl;
    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION,
        &scd, &g_r->sc, &g_r->device, &fl, &g_r->ctx);

    if (FAILED(hr))
    {
        // Fallback: WARP software device (useful without a discrete GPU)
        hr = D3D11CreateDeviceAndSwapChain(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
            nullptr, 0, D3D11_SDK_VERSION,
            &scd, &g_r->sc, &g_r->device, &fl, &g_r->ctx);
    }
    if (FAILED(hr)) { SetErr("D3D11CreateDeviceAndSwapChain failed"); delete g_r; g_r = nullptr; return false; }

    if (!CreateRTV(*g_r) || !CreateDSV(*g_r, width, height) || !CompileShaders(*g_r))
    { delete g_r; g_r = nullptr; return false; }

    // Constant buffer
    D3D11_BUFFER_DESC cbd{};
    cbd.ByteWidth      = sizeof(ConstantBuffer);
    cbd.Usage          = D3D11_USAGE_DYNAMIC;
    cbd.BindFlags      = D3D11_BIND_CONSTANT_BUFFER;
    cbd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    g_r->device->CreateBuffer(&cbd, nullptr, &g_r->cb);

    return true;
}

void Renderer_Destroy()
{
    if (g_r) { delete g_r; g_r = nullptr; }
}

bool Renderer_LoadFile(const char* path)
{
    if (!g_r) return false;

    std::vector<MeshVertex> mv;
    std::vector<uint32_t>   mi;
    if (!LoadOBJ(path, mv, mi)) { SetErr("Failed to parse OBJ file"); return false; }

    g_r->vb.Reset();
    g_r->ib.Reset();
    g_r->indexCount = (UINT)mi.size();

    // Build GPU-side vertex buffer from MeshVertex to GPUVertex
    std::vector<GPUVertex> gpuVerts(mv.size());
    float maxExt = 0.f;
    for (size_t i = 0; i < mv.size(); i++)
    {
        gpuVerts[i].pos    = { mv[i].x, mv[i].y, mv[i].z };
        gpuVerts[i].normal = { mv[i].nx, mv[i].ny, mv[i].nz };
        // Avoid std::max initializer_list form; use explicit comparisons instead
        float ax = fabsf(mv[i].x), ay = fabsf(mv[i].y), az = fabsf(mv[i].z);
        if (ax > maxExt) maxExt = ax;
        if (ay > maxExt) maxExt = ay;
        if (az > maxExt) maxExt = az;
    }

    D3D11_BUFFER_DESC      bd{};
    D3D11_SUBRESOURCE_DATA sd{};

    bd.ByteWidth = (UINT)(gpuVerts.size() * sizeof(GPUVertex));
    bd.Usage     = D3D11_USAGE_IMMUTABLE;
    bd.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    sd.pSysMem   = gpuVerts.data();
    g_r->device->CreateBuffer(&bd, &sd, &g_r->vb);

    bd.ByteWidth = (UINT)(mi.size() * sizeof(uint32_t));
    bd.BindFlags = D3D11_BIND_INDEX_BUFFER;
    sd.pSysMem   = mi.data();
    g_r->device->CreateBuffer(&bd, &sd, &g_r->ib);

    g_r->distance = maxExt * 2.5f;
    return true;
}

void Renderer_Render()
{
    if (!g_r || !g_r->vb) return;
    auto& r = *g_r;

    float bg[] = { 0.10f, 0.12f, 0.15f, 1.f };
    r.ctx->ClearRenderTargetView(r.rtv.Get(), bg);
    r.ctx->ClearDepthStencilView(r.dsv.Get(), D3D11_CLEAR_DEPTH, 1.f, 0);

    // Camera
    float aspect = r.width / (float)r.height;
    float cx = r.distance * cosf(r.pitch) * sinf(r.yaw);
    float cy = r.distance * sinf(r.pitch);
    float cz = r.distance * cosf(r.pitch) * cosf(r.yaw);
    XMMATRIX proj  = XMMatrixPerspectiveFovLH(XMConvertToRadians(60.f), aspect, 0.01f, 10000.f);
    XMMATRIX view  = XMMatrixLookAtLH(XMVectorSet(cx, cy, cz, 0),
                                      XMVectorZero(),
                                      XMVectorSet(0, 1, 0, 0));
    XMMATRIX model = XMMatrixIdentity();
    XMMATRIX mvp   = XMMatrixTranspose(model * view * proj);
    XMMATRIX modelT = XMMatrixTranspose(model);

    // Upload constant buffer
    D3D11_MAPPED_SUBRESOURCE ms;
    r.ctx->Map(r.cb.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &ms);
    auto* cb = static_cast<ConstantBuffer*>(ms.pData);
    XMStoreFloat4x4(&cb->mvp,   mvp);
    XMStoreFloat4x4(&cb->model, modelT);
    cb->lightDir = { 0.5f, -0.8f, 0.3f };
    cb->_pad = 0;
    r.ctx->Unmap(r.cb.Get(), 0);

    // Pipeline
    D3D11_VIEWPORT vp{ 0, 0, (float)r.width, (float)r.height, 0, 1 };
    r.ctx->RSSetViewports(1, &vp);
    r.ctx->OMSetRenderTargets(1, r.rtv.GetAddressOf(), r.dsv.Get());
    r.ctx->IASetInputLayout(r.layout.Get());
    r.ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    UINT stride = sizeof(GPUVertex), offset = 0;
    r.ctx->IASetVertexBuffers(0, 1, r.vb.GetAddressOf(), &stride, &offset);
    r.ctx->IASetIndexBuffer(r.ib.Get(), DXGI_FORMAT_R32_UINT, 0);
    r.ctx->VSSetShader(r.vs.Get(), nullptr, 0);
    r.ctx->PSSetShader(r.ps.Get(), nullptr, 0);
    r.ctx->VSSetConstantBuffers(0, 1, r.cb.GetAddressOf());
    r.ctx->PSSetConstantBuffers(0, 1, r.cb.GetAddressOf());
    r.ctx->DrawIndexed(r.indexCount, 0, 0);

    r.sc->Present(0, 0);
}

void Renderer_Resize(int width, int height)
{
    if (!g_r || width <= 0 || height <= 0) return;
    auto& r = *g_r;
    r.width = width; r.height = height;
    r.ctx->OMSetRenderTargets(0, nullptr, nullptr);
    r.rtv.Reset();
    r.dsv.Reset();
    r.sc->ResizeBuffers(0, (UINT)width, (UINT)height, DXGI_FORMAT_UNKNOWN, 0);
    CreateRTV(r);
    CreateDSV(r, width, height);
}

void Renderer_SetViewAngle(float distance, float pitch, float yaw)
{
    if (!g_r) return;
    g_r->distance = distance;
    g_r->pitch    = pitch;
    g_r->yaw      = yaw;
}

const char* Renderer_GetLastError()
{
    return g_err.c_str();
}
