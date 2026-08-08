module;

#include <Windows.h>
#include <UIAutomation.h>
#include <atomic>
#include <functional>

export module taskbar.Handler;

export class Handler : public IUIAutomationStructureChangedEventHandler {
public:
    typedef std::function<void()> Callback;

private:
    std::atomic<ULONG> refCount{1};
    Handler::Callback callback{};

public:
    Handler(const Handler::Callback &callback) {
        this->callback = callback;
    }

    auto AddRef() -> ULONG override {
        return ++this->refCount;
    }

    auto Release() -> ULONG override {
        const auto count = --this->refCount;
        if (count == 0) {
            delete this;
        }
        return count;
    }

    auto QueryInterface(const IID &riid, void **ppvObject) -> HRESULT override {
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IUIAutomationStructureChangedEventHandler)) {
            *ppvObject = static_cast<IUIAutomationStructureChangedEventHandler *>(this);
            AddRef();
            return S_OK;
        }
        *ppvObject = nullptr;
        return E_NOINTERFACE;
    }

    auto HandleStructureChangedEvent(IUIAutomationElement *sender, StructureChangeType changeType, SAFEARRAY *runtimeId) -> HRESULT override {
        if (this->callback) {
            this->callback();
        }
        return S_OK;
    }
};
