module;

#include <Windows.h>
#include <thread>
#include <atomic>
#include <chrono>
#include <mutex>

export module plugin.Plugin;

import plugin.Config;
import window.Window;
import window.WindowWin10;
import utils.WindowsVersion;

export class Plugin {
public:
    HANDLE mutex = nullptr;
    Window *window = nullptr;
    WindowWin10 *windowWin10 = nullptr;
    bool isWin10Mode = false;

private:
    std::atomic_bool refreshPending{false};
    std::mutex refreshMutex;
    std::chrono::steady_clock::time_point lastRefreshTime;
    static constexpr int MIN_REFRESH_INTERVAL_MS = 50; // 最小刷新间隔：50ms (20fps)

    Plugin() : lastRefreshTime(std::chrono::steady_clock::now()) {
        this->mutex = CreateMutex(nullptr, true, L"Global\\Taskbar-Lyrics-Musicfox");
        if (GetLastError() == ERROR_ALREADY_EXISTS) {
            CloseHandle(this->mutex);
            this->mutex = nullptr;
        } else {
            this->initialize();
        }
    }

    ~Plugin() {
        if (this->mutex) {
            ReleaseMutex(this->mutex);
            CloseHandle(this->mutex);
            this->mutex = nullptr;
        }
        if (this->window) {
            delete this->window;
            this->window = nullptr;
        }
        if (this->windowWin10) {
            delete this->windowWin10;
            this->windowWin10 = nullptr;
        }
    }

    auto initialize() -> void {
        // 初始化 Windows 版本检测
        WindowsVersion::initialize();
        
        // 判断是否为 Windows 10
        isWin10Mode = WindowsVersion::isWindows10();
        
        std::thread([this] {
            if (isWin10Mode) {
                // Windows 10
                this->windowWin10 = new WindowWin10();
                this->windowWin10->create();

                // 等待 window 就绪
                for (int i = 0; i < 500 && !this->windowWin10->isReady(); ++i) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(10));
                }

                if (this->refreshPending.exchange(false)) {
                    this->windowWin10->postRefresh();
                }

                this->windowWin10->runner();
            } else {
                // Windows 11: 使用原有 UI Automation 方式
                this->window = new Window();
                this->window->create();

                // 等待 window 就绪（有 HWND）——短轮询
                for (int i = 0; i < 500 && !this->window->isReady(); ++i) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(10));
                }

                // 如果在 window 未就绪时有刷新请求，主动触发一次
                if (this->refreshPending.exchange(false)) {
                    this->window->postRefresh();
                }

                this->window->runner();
            }
        }).detach();
    }

public:
    static auto getInstance() -> Plugin & {
        static Plugin instance;
        return instance;
    }

    // 线程安全的刷新请求接口：若 window 就绪则发消息刷新，否则标记为待刷新
    // 使用节流机制防止过度刷新
    static auto refresh() -> void {
        auto &inst = Plugin::getInstance();

        // 根据模式选择不同的窗口实例
        bool isReady = inst.isWin10Mode ? 
            (inst.windowWin10 && inst.windowWin10->isReady()) :
            (inst.window && inst.window->isReady());

        if (isReady) {
            // 使用节流：检查距离上次刷新是否已过最小间隔
            std::lock_guard<std::mutex> lock(inst.refreshMutex);
            auto now = std::chrono::steady_clock::now();
            auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - inst.lastRefreshTime).count();

            if (elapsed >= MIN_REFRESH_INTERVAL_MS) {
                // 已过最小间隔，立即刷新
                if (inst.isWin10Mode) {
                    inst.windowWin10->postRefresh();
                } else {
                    inst.window->postRefresh();
                }
                inst.lastRefreshTime = now;
            } else {
                // 未达到最小间隔，标记为待刷新（会在下次有机会时刷新）
                inst.refreshPending.store(true, std::memory_order_relaxed);
            }
        } else {
            // 窗口未就绪，标记为待刷新
            inst.refreshPending.store(true, std::memory_order_relaxed);
        }
    }

    static auto updateLayout() -> void {
        auto &inst = Plugin::getInstance();
        if (inst.isWin10Mode) {
            if (inst.windowWin10 && inst.windowWin10->isReady()) {
                inst.windowWin10->updateLayout();
            }
        } else if (inst.window && inst.window->isReady()) {
            inst.window->postUpdateLayout();
        }
    }
};
