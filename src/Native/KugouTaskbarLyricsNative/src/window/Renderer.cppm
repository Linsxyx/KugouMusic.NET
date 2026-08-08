module;

#include <d3d11.h>
#include <d2d1.h>
#include <dwrite.h>
#include <dcomp.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <fstream>
#include <mutex>
#include <string>
#include <sstream>
#include <Windows.h>

export module window.Renderer;
import window.Lyrics;
import plugin.Config;

// 全局日志锁与文件路径（与主DLL一致）
static std::mutex g_logMutex;
static const wchar_t* LOG_FILE = L"./dll.log";

// 通用日志函数
static void logDll(const std::string& msg) {
    std::lock_guard<std::mutex> lock(g_logMutex);
    std::ofstream ofs(LOG_FILE, std::ios::app);
    if (ofs.is_open()) {
        ofs << "[DLL-Renderer] " << msg << std::endl;
    }
}

static void logDll(const std::wstring& msg) {
    std::lock_guard<std::mutex> lock(g_logMutex);
    std::ofstream ofs(LOG_FILE, std::ios::app);
    if (ofs.is_open()) {
        int size = WideCharToMultiByte(CP_UTF8, 0, msg.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::string utf8(size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, msg.c_str(), -1, utf8.data(), size, nullptr, nullptr);
        ofs << "[DLL-Renderer] " << utf8.c_str() << std::endl;
    }
}

export class Renderer {
private:
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice{};
    Microsoft::WRL::ComPtr<ID2D1Factory> d2dFactory{};
    Microsoft::WRL::ComPtr<ID2D1RenderTarget> d2dRenderTarget{};
    Microsoft::WRL::ComPtr<IDWriteFactory> dwriteFactory{};
    Microsoft::WRL::ComPtr<IDXGIFactory2> dxgiFactory{};
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgiDevice{};
    Microsoft::WRL::ComPtr<IDXGISwapChain1> dxgiSwapChain{};
    Microsoft::WRL::ComPtr<IDXGISurface1> dxgiSurface{};
    Microsoft::WRL::ComPtr<IDCompositionDevice> dcompDevice{};
    Microsoft::WRL::ComPtr<IDCompositionTarget> dcompTarget{};
    Microsoft::WRL::ComPtr<IDCompositionVisual> dcompVisual{};

    auto initializeDirectX() -> void {
        logDll("Initializing DirectX...");

        auto hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,
            0,
            D3D11_SDK_VERSION,
            &this->d3dDevice,
            nullptr,
            nullptr
        );
        if (FAILED(hr)) {
            logDll("Failed to create D3D11 device");
        }
        else {
            logDll("D3D11 device created");
        }

        hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, IID_PPV_ARGS(&this->d2dFactory));
        if (FAILED(hr)) logDll("Failed to create D2D factory");
        else logDll("D2D factory created");

        hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(this->dwriteFactory), &this->dwriteFactory);
        if (FAILED(hr)) logDll("Failed to create DWrite factory");
        else logDll("DWrite factory created");
    }

    auto initializeSwapChain() -> void {
        logDll("Initializing SwapChain...");

        constexpr auto desc = DXGI_SWAP_CHAIN_DESC1{
            .Width = 1,
            .Height = 1,
            .Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            .SampleDesc{.Count = 1},
            .BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
            .BufferCount = 2,
            .SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
            .AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED,
        };

        CreateDXGIFactory(IID_PPV_ARGS(&this->dxgiFactory));
        this->d3dDevice.As(&this->dxgiDevice);
        auto hr = this->dxgiFactory->CreateSwapChainForComposition(
            this->dxgiDevice.Get(), &desc, nullptr, &this->dxgiSwapChain
        );
        if (FAILED(hr)) logDll("Failed to create swap chain");
        else logDll("Swap chain created successfully");
    }

    auto initializeComposition(const HWND hwnd) -> void {
        logDll("Initializing DComposition...");

        auto hr = DCompositionCreateDevice(this->dxgiDevice.Get(), IID_PPV_ARGS(&this->dcompDevice));
        if (FAILED(hr)) { logDll("Failed to create DComp device"); return; }

        this->dcompDevice->CreateTargetForHwnd(hwnd, false, &this->dcompTarget);
        this->dcompDevice->CreateVisual(&this->dcompVisual);
        this->dcompVisual->SetContent(this->dxgiSwapChain.Get());
        this->dcompTarget->SetRoot(this->dcompVisual.Get());
        logDll("DComposition initialized");
    }

public:
    auto onCreate(const HWND hwnd) -> void {
        logDll("Renderer::onCreate called");
        this->initializeDirectX();
        this->initializeSwapChain();
        this->initializeComposition(hwnd);
        logDll("Renderer::onCreate finished");
    }

    auto onSize(const UINT width, const UINT height, const UINT dpi) -> void {
        logDll("Renderer::onSize - resizing buffers");
        this->dxgiSurface.Reset();
        this->d2dRenderTarget.Reset();
        this->dxgiSwapChain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN, 0);
        this->dxgiSwapChain->GetBuffer(0, IID_PPV_ARGS(&this->dxgiSurface));
        this->d2dFactory->CreateDxgiSurfaceRenderTarget(
            this->dxgiSurface.Get(),
            D2D1::RenderTargetProperties(
                D2D1_RENDER_TARGET_TYPE_DEFAULT,
                D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
                dpi,
                dpi
            ),
            &this->d2dRenderTarget
        );
        logDll("Renderer::onSize finished");
    }

    auto onPaint() -> void {
        if (!this->d2dRenderTarget || !this->dwriteFactory) {
            logDll("Renderer::onPaint - missing render target or dwrite factory");
            return;
        }

        logDll("Renderer::onPaint - begin drawing");
        // 添加日志显示当前要绘制的歌词
        logDll(L"Current lyric_primary: [" + config.lyric_primary + L"]");
        logDll(L"Current lyric_secondary: [" + config.lyric_secondary + L"]");

        Lyrics lyrics{ this->d2dRenderTarget.Get(), this->dwriteFactory.Get() };

        this->d2dRenderTarget->BeginDraw();
        this->d2dRenderTarget->Clear(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.0f));
        lyrics.onDraw();
        this->d2dRenderTarget->EndDraw();

        this->dxgiSwapChain->Present(1, 0);
        this->dcompDevice->Commit();
        logDll("Renderer::onPaint - draw completed");
    }
};
