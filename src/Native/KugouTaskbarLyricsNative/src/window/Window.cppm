module;

#include <Windows.h>
#include <algorithm>
#include <functional>
#include <fstream>
#include <mutex>
#include <string>

export module window.Window;

import plugin.Config;
import taskbar.Taskbar;
import taskbar.Registry;
import window.Renderer;

// 日志功能
static std::mutex g_logMutex;
static const wchar_t* LOG_FILE = L"./dll.log";

static void logWindow(const std::string& msg) {
    std::lock_guard<std::mutex> lock(g_logMutex);
    std::ofstream ofs(LOG_FILE, std::ios::app);
    if (ofs.is_open()) {
        ofs << "[Window] " << msg << std::endl;
    }
}

export class Window {
private:
    HWND hwnd = nullptr;
    static constexpr UINT WM_APP_REFRESH = WM_APP + 1;
    static constexpr UINT WM_APP_UPDATE_LAYOUT = WM_APP + 2;

    static auto CALLBACK WindowProc(const HWND hwnd, const UINT message, const WPARAM wParam, const LPARAM lParam) -> LRESULT {
        if (message == WM_CREATE) [[unlikely]] {
            const auto create = reinterpret_cast<LPCREATESTRUCT>(lParam);
            const auto window = static_cast<Window *>(create->lpCreateParams);
            SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(window));
        }
        if (const auto that = reinterpret_cast<Window *>(GetWindowLongPtr(hwnd, GWLP_USERDATA))) [[likely]] {
            if (message == WM_APP_REFRESH) {
                logWindow("WindowProc - received WM_APP_REFRESH message");
                that->refresh();
                return 0;
            }
            if (message == WM_APP_UPDATE_LAYOUT) {
                that->update();
                return 0;
            }
            return that->handleMessage(hwnd, message, wParam, lParam);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    auto handleMessage(const HWND hwnd, const UINT message, const WPARAM wParam, const LPARAM lParam) -> LRESULT {
        switch (message) {
            case WM_CREATE: {
                this->hwnd = hwnd;
                this->taskbar.initialize();
                this->taskbar.setListener(std::bind(&Window::update, this));
                this->renderer.onCreate(hwnd);
                // 主动调用一次 update() 来初始化窗口尺寸和位置
                logWindow("WM_CREATE - calling update() to initialize window size");
                this->update();
                break;
            }
            case WM_SIZE: {
                const auto width = LOWORD(lParam);
                const auto height = HIWORD(lParam);
                const auto dpi = GetDpiForWindow(hwnd);
                logWindow("WM_SIZE - width: " + std::to_string(width) + 
                         ", height: " + std::to_string(height) + 
                         ", dpi: " + std::to_string(dpi));
                this->renderer.onSize(width, height, dpi);
                break;
            }
            case WM_PAINT: {
                this->renderer.onPaint();
                ValidateRect(hwnd, nullptr);
                break;
            }
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            default: return DefWindowProc(hwnd, message, wParam, lParam);
        }
        return 0;
    }

public:
    Renderer renderer{};
    Taskbar taskbar{};


    auto create() -> void {
        const auto dll_instance = GetModuleHandle(nullptr);
        const auto class_name = L"taskbar_lyrics_musicfox";
        RegisterClassEx(new WNDCLASSEX{
            .cbSize = sizeof(WNDCLASSEX),
            .lpfnWndProc = Window::WindowProc,
            .hInstance = dll_instance,
            .lpszClassName = class_name,
        });
        CreateWindowEx(
            WS_EX_NOPARENTNOTIFY | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
            class_name,
            nullptr,
            WS_CHILD | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            Taskbar::getHWND(),
            nullptr,
            dll_instance,
            this
        );
    }

    auto runner() -> void {
        MSG msg{};
        while (GetMessage(&msg, nullptr, 0, 0)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    auto refresh() -> void {
        if (this->hwnd) {
            logWindow("refresh() called - directly calling onPaint");
            // 直接调用绘制，不依赖 Windows 消息机制
            this->renderer.onPaint();
            logWindow("refresh() completed");
        } else {
            logWindow("refresh() called but hwnd is null!");
        }
    }

    // 新增：窗口是否已就绪（是否有 HWND）
    auto isReady() const -> bool {
        return this->hwnd != nullptr;
    }

    // 新增：以异步消息请求刷新（线程安全）
    auto postRefresh() -> void {
        if (this->hwnd) {
            logWindow("postRefresh() - posting WM_APP_REFRESH message");
            PostMessage(this->hwnd, WM_APP_REFRESH, 0, 0);
        } else {
            logWindow("postRefresh() - hwnd is null!");
        }
    }

    auto postUpdateLayout() -> void {
        if (this->hwnd) {
            PostMessage(this->hwnd, WM_APP_UPDATE_LAYOUT, 0, 0);
        }
    }

    auto update() -> void {
        if (this->hwnd == nullptr) [[unlikely]] {
            logWindow("update() called but hwnd is null!");
            return;
        }

        logWindow("update() - calculating window size and position");

        // 添加异常处理，防止在 Windows 10 上因 UI Automation 查询失败而崩溃
        RECT taskbarFrame{}, trayFrameRect{}, widgetsButtonRect{}, taskListRect{};

        try {
            taskbarFrame = this->taskbar.getRectForTaskbarFrame();
            logWindow("update() - taskbarFrame: left=" + std::to_string(taskbarFrame.left) +
                     ", right=" + std::to_string(taskbarFrame.right) +
                     ", top=" + std::to_string(taskbarFrame.top) +
                     ", bottom=" + std::to_string(taskbarFrame.bottom));

            trayFrameRect = this->taskbar.getRectForTrayFrame();
            logWindow("update() - trayFrameRect retrieved");

            widgetsButtonRect = this->taskbar.getRectForWidgetsButton();
            logWindow("update() - widgetsButtonRect retrieved");

            taskListRect = this->taskbar.getRectForTaskList();
            logWindow("update() - taskListRect retrieved");
        } catch (...) {
            logWindow("update() - Exception caught while getting taskbar rects, using fallback");
            // 如果查询失败，使用任务栏窗口的客户区作为后备
            HWND taskbarHwnd = Taskbar::getHWND();
            if (taskbarHwnd && !GetClientRect(taskbarHwnd, &taskbarFrame)) {
                logWindow("update() - GetClientRect failed, using default rect");
                // 使用默认值：假设任务栏在底部，高度40像素
                RECT desktop;
                SystemParametersInfo(SPI_GETWORKAREA, 0, &desktop, 0);
                taskbarFrame = {0, desktop.bottom, desktop.right, desktop.bottom + 40};
            }
            // 其他矩形使用空值
            trayFrameRect = taskListRect = widgetsButtonRect = {0, 0, 0, 0};
        }

        // 验证 taskbarFrame 是否有效
        if (taskbarFrame.right <= taskbarFrame.left || taskbarFrame.bottom <= taskbarFrame.top) {
            logWindow("update() - Invalid taskbarFrame, aborting update");
            return;
        }

        auto offset = 0L;
        auto width = 0L;
        auto height = 0L;

        logWindow("update() - taskListRect: left=" + std::to_string(taskListRect.left) +
                 ", right=" + std::to_string(taskListRect.right));
        logWindow("update() - trayFrameRect: left=" + std::to_string(trayFrameRect.left) +
                 ", right=" + std::to_string(trayFrameRect.right));
        logWindow("update() - widgetsButtonRect: left=" + std::to_string(widgetsButtonRect.left) +
                 ", right=" + std::to_string(widgetsButtonRect.right));

        switch (config.window_alignment) {
            case TASKBAR_WINDOW_ALIGNMENT::TASKBAR_WINDOW_ALIGNMENT_AUTO: {
                // 自动模式：根据任务栏图标位置自动调整歌词显示位置
                if (Registry::isTaskbarCentered()) {
                    // 图标居中时：歌词显示在左侧（Widgets按钮右侧到任务列表左侧）
                    logWindow("AUTO mode - Taskbar icons centered, displaying lyrics on LEFT");
                    width += taskListRect.left;
                    if (Registry::isWidgetsEnabled()) {
                        offset += widgetsButtonRect.right;
                    }
                } else {
                    // 图标居左时：歌词显示在右侧（任务列表右侧到托盘左侧）
                    logWindow("AUTO mode - Taskbar icons left-aligned, displaying lyrics on RIGHT");
                    offset += taskListRect.right;
                    width += trayFrameRect.left;
                }
                logWindow("update() - after AUTO: offset=" + std::to_string(offset) + 
                         ", width=" + std::to_string(width));
                break;
            }
            case TASKBAR_WINDOW_ALIGNMENT::TASKBAR_WINDOW_ALIGNMENT_RIGHT: {
                // 强制右对齐：歌词显示在任务列表右侧到托盘左侧
                logWindow("RIGHT mode - forcing lyrics on RIGHT");
                offset += taskListRect.right;
                width += trayFrameRect.left;
                logWindow("update() - after RIGHT: offset=" + std::to_string(offset) + 
                         ", width=" + std::to_string(width));
                break;
            }
            case TASKBAR_WINDOW_ALIGNMENT::TASKBAR_WINDOW_ALIGNMENT_LEFT: {
                // 强制左对齐：歌词显示在任务栏左侧（Widgets按钮右侧到任务列表左侧）
                if (Registry::isWidgetsEnabled()) {
                    offset += widgetsButtonRect.right;
                    width += taskListRect.left;
                } else {
                    offset += taskbarFrame.left;
                    width += taskListRect.left;
                }
                break;
            }
            case TASKBAR_WINDOW_ALIGNMENT::TASKBAR_WINDOW_ALIGNMENT_CENTER: {
                // 居中模式：歌词占据整个任务栏宽度
                offset += taskbarFrame.left;
                width += taskbarFrame.right;
                break;
            }
        }

        offset += config.margin_left;
        width -= config.margin_right + offset;
        RECT taskbarClientRect{};
        if (GetClientRect(Taskbar::getHWND(), &taskbarClientRect)) {
            const auto maximumOffset = std::max(0L, taskbarClientRect.right - width);
            offset = std::clamp(offset + config.horizontal_offset, 0L, maximumOffset);
        } else {
            offset += config.horizontal_offset;
        }
        height += taskbarFrame.bottom - taskbarFrame.top;

        logWindow("update() - final position: offset=" + std::to_string(offset) + 
                 ", width=" + std::to_string(width) + 
                 ", height=" + std::to_string(height));

        BringWindowToTop(this->hwnd);
        MoveWindow(this->hwnd, offset, 0, width, height, false);
        RedrawWindow(this->hwnd, nullptr, nullptr, RDW_INVALIDATE | RDW_UPDATENOW);

        logWindow("update() - MoveWindow completed");
    }
};
