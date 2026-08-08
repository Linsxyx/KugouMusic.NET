module;

#include <Windows.h>
#include <UIAutomation.h>
#include <wrl/client.h>
#include <thread>
#include <functional>
#include <algorithm>

export module taskbar.Taskbar;

import taskbar.Handler;
import taskbar.Registry;

export class Taskbar {
public:
    typedef std::function<void()> Callback;

private:
    Microsoft::WRL::ComPtr<Handler> handler{};
    Microsoft::WRL::ComPtr<IUIAutomation> automation{};
    Microsoft::WRL::ComPtr<IUIAutomationElement> root{};

    auto createConditionByProperty(PROPERTYID propertyId, const wchar_t *value) const -> Microsoft::WRL::ComPtr<IUIAutomationCondition> {
        VARIANT var{};
        VariantInit(&var);
        var.vt = VT_BSTR;
        var.bstrVal = SysAllocString(value);
        Microsoft::WRL::ComPtr<IUIAutomationCondition> condition{};
        this->automation->CreatePropertyCondition(propertyId, var, &condition);
        SysFreeString(var.bstrVal);
        VariantClear(&var);
        return condition;
    }

public:
    Taskbar() {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED | COINIT_DISABLE_OLE1DDE);
    }

    ~Taskbar() {
        CoUninitialize();
    }

    auto initialize() -> void {
        HRESULT hr = CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_IUIAutomation, &this->automation);
        if (FAILED(hr) || !this->automation) {
            return; // 创建 UI Automation 失败
        }

        HWND taskbarHwnd = Taskbar::getHWND();
        if (!taskbarHwnd) {
            return; // 找不到任务栏窗口
        }

        Microsoft::WRL::ComPtr<IUIAutomationElement> element{};
        hr = this->automation->ElementFromHandle(taskbarHwnd, &element);
        if (FAILED(hr) || !element) {
            return; // 无法从任务栏窗口创建元素
        }

        const auto condition = this->createConditionByProperty(UIA_ClassNamePropertyId, L"Windows.UI.Input.InputSite.WindowClass");
        if (!condition) {
            return; // 创建条件失败
        }

        hr = element->FindFirst(TreeScope_Children, condition.Get(), &this->root);
        if (FAILED(hr)) {
            this->root = nullptr; // 查找失败，确保 root 为空
        }
        // 注意：即使找不到 root 元素，也不算致命错误，继续运行
    }

    auto setListener(const Taskbar::Callback &callback) -> void {
        this->handler = new Handler(callback);
        // 只有在 root 和 automation 都有效时才添加事件监听器
        if (this->root && this->automation) {
            this->automation->AddStructureChangedEventHandler(this->root.Get(), TreeScope_Descendants, nullptr, this->handler.Get());
        }
        // 注册表监听独立运行
        std::thread([callback] {
            Registry::onWatch(callback);
        }).detach();
    }

    auto getRectForTaskbarFrame() const -> RECT {
        RECT rect{};
        if (!this->root || !this->automation) {
            return rect;
        }
        try {
            const auto condition = this->createConditionByProperty(UIA_ClassNamePropertyId, L"Taskbar.TaskbarFrameAutomationPeer");
            Microsoft::WRL::ComPtr<IUIAutomationElement> element{};
            if (condition) {
                this->root->FindFirst(TreeScope_Children, condition.Get(), &element);
            }
            if (element) {
                element->get_CurrentBoundingRectangle(&rect);
            }
        } catch (...) {
            // 捕获任何异常，返回空矩形
        }
        return rect;
    }

    auto getRectForTaskList() const -> RECT {
        RECT rect{
            .left = LONG_MAX,
            .top = LONG_MAX,
            .right = LONG_MIN,
            .bottom = LONG_MIN
        };
        if (!this->root || !this->automation) {
            return rect;
        }
        try {
            const auto conditionID = this->createConditionByProperty(UIA_AutomationIdPropertyId, L"StartButton");
            const auto conditionCN = this->createConditionByProperty(UIA_ClassNamePropertyId, L"Taskbar.TaskListButtonAutomationPeer");
            Microsoft::WRL::ComPtr<IUIAutomationCondition> condition{};
            if (conditionID && conditionCN) {
                this->automation->CreateOrCondition(conditionID.Get(), conditionCN.Get(), &condition);
            }
            if (!condition) {
                return rect;
            }
            Microsoft::WRL::ComPtr<IUIAutomationElementArray> elements{};
            this->root->FindAll(TreeScope_Descendants, condition.Get(), &elements);
            if (!elements) {
                return rect;
            }
            int length = 0;
            elements->get_Length(&length);
            for (int i = 0; i < length; i++) {
                RECT tempRect{};
                Microsoft::WRL::ComPtr<IUIAutomationElement> element{};
                elements->GetElement(i, &element);
                if (element) {
                    element->get_CurrentBoundingRectangle(&tempRect);
                    rect = {
                        .left = std::min(rect.left, tempRect.left),
                        .top = std::min(rect.top, tempRect.top),
                        .right = std::max(rect.right, tempRect.right),
                        .bottom = std::max(rect.bottom, tempRect.bottom)
                    };
                }
            }
        } catch (...) {
            // 捕获任何异常，返回默认矩形
        }
        return rect;
    }

    auto getRectForTrayFrame() const -> RECT {
        RECT rect{
            .left = LONG_MAX,
            .top = LONG_MAX,
            .right = LONG_MIN,
            .bottom = LONG_MIN
        };
        if (!this->root || !this->automation) {
            return rect;
        }
        try {
            const auto condition = this->createConditionByProperty(UIA_AutomationIdPropertyId, L"SystemTrayIcon");
            if (!condition) {
                return rect;
            }
            Microsoft::WRL::ComPtr<IUIAutomationElementArray> elements{};
            this->root->FindAll(TreeScope_Children, condition.Get(), &elements);
            if (!elements) {
                return rect;
            }
            int length = 0;
            elements->get_Length(&length);
            for (int i = 0; i < length; i++) {
                RECT tempRect{};
                Microsoft::WRL::ComPtr<IUIAutomationElement> element{};
                elements->GetElement(i, &element);
                if (element) {
                    element->get_CurrentBoundingRectangle(&tempRect);
                    rect = {
                        .left = std::min(rect.left, tempRect.left),
                        .top = std::min(rect.top, tempRect.top),
                        .right = std::max(rect.right, tempRect.right),
                        .bottom = std::max(rect.bottom, tempRect.bottom)
                    };
                }
            }
        } catch (...) {
            // 捕获任何异常，返回默认矩形
        }
        return rect;
    }

    auto getRectForWidgetsButton() const -> RECT {
        RECT rect{};
        if (!this->root || !this->automation) {
            return rect;
        }
        try {
            if (Registry::isWidgetsEnabled()) {
                const auto condition = this->createConditionByProperty(UIA_AutomationIdPropertyId, L"WidgetsButton");
                if (!condition) {
                    return rect;
                }
                Microsoft::WRL::ComPtr<IUIAutomationElement> element{};
                this->root->FindFirst(TreeScope_Descendants, condition.Get(), &element);
                if (element) {
                    element->get_CurrentBoundingRectangle(&rect);
                }
            }
        } catch (...) {
            // 捕获任何异常，返回空矩形
        }
        return rect;
    }

    static auto getHWND() -> HWND {
        return FindWindow(L"Shell_TrayWnd", nullptr);
    }
};
