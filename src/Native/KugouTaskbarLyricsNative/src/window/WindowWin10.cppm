module;

#include <Windows.h>
#include <oleacc.h>
#include <d2d1.h>
#include <dwrite.h>
#include <algorithm>
#include <cmath>
#include <functional>
#include <fstream>
#include <mutex>
#include <string>
#include <wrl/client.h>

export module window.WindowWin10;

import plugin.Config;
import window.Lyrics;

#pragma comment(lib, "oleacc.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dwrite.lib")

// 日志功能
static std::mutex g_logMutex;
static const wchar_t* LOG_FILE = L"./dll.log";

static void logWin10(const std::string& msg) {
    std::lock_guard<std::mutex> lock(g_logMutex);
    std::ofstream ofs(LOG_FILE, std::ios::app);
    if (ofs.is_open()) {
        ofs << "[WindowWin10] " << msg << std::endl;
    }
}

// 函数指针类型定义
typedef HRESULT(WINAPI* pfnAccessibleObjectFromWindow)(HWND hwnd, DWORD dwId, REFIID riid, void** ppvObject);
typedef HRESULT(WINAPI* pfnAccessibleChildren)(IAccessible* paccContainer, LONG iChildStart, LONG cChildren, VARIANT* rgvarChildren, LONG* pcObtained);

export class WindowWin10 {
private:
    HWND hwnd = nullptr;
    HWND taskbarHwnd = nullptr;
    HWND taskListHwnd = nullptr;
    HWND trayNotifyHwnd = nullptr;
    HMODULE hOleacc = nullptr;
    pfnAccessibleObjectFromWindow AccessibleObjectFromWindowFunc = nullptr;
    pfnAccessibleChildren AccessibleChildrenFunc = nullptr;
    
    int lyricsWidth = 300;  // 歌词区域宽度
    int taskIconsWidth = 0;  // 任务图标总宽度
    
    static constexpr UINT WM_APP_REFRESH = WM_APP + 1;
    static constexpr UINT WM_APP_UPDATE_LAYOUT = WM_APP + 2;

    static auto CALLBACK WindowProc(const HWND hwnd, const UINT message, const WPARAM wParam, const LPARAM lParam) -> LRESULT {
        if (message == WM_CREATE) [[unlikely]] {
            const auto create = reinterpret_cast<LPCREATESTRUCT>(lParam);
            const auto window = static_cast<WindowWin10*>(create->lpCreateParams);
            SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(window));
        }
        if (const auto that = reinterpret_cast<WindowWin10*>(GetWindowLongPtr(hwnd, GWLP_USERDATA))) [[likely]] {
            if (message == WM_APP_REFRESH) {
                that->refresh();
                return 0;
            }
            if (message == WM_APP_UPDATE_LAYOUT) {
                that->updateTaskListPosition();
                that->updateWindowPosition();
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
                this->renderer.onCreate(hwnd);
                logWin10("WM_CREATE - window created");
                break;
            }
            case WM_SIZE: {
                const auto width = LOWORD(lParam);
                const auto height = HIWORD(lParam);
                logWin10("WM_SIZE - width: " + std::to_string(width) + 
                         ", height: " + std::to_string(height));
                this->renderer.onSize(width, height);
                break;
            }
            case WM_PAINT: {
                PAINTSTRUCT ps;
                HDC hdc = BeginPaint(hwnd, &ps);
                this->renderer.onPaint(hdc);
                EndPaint(hwnd, &ps);
                break;
            }
            case WM_ERASEBKGND: {
                // 让WM_PAINT处理所有绘制
                return 1;
            }
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            case WM_TIMER: {
                if (wParam == 1) {
                    updateTaskListPosition();
                    updateWindowPosition();
                } else if (wParam == 2) {
                    // 歌词滚动tick
                    this->renderer.tick();
                    InvalidateRect(hwnd, nullptr, FALSE);
                }
                break;
            }
            default: return DefWindowProc(hwnd, message, wParam, lParam);
        }
        return 0;
    }

    // 查找 IAccessible 对象（参考 TrayS 的 Find 函数）
    auto findAccessibleChild(IAccessible* pAcc, int role, IAccessible** ppChild) -> bool {
        if (!pAcc) {
            logWin10("findAccessibleChild - pAcc is null");
            return false;
        }
        
        long childCount = 0;
        if (pAcc->get_accChildCount(&childCount) != S_OK || childCount == 0) {
            logWin10("findAccessibleChild - get_accChildCount failed or childCount=0");
            return false;
        }
        
        logWin10("findAccessibleChild - looking for role=" + std::to_string(role) + " in " + std::to_string(childCount) + " children");

        IEnumVARIANT* pEnum = nullptr;
        pAcc->QueryInterface(IID_IEnumVARIANT, (void**)&pEnum);
        if (pEnum) pEnum->Reset();

        bool found = false;
        
        for (int indexCount = 1; indexCount <= childCount && !found; indexCount++) {
            VARIANT varChild;
            VariantInit(&varChild);
            unsigned long numFetched = 0;
            
            if (pEnum) {
                pEnum->Next(1, &varChild, &numFetched);
            } else {
                varChild.vt = VT_I4;
                varChild.lVal = indexCount;
            }
            
            IDispatch* pDisp = nullptr;
            if (varChild.vt == VT_I4) {
                pAcc->get_accChild(varChild, &pDisp);
            } else {
                pDisp = varChild.pdispVal;
                if (pDisp) pDisp->AddRef();
            }
            
            IAccessible* pChild = nullptr;
            if (pDisp) {
                pDisp->QueryInterface(IID_IAccessible, (void**)&pChild);
                pDisp->Release();
            }
            
            if (pChild) {
                VARIANT varChildSelf;
                VariantInit(&varChildSelf);
                varChildSelf.vt = VT_I4;
                varChildSelf.lVal = CHILDID_SELF;
                
                VARIANT varState;
                VariantInit(&varState);
                pChild->get_accState(varChildSelf, &varState);
                
                if ((varState.lVal & 0x8000) == 0) {  // STATE_SYSTEM_INVISIBLE
                    VARIANT varRole;
                    VariantInit(&varRole);
                    pChild->get_accRole(varChildSelf, &varRole);
                    logWin10("  findAccessibleChild - child[" + std::to_string(indexCount) + "] role=" + std::to_string(varRole.lVal));
                    if (varRole.lVal == role) {
                        logWin10("  findAccessibleChild - FOUND role " + std::to_string(role));
                        *ppChild = pChild;
                        found = true;
                        VariantClear(&varRole);
                        VariantClear(&varState);
                        VariantClear(&varChildSelf);
                        VariantClear(&varChild);
                        break;
                    }
                    VariantClear(&varRole);
                } else {
                    logWin10("  findAccessibleChild - child[" + std::to_string(indexCount) + "] is invisible, state=" + std::to_string(varState.lVal));
                }
                VariantClear(&varState);
                VariantClear(&varChildSelf);
                
                if (!found) {
                    pChild->Release();
                }
            }
            VariantClear(&varChild);
        }
        
        if (pEnum) pEnum->Release();
        logWin10("findAccessibleChild - result: " + std::to_string(found));
        return found;
    }

    // 计算任务图标总宽度（参考 TrayS 的 SetTaskBarPos 函数）
    auto calculateTaskIconsWidth() -> int {
        if (!taskListHwnd || !hOleacc) {
            logWin10("calculateTaskIconsWidth - taskListHwnd or hOleacc is null");
            return 0;
        }

        IAccessible* pAcc = nullptr;
        HRESULT hr = AccessibleObjectFromWindowFunc(taskListHwnd, OBJID_WINDOW, IID_IAccessible, (void**)&pAcc);
        if (FAILED(hr) || !pAcc) {
            logWin10("calculateTaskIconsWidth - AccessibleObjectFromWindow failed, hr=" + std::to_string(hr));
            return 0;
        }

        // 按照 TrayS 的方式：先查找角色为 22 (ROLE_SYSTEM_CLIENT) 的子对象
        IAccessible* paccChild = nullptr;
        if (!findAccessibleChild(pAcc, 22, &paccChild)) {
            logWin10("calculateTaskIconsWidth - findAccessibleChild(role=22) failed, releasing pAcc");
            pAcc->Release();
            return 0;
        }
        pAcc->Release();
        
        if (!paccChild) {
            logWin10("calculateTaskIconsWidth - paccChild is null after findAccessibleChild");
            return 0;
        }

        long returnCount = 0;
        int totalWidth = 0;
        LONG lastLeft = 0;

        // 获取 paccChild 的子元素数量
        long childCount = 0;
        if (paccChild->get_accChildCount(&childCount) == S_OK && childCount != 0) {
            logWin10("calculateTaskIconsWidth - processing " + std::to_string(childCount) + " children");
            VARIANT* pArray = (VARIANT*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(VARIANT) * childCount);
            if (pArray && AccessibleChildrenFunc(paccChild, 0L, childCount, pArray, &returnCount) == S_OK) {
                logWin10("calculateTaskIconsWidth - got " + std::to_string(returnCount) + " accessible children");
                for (int i = 0; i < returnCount; i++) {
                    VARIANT vtChild = pArray[i];
                    
                    VARIANT varState;
                    VariantInit(&varState);
                    if (paccChild->get_accState(vtChild, &varState) == S_OK) {
                        // 检查是否可见（不是 STATE_SYSTEM_INVISIBLE）
                        if ((varState.lVal & 0x8000) == 0) {
                            VARIANT varRole;
                            VariantInit(&varRole);
                            if (paccChild->get_accRole(vtChild, &varRole) == S_OK) {
                                int roleValue = varRole.lVal;
                                logWin10("  Child[" + std::to_string(i) + "] role=" + std::to_string(roleValue) + 
                                        ", state=" + std::to_string(varState.lVal));
                                
                                // 角色 0x2b (ROLE_SYSTEM_PUSHBUTTON=43) 或 0x39 (57)
                                if (roleValue == 0x2b || roleValue == 0x39) {
                                    LONG left, top, width, height;
                                    if (paccChild->accLocation(&left, &top, &width, &height, vtChild) == S_OK) {
                                        logWin10("  Child[" + std::to_string(i) + "] location: left=" + std::to_string(left) + 
                                                ", width=" + std::to_string(width));
                                        if (lastLeft != left) {
                                            totalWidth += width;
                                            lastLeft = left;
                                            logWin10("  -> Added to totalWidth=" + std::to_string(totalWidth));
                                        }
                                    }
                                }
                            }
                            VariantClear(&varRole);
                        }
                    }
                    VariantClear(&varState);
                }
                HeapFree(GetProcessHeap(), 0, pArray);
            } else {
                logWin10("calculateTaskIconsWidth - AccessibleChildren failed or pArray is null");
            }
        } else {
            logWin10("calculateTaskIconsWidth - get_accChildCount failed or childCount=0");
        }

        // 如果 paccChild 是新创建的（不是 pAcc 本身），需要释放
        if (paccChild != pAcc) {
            paccChild->Release();
        }
        
        logWin10("calculateTaskIconsWidth - totalWidth: " + std::to_string(totalWidth));
        return totalWidth;
    }

    // 更新任务列表位置（参考 TrayS 的 SetTaskBarPos）
    auto updateTaskListPosition() -> void {
        if (!taskListHwnd) {
            logWin10("updateTaskListPosition - taskListHwnd is null");
            return;
        }

        // 计算任务图标总宽度
        int iconsWidth = calculateTaskIconsWidth();
        if (iconsWidth == 0) {
            logWin10("updateTaskListPosition - iconsWidth is 0, using default width");
            // 使用默认宽度，不跳过
        }

        taskIconsWidth = iconsWidth;

        // 按照 TrayS：任务列表不需要移动，保持原位
        // 歌词窗口会显示在托盘左侧
        
        logWin10("updateTaskListPosition - iconsWidth: " + std::to_string(iconsWidth));
    }

    // GDI 渲染器（参考 TrayS 实现 + 歌词滚动）
    class SimpleRenderer {
    private:
        HWND hwnd = nullptr;
        HFONT hFont = nullptr;
        HFONT hFontSecondary = nullptr;
        
        // 滚动相关
        double scrollOffset = 0.0;
        double scrollSpeed = 3.0;  // 固定滚动速度（像素/帧）
        bool needScroll = false;
        int textWidth = 0;
        int windowWidth = 0;
        std::wstring lastPrimaryText;
        int pauseTicks = 0;  // 停顿计数器（tick次数）
        bool isScrolling = false;  // 是否正在滚动
        const int PAUSE_DURATION = 10;  // 停顿时长（10次tick = 0.5秒，每次tick 50ms）
        
        int lastPrimaryFontSize = 0;
        int lastSecondaryFontSize = 0;
        DWRITE_FONT_WEIGHT lastPrimaryWeight = DWRITE_FONT_WEIGHT_NORMAL;
        DWRITE_FONT_WEIGHT lastSecondaryWeight = DWRITE_FONT_WEIGHT_NORMAL;
        std::wstring lastFontFamily;
        bool lastHasSecondary = false;
        
    public:
        auto onCreate(HWND hwnd) -> void {
            logWin10("SimpleRenderer::onCreate");
            this->hwnd = hwnd;
            const auto state = snapshotConfig();
            createFonts(state, !state.lyric_secondary.empty() && state.lyric_secondary != L" ");
            
            logWin10("SimpleRenderer::onCreate - GDI renderer created");
        }
        
        // 创建字体
        auto createFonts(const Config &state, bool hasSecondary) -> void {
            const auto primaryFontSize = hasSecondary ? state.size_primary : state.size_primary_single;
            if (hFont) {
                DeleteObject(hFont);
                hFont = nullptr;
            }

            hFont = CreateFontW(
                primaryFontSize,
                0, 0, 0,
                state.weight_primary,
                FALSE, FALSE, FALSE,
                DEFAULT_CHARSET,
                OUT_TT_PRECIS,
                CLIP_DEFAULT_PRECIS,
                ANTIALIASED_QUALITY,
                DEFAULT_PITCH | FF_DONTCARE,
                state.font_family.c_str()
            );

            if (hFontSecondary) {
                DeleteObject(hFontSecondary);
                hFontSecondary = nullptr;
            }

            hFontSecondary = CreateFontW(
                state.size_secondary,
                0, 0, 0,
                state.weight_secondary,
                FALSE, FALSE, FALSE,
                DEFAULT_CHARSET,
                OUT_TT_PRECIS,
                CLIP_DEFAULT_PRECIS,
                ANTIALIASED_QUALITY,
                DEFAULT_PITCH | FF_DONTCARE,
                state.font_family.c_str()
            );

            lastPrimaryFontSize = primaryFontSize;
            lastSecondaryFontSize = state.size_secondary;
            lastPrimaryWeight = state.weight_primary;
            lastSecondaryWeight = state.weight_secondary;
            lastFontFamily = state.font_family;
            lastHasSecondary = hasSecondary;

            logWin10("createFonts - primary size: " + std::to_string(primaryFontSize) +
                     ", secondary size: " + std::to_string(state.size_secondary));
        }
        
        auto onSize(UINT width, UINT height) -> void {
            windowWidth = width;
            lastPrimaryText.clear();
        }
        
        auto updateScrollParameters(const Config &state) -> void {
            if (!hwnd) return;

            HDC hdc = GetDC(hwnd);
            HFONT oldFont = (HFONT)SelectObject(hdc, hFont);

            SIZE textSize{};
            GetTextExtentPoint32W(
                hdc,
                state.lyric_primary.c_str(),
                static_cast<int>(state.lyric_primary.length()),
                &textSize);
            textWidth = textSize.cx;

            SelectObject(hdc, oldFont);
            ReleaseDC(hwnd, hdc);

            needScroll = textWidth > windowWidth;

            lastPrimaryText = state.lyric_primary;
            if (needScroll) {
                // 重置滚动位置到0（从左对齐开始）
                scrollOffset = 0.0;
                pauseTicks = 0;  // 重置停顿计数器
                isScrolling = false;  // 停止滚动，等待停顿结束
                logWin10("updateScrollParameters - needScroll=true, textWidth=" + std::to_string(textWidth) + 
                         ", windowWidth=" + std::to_string(windowWidth));
            } else {
                scrollOffset = 0.0;
                pauseTicks = 0;
                isScrolling = false;
                logWin10("updateScrollParameters - needScroll=false");
            }
        }
        
        auto tick() -> void {
            if (!needScroll) return;
            
            // 如果还在停顿期
            if (!isScrolling) {
                pauseTicks++;
                if (pauseTicks >= PAUSE_DURATION) {
                    isScrolling = true;  // 停顿结束，开始滚动
                    logWin10("tick - pause ended, start scrolling");
                }
                return;
            }
            
            // 滚动：从左往右平移
            scrollOffset += scrollSpeed;
            
            // 当文字滚动到末尾时停止（不循环）
            double maxOffset = textWidth - windowWidth;
            if (scrollOffset > maxOffset) {
                scrollOffset = maxOffset;  // 停在末尾
            }
        }

        static auto toColor(unsigned int argb) -> COLORREF {
            return RGB(
                (argb >> 16) & 0xFF,
                (argb >> 8) & 0xFF,
                argb & 0xFF);
        }

        static auto measureSelectedText(HDC hdc, const std::wstring &text) -> int {
            if (text.empty()) return 0;
            SIZE size{};
            return GetTextExtentPoint32W(
                hdc,
                text.c_str(),
                static_cast<int>(text.length()),
                &size)
                    ? size.cx
                    : 0;
        }

        static auto calculatePlayedWidth(HDC hdc, const Config &state) -> int {
            if (state.lyric_primary.empty()) return 0;

            if (state.words.empty()) {
                const auto duration = std::max(1.0, state.line_duration_ms);
                const auto progress = std::clamp(
                    (state.playback_position_ms - state.line_start_ms) / duration,
                    0.0,
                    1.0);
                return static_cast<int>(std::round(
                    measureSelectedText(hdc, state.lyric_primary) * progress));
            }

            std::wstring completed;
            auto playedWidth = 0;
            for (const auto &word : state.words) {
                if (state.playback_position_ms >= word.start_ms + word.duration_ms) {
                    completed += word.text;
                    playedWidth = measureSelectedText(hdc, completed);
                    continue;
                }

                if (state.playback_position_ms > word.start_ms) {
                    const auto progress = std::clamp(
                        (state.playback_position_ms - word.start_ms) /
                            std::max(1.0, word.duration_ms),
                        0.0,
                        1.0);
                    playedWidth = measureSelectedText(hdc, completed) +
                        static_cast<int>(std::round(
                            measureSelectedText(hdc, word.text) * progress));
                }
                break;
            }
            return playedWidth;
        }

        auto needsFontRefresh(const Config &state, bool hasSecondary) const -> bool {
            const auto primaryFontSize = hasSecondary ? state.size_primary : state.size_primary_single;
            return !hFont || !hFontSecondary ||
                primaryFontSize != lastPrimaryFontSize ||
                state.size_secondary != lastSecondaryFontSize ||
                state.weight_primary != lastPrimaryWeight ||
                state.weight_secondary != lastSecondaryWeight ||
                state.font_family != lastFontFamily ||
                hasSecondary != lastHasSecondary;
        }

        auto drawPrimary(HDC hdc, const Config &state, const RECT &bounds) -> void {
            if (state.lyric_primary.empty() || state.lyric_primary == L" ") return;

            auto drawRect = bounds;
            auto format = DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX;
            auto textStartX = bounds.left;

            if (needScroll) {
                textStartX = -static_cast<int>(std::round(scrollOffset));
                drawRect.left = textStartX;
                drawRect.right = textStartX + std::max(1, textWidth);
                format |= DT_LEFT;
            } else if (state.align_primary == DWRITE_TEXT_ALIGNMENT_TRAILING) {
                textStartX = bounds.right - textWidth;
                format |= DT_RIGHT;
            } else if (state.align_primary == DWRITE_TEXT_ALIGNMENT_CENTER) {
                textStartX = bounds.left + (bounds.right - bounds.left - textWidth) / 2;
                format |= DT_CENTER;
            } else {
                format |= DT_LEFT;
            }

            SetTextColor(hdc, toColor(state.color_primary));
            auto unplayedRect = drawRect;
            DrawTextW(hdc, state.lyric_primary.c_str(), -1, &unplayedRect, format);

            const auto playedWidth = std::clamp(
                calculatePlayedWidth(hdc, state),
                0,
                std::max(0, textWidth));
            const auto clipLeft = std::max<LONG>(bounds.left, textStartX);
            const auto clipRight = std::min<LONG>(bounds.right, textStartX + playedWidth);
            if (clipRight <= clipLeft) return;

            const auto savedDc = SaveDC(hdc);
            if (savedDc == 0) return;
            IntersectClipRect(hdc, clipLeft, bounds.top, clipRight, bounds.bottom);
            SetTextColor(hdc, toColor(state.color_played));
            auto playedRect = drawRect;
            DrawTextW(hdc, state.lyric_primary.c_str(), -1, &playedRect, format);
            RestoreDC(hdc, savedDc);
        }
        
        auto onPaint(HDC hdc) -> void {
            if (!hwnd) return;
            
            RECT rc;
            GetClientRect(hwnd, &rc);
            int width = rc.right - rc.left;
            int height = rc.bottom - rc.top;
            
            if (width <= 0 || height <= 0) return;

            logWin10("onPaint - size: " + std::to_string(width) + "x" + std::to_string(height));

            const auto state = snapshotConfig();
            const auto hasSecondary = !state.lyric_secondary.empty() && state.lyric_secondary != L" ";
            const auto fontChanged = needsFontRefresh(state, hasSecondary);
            if (fontChanged) {
                createFonts(state, hasSecondary);
            }
            if (fontChanged || lastPrimaryText != state.lyric_primary || windowWidth != width) {
                windowWidth = width;
                updateScrollParameters(state);
            }

            HDC hdcMem = CreateCompatibleDC(hdc);
            HBITMAP hMemBmp = CreateCompatibleBitmap(hdc, width, height);
            HBITMAP oldBmp = (HBITMAP)SelectObject(hdcMem, hMemBmp);

            HBRUSH hBrush = CreateSolidBrush(RGB(0, 0, 1));
            FillRect(hdcMem, &rc, hBrush);
            DeleteObject(hBrush);

            SetBkMode(hdcMem, TRANSPARENT);
            SetGraphicsMode(hdcMem, GM_ADVANCED);

            HFONT oldFont = (HFONT)SelectObject(hdcMem, hFont);

            if (hasSecondary) {
                const auto primaryLineHeight = static_cast<int>(std::ceil(state.size_primary * 1.45));
                const auto secondaryLineHeight = static_cast<int>(std::ceil(state.size_secondary * 1.45));
                const auto totalHeight = primaryLineHeight + state.line_spacing + secondaryLineHeight;
                const auto primaryTop = std::max(0, (height - totalHeight) / 2);
                const RECT primaryBounds{0, primaryTop, width, primaryTop + primaryLineHeight};
                drawPrimary(hdcMem, state, primaryBounds);

                const auto secondaryTop = primaryTop + primaryLineHeight + state.line_spacing;
                RECT secondaryBounds{0, secondaryTop, width, secondaryTop + secondaryLineHeight};
                HFONT previousFont = (HFONT)SelectObject(hdcMem, hFontSecondary);
                SetTextColor(hdcMem, toColor(state.color_secondary));

                auto secondaryFormat = DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX;
                if (state.align_secondary == DWRITE_TEXT_ALIGNMENT_TRAILING) {
                    secondaryFormat |= DT_RIGHT;
                } else if (state.align_secondary == DWRITE_TEXT_ALIGNMENT_CENTER) {
                    secondaryFormat |= DT_CENTER;
                } else {
                    secondaryFormat |= DT_LEFT;
                }
                DrawTextW(
                    hdcMem,
                    state.lyric_secondary.c_str(),
                    -1,
                    &secondaryBounds,
                    secondaryFormat);
                SelectObject(hdcMem, previousFont);
            } else {
                const RECT primaryBounds{0, 0, width, height};
                drawPrimary(hdcMem, state, primaryBounds);
            }

            SelectObject(hdcMem, oldFont);

            BitBlt(hdc, 0, 0, width, height, hdcMem, 0, 0, SRCCOPY);

            SelectObject(hdcMem, oldBmp);
            DeleteObject(hMemBmp);
            DeleteDC(hdcMem);
            
            logWin10("onPaint - GDI paint completed");
        }
        
        ~SimpleRenderer() {
            if (hFont) {
                DeleteObject(hFont);
            }
            if (hFontSecondary) {
                DeleteObject(hFontSecondary);
            }
        }
    };

public:
    SimpleRenderer renderer{};

    auto create() -> void {
        logWin10("create() - starting");
        
        // 加载 oleacc.dll
        hOleacc = LoadLibraryW(L"oleacc.dll");
        if (!hOleacc) {
            logWin10("create() - failed to load oleacc.dll");
            return;
        }

        AccessibleObjectFromWindowFunc = (pfnAccessibleObjectFromWindow)GetProcAddress(hOleacc, "AccessibleObjectFromWindow");
        AccessibleChildrenFunc = (pfnAccessibleChildren)GetProcAddress(hOleacc, "AccessibleChildren");

        if (!AccessibleObjectFromWindowFunc || !AccessibleChildrenFunc) {
            logWin10("create() - failed to get oleacc functions");
            FreeLibrary(hOleacc);
            hOleacc = nullptr;
            return;
        }

        // 获取任务栏窗口
        taskbarHwnd = FindWindowW(L"Shell_TrayWnd", nullptr);
        if (!taskbarHwnd) {
            logWin10("create() - failed to find taskbar window");
            return;
        }

        // 查找托盘通知区域（用于定位歌词窗口）
        trayNotifyHwnd = FindWindowExW(taskbarHwnd, nullptr, L"TrayNotifyWnd", nullptr);
        if (!trayNotifyHwnd) {
            logWin10("create() - failed to find TrayNotifyWnd");
            return;
        }
        logWin10("create() - found TrayNotifyWnd");

        // 按照 TrayS 的方式查找窗口层次：Shell_TrayWnd -> ReBarWindow32 -> MSTaskSwWClass -> MSTaskListWClass
        HWND rebarHwnd = FindWindowExW(taskbarHwnd, nullptr, L"ReBarWindow32", nullptr);
        if (!rebarHwnd) {
            logWin10("create() - failed to find ReBarWindow32");
            return;
        }
        logWin10("create() - found ReBarWindow32");

        HWND taskSwWnd = FindWindowExW(rebarHwnd, nullptr, L"MSTaskSwWClass", nullptr);
        if (!taskSwWnd) {
            logWin10("create() - failed to find MSTaskSwWClass");
            return;
        }
        logWin10("create() - found MSTaskSwWClass");

        // 在 MSTaskSwWClass 下查找 MSTaskListWClass
        taskListHwnd = FindWindowExW(taskSwWnd, nullptr, L"MSTaskListWClass", nullptr);
        if (!taskListHwnd) {
            // 备用：尝试 ToolbarWindow32
            taskListHwnd = FindWindowExW(taskSwWnd, nullptr, L"ToolbarWindow32", nullptr);
        }

        if (!taskListHwnd) {
            logWin10("create() - failed to find task list window (MSTaskListWClass or ToolbarWindow32)");
        } else {
            logWin10("create() - found task list window");
        }

        const auto dll_instance = GetModuleHandleW(nullptr);
        const auto class_name = L"taskbar_lyrics_musicfox_win10";
        
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(WNDCLASSEXW);
        wc.lpfnWndProc = WindowWin10::WindowProc;
        wc.hInstance = dll_instance;
        wc.lpszClassName = class_name;
        wc.hbrBackground = (HBRUSH)GetStockObject(NULL_BRUSH);
        
        RegisterClassExW(&wc);

        // 获取任务栏尺寸
        RECT taskbarRect;
        GetWindowRect(taskbarHwnd, &taskbarRect);
        int taskbarHeight = taskbarRect.bottom - taskbarRect.top;

        // 创建分层窗口（WS_POPUP + SetParent方式，因为WS_CHILD不能用WS_EX_LAYERED）
        hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            class_name,
            nullptr,
            WS_POPUP,
            0,
            0,
            lyricsWidth,
            taskbarHeight,
            nullptr,
            nullptr,
            dll_instance,
            this
        );

        if (!hwnd) {
            DWORD error = GetLastError();
            logWin10("create() - CreateWindowEx failed, error: " + std::to_string(error));
            return;
        }

        logWin10("create() - window created successfully");

        // SetParent 到任务栏（创建后设置父窗口）
        SetParent(hwnd, taskbarHwnd);
        logWin10("create() - SetParent completed");

        if (taskListHwnd) {
            updateTaskListPosition();
        }
        updateWindowPosition();

        // 设置分层窗口属性：使用透明色键 RGB(0,0,1)
        SetLayeredWindowAttributes(hwnd, RGB(0, 0, 1), 0, LWA_COLORKEY);
        logWin10("create() - SetLayeredWindowAttributes with colorkey RGB(0,0,1)");
        
        // 显示窗口
        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);
        
        logWin10("create() - window shown, layered window ready");
        
        // 设置定时器：1=更新布局(2秒), 2=歌词滚动(50ms)
        SetTimer(hwnd, 1, 2000, nullptr);
        SetTimer(hwnd, 2, 50, nullptr);

        logWin10("create() - initialization completed");
    }

    auto updateWindowPosition() -> void {
        if (!hwnd || !taskbarHwnd || !trayNotifyHwnd) return;

        RECT taskbarRect{}, trayRect{}, taskListRect{};
        GetWindowRect(taskbarHwnd, &taskbarRect);
        GetWindowRect(trayNotifyHwnd, &trayRect);

        const auto taskbarHeight = static_cast<int>(taskbarRect.bottom - taskbarRect.top);
        POINT trayLeftTop = {trayRect.left, trayRect.top};
        ScreenToClient(taskbarHwnd, &trayLeftTop);

        auto availableLeft = 0;
        if (taskListHwnd && GetWindowRect(taskListHwnd, &taskListRect)) {
            POINT taskListLeftTop = {taskListRect.left, taskListRect.top};
            ScreenToClient(taskbarHwnd, &taskListLeftTop);
            availableLeft = std::max(0, static_cast<int>(taskListLeftTop.x) + taskIconsWidth);
        }

        const auto availableRight = static_cast<int>(trayLeftTop.x);
        const auto availableWidth = std::max(0, availableRight - availableLeft);
        if (availableWidth <= 0) return;

        const auto currentWidth = std::min(lyricsWidth, availableWidth);
        const auto state = snapshotConfig();
        auto xPos = availableRight - currentWidth;
        if (state.window_alignment == TASKBAR_WINDOW_ALIGNMENT_LEFT) {
            xPos = availableLeft;
        } else if (state.window_alignment == TASKBAR_WINDOW_ALIGNMENT_CENTER) {
            xPos = availableLeft + (availableWidth - currentWidth) / 2;
        }
        xPos = std::clamp(
            xPos + state.horizontal_offset,
            0,
            std::max(0, static_cast<int>(taskbarRect.right - taskbarRect.left) - currentWidth));

        logWin10("updateWindowPosition - availableLeft: " + std::to_string(availableLeft) +
                 ", trayLeft: " + std::to_string(availableRight) +
                 ", xPos: " + std::to_string(xPos) +
                 ", width: " + std::to_string(currentWidth) +
                 ", height: " + std::to_string(taskbarHeight));

        SetWindowPos(hwnd, nullptr, xPos, 0, currentWidth, taskbarHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE);

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
            // 检查窗口状态
            BOOL isVisible = IsWindowVisible(hwnd);
            RECT rc;
            GetWindowRect(hwnd, &rc);
            logWin10("refresh() - isVisible=" + std::to_string(isVisible) + 
                     ", pos=(" + std::to_string(rc.left) + "," + std::to_string(rc.top) + 
                     "), size=" + std::to_string(rc.right - rc.left) + "x" + std::to_string(rc.bottom - rc.top));
            
            logWin10("refresh() - calling InvalidateRect");
            // 强制重绘
            InvalidateRect(hwnd, nullptr, TRUE);
            UpdateWindow(hwnd);
        }
    }

    auto isReady() const -> bool {
        return this->hwnd != nullptr;
    }

    auto postRefresh() -> void {
        if (this->hwnd) {
            PostMessage(this->hwnd, WM_APP_REFRESH, 0, 0);
        }
    }

    auto updateLayout() -> void {
        if (this->hwnd) {
            PostMessage(this->hwnd, WM_APP_UPDATE_LAYOUT, 0, 0);
        }
    }

    ~WindowWin10() {
        if (hwnd) {
            DestroyWindow(hwnd);
        }
        if (hOleacc) {
            FreeLibrary(hOleacc);
        }
    }
};
