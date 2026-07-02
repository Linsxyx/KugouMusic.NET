#define COBJMACROS

#include <windows.h>
#include <commctrl.h>
#include <shobjidl_core.h>
#include <stdint.h>
#include <stdlib.h>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "user32.lib")

#define KG_BUTTON_PREVIOUS 1001u
#define KG_BUTTON_PLAYPAUSE 1002u
#define KG_BUTTON_NEXT 1003u
#define KG_BUTTON_LIKE 1004u

typedef void (__stdcall *KgTaskbarButtonClickCallback)(uint32_t buttonId, void* userData);

typedef struct KgTaskbarToolbar {
    HWND hwnd;
    WNDPROC originalWndProc;
    ITaskbarList3* taskbar;
    HICON previousIcon;
    HICON playIcon;
    HICON pauseIcon;
    HICON nextIcon;
    HICON heartGreyIcon;
    HICON heartRedIcon;
    uint32_t taskbarButtonCreatedMessage;
    KgTaskbarButtonClickCallback callback;
    void* userData;
    int coInitialized;
    int isPlaying;
    int isLiked;
    int buttonsAdded;
} KgTaskbarToolbar;

static const wchar_t* KG_TASKBAR_PROP_NAME = L"KugouTaskbarToolbar.Instance";

static HICON KgLoadIconFromFile(const wchar_t* path)
{
    if (path == NULL || path[0] == L'\0')
        return NULL;

    return (HICON)LoadImageW(NULL, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
}

static void KgFillThumbButton(THUMBBUTTON* button, uint32_t id, HICON icon, const wchar_t* tooltip, THUMBBUTTONFLAGS flags)
{
    ZeroMemory(button, sizeof(*button));
    button->dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS;
    button->iId = id;
    button->hIcon = icon;
    button->dwFlags = flags;
    if (tooltip != NULL)
        lstrcpynW(button->szTip, tooltip, ARRAYSIZE(button->szTip));
}

static HICON KgGetLikeIcon(const KgTaskbarToolbar* toolbar)
{
    return toolbar->isLiked ? toolbar->heartRedIcon : toolbar->heartGreyIcon;
}

static const wchar_t* KG_TOOLTIP_PREVIOUS = L"\x4E0A\x4E00\x9996";
static const wchar_t* KG_TOOLTIP_PLAY = L"\x64AD\x653E";
static const wchar_t* KG_TOOLTIP_PAUSE = L"\x6682\x505C";
static const wchar_t* KG_TOOLTIP_NEXT = L"\x4E0B\x4E00\x9996";
static const wchar_t* KG_TOOLTIP_LIKE = L"\x6211\x559C\x6B22";
static const wchar_t* KG_TOOLTIP_LIKED = L"\x53D6\x6D88\x559C\x6B22";

static const wchar_t* KgGetLikeTooltip(const KgTaskbarToolbar* toolbar)
{
    return toolbar->isLiked ? KG_TOOLTIP_LIKED : KG_TOOLTIP_LIKE;
}

static HRESULT KgAddButtons(KgTaskbarToolbar* toolbar)
{
    THUMBBUTTON buttons[4];
    if (toolbar == NULL || toolbar->taskbar == NULL || toolbar->hwnd == NULL)
        return E_INVALIDARG;

    KgFillThumbButton(&buttons[0], KG_BUTTON_PREVIOUS, toolbar->previousIcon, KG_TOOLTIP_PREVIOUS, THBF_ENABLED);
    KgFillThumbButton(&buttons[1], KG_BUTTON_PLAYPAUSE, toolbar->isPlaying ? toolbar->pauseIcon : toolbar->playIcon, toolbar->isPlaying ? KG_TOOLTIP_PAUSE : KG_TOOLTIP_PLAY, THBF_ENABLED);
    KgFillThumbButton(&buttons[2], KG_BUTTON_NEXT, toolbar->nextIcon, KG_TOOLTIP_NEXT, THBF_ENABLED);
    KgFillThumbButton(&buttons[3], KG_BUTTON_LIKE, KgGetLikeIcon(toolbar), KgGetLikeTooltip(toolbar), THBF_DISABLED);

    return ITaskbarList3_ThumbBarAddButtons(toolbar->taskbar, toolbar->hwnd, ARRAYSIZE(buttons), buttons);
}

static HRESULT KgUpdatePlayPauseCore(KgTaskbarToolbar* toolbar)
{
    THUMBBUTTON button;
    if (toolbar == NULL || toolbar->taskbar == NULL || toolbar->hwnd == NULL)
        return E_INVALIDARG;

    KgFillThumbButton(&button, KG_BUTTON_PLAYPAUSE, toolbar->isPlaying ? toolbar->pauseIcon : toolbar->playIcon, toolbar->isPlaying ? KG_TOOLTIP_PAUSE : KG_TOOLTIP_PLAY, THBF_ENABLED);
    return ITaskbarList3_ThumbBarUpdateButtons(toolbar->taskbar, toolbar->hwnd, 1, &button);
}

static HRESULT KgUpdateEnabledCore(KgTaskbarToolbar* toolbar, BOOL previousEnabled, BOOL playPauseEnabled, BOOL nextEnabled)
{
    THUMBBUTTON buttons[4];
    if (toolbar == NULL || toolbar->taskbar == NULL || toolbar->hwnd == NULL)
        return E_INVALIDARG;

    KgFillThumbButton(&buttons[0], KG_BUTTON_PREVIOUS, toolbar->previousIcon, KG_TOOLTIP_PREVIOUS, previousEnabled ? THBF_ENABLED : THBF_DISABLED);
    KgFillThumbButton(&buttons[1], KG_BUTTON_PLAYPAUSE, toolbar->isPlaying ? toolbar->pauseIcon : toolbar->playIcon, toolbar->isPlaying ? KG_TOOLTIP_PAUSE : KG_TOOLTIP_PLAY, playPauseEnabled ? THBF_ENABLED : THBF_DISABLED);
    KgFillThumbButton(&buttons[2], KG_BUTTON_NEXT, toolbar->nextIcon, KG_TOOLTIP_NEXT, nextEnabled ? THBF_ENABLED : THBF_DISABLED);
    KgFillThumbButton(&buttons[3], KG_BUTTON_LIKE, KgGetLikeIcon(toolbar), KgGetLikeTooltip(toolbar), THBF_DISABLED);

    return ITaskbarList3_ThumbBarUpdateButtons(toolbar->taskbar, toolbar->hwnd, ARRAYSIZE(buttons), buttons);
}

static HRESULT KgUpdateLikeCore(KgTaskbarToolbar* toolbar, BOOL enabled)
{
    THUMBBUTTON button;
    if (toolbar == NULL || toolbar->taskbar == NULL || toolbar->hwnd == NULL)
        return E_INVALIDARG;

    KgFillThumbButton(&button, KG_BUTTON_LIKE, KgGetLikeIcon(toolbar), KgGetLikeTooltip(toolbar), enabled ? THBF_ENABLED : THBF_DISABLED);
    return ITaskbarList3_ThumbBarUpdateButtons(toolbar->taskbar, toolbar->hwnd, 1, &button);
}

static LRESULT CALLBACK KgTaskbarToolbar_WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    KgTaskbarToolbar* toolbar = (KgTaskbarToolbar*)GetPropW(hwnd, KG_TASKBAR_PROP_NAME);
    if (toolbar == NULL)
        return DefWindowProcW(hwnd, msg, wParam, lParam);

    if (msg == toolbar->taskbarButtonCreatedMessage)
    {
        if (!toolbar->buttonsAdded)
        {
            if (SUCCEEDED(KgAddButtons(toolbar)))
                toolbar->buttonsAdded = 1;
        }
    }
    else if (msg == WM_COMMAND && HIWORD(wParam) == THBN_CLICKED)
    {
        uint32_t buttonId = LOWORD(wParam);
        if ((buttonId == KG_BUTTON_PREVIOUS || buttonId == KG_BUTTON_PLAYPAUSE || buttonId == KG_BUTTON_NEXT || buttonId == KG_BUTTON_LIKE) && toolbar->callback != NULL)
            toolbar->callback(buttonId, toolbar->userData);
    }
    else if (msg == WM_NCDESTROY)
    {
        if (toolbar->originalWndProc != NULL)
            SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)toolbar->originalWndProc);
        RemovePropW(hwnd, KG_TASKBAR_PROP_NAME);
    }

    if (toolbar->originalWndProc != NULL)
        return CallWindowProcW(toolbar->originalWndProc, hwnd, msg, wParam, lParam);

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

__declspec(dllexport) KgTaskbarToolbar* __stdcall KgTaskbarToolbar_Create(
    HWND hwnd,
    const wchar_t* previousIconPath,
    const wchar_t* playIconPath,
    const wchar_t* pauseIconPath,
    const wchar_t* nextIconPath,
    const wchar_t* heartGreyIconPath,
    const wchar_t* heartRedIconPath,
    KgTaskbarButtonClickCallback callback,
    void* userData)
{
    HRESULT hr;
    KgTaskbarToolbar* toolbar;

    if (hwnd == NULL)
        return NULL;

    toolbar = (KgTaskbarToolbar*)calloc(1, sizeof(KgTaskbarToolbar));
    if (toolbar == NULL)
        return NULL;

    toolbar->hwnd = hwnd;
    toolbar->callback = callback;
    toolbar->userData = userData;
    toolbar->taskbarButtonCreatedMessage = RegisterWindowMessageW(L"TaskbarButtonCreated");

    toolbar->previousIcon = KgLoadIconFromFile(previousIconPath);
    toolbar->playIcon = KgLoadIconFromFile(playIconPath);
    toolbar->pauseIcon = KgLoadIconFromFile(pauseIconPath);
    toolbar->nextIcon = KgLoadIconFromFile(nextIconPath);
    toolbar->heartGreyIcon = KgLoadIconFromFile(heartGreyIconPath);
    toolbar->heartRedIcon = KgLoadIconFromFile(heartRedIconPath);

    if (toolbar->previousIcon == NULL || toolbar->playIcon == NULL || toolbar->pauseIcon == NULL || toolbar->nextIcon == NULL || toolbar->heartGreyIcon == NULL || toolbar->heartRedIcon == NULL)
        goto fail;

    hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(hr))
        toolbar->coInitialized = 1;
    else if (hr != RPC_E_CHANGED_MODE)
        goto fail;

    hr = CoCreateInstance(&CLSID_TaskbarList, NULL, CLSCTX_INPROC_SERVER, &IID_ITaskbarList3, (void**)&toolbar->taskbar);
    if (FAILED(hr) || toolbar->taskbar == NULL)
        goto fail;

    hr = ITaskbarList3_HrInit(toolbar->taskbar);
    if (FAILED(hr))
        goto fail;

    SetPropW(hwnd, KG_TASKBAR_PROP_NAME, (HANDLE)toolbar);
    toolbar->originalWndProc = (WNDPROC)SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)KgTaskbarToolbar_WndProc);
    if (toolbar->originalWndProc == NULL && GetLastError() != 0)
        goto fail;

    if (SUCCEEDED(KgAddButtons(toolbar)))
        toolbar->buttonsAdded = 1;

    return toolbar;

fail:
    if (toolbar->taskbar != NULL)
        ITaskbarList3_Release(toolbar->taskbar);
    if (toolbar->previousIcon != NULL)
        DestroyIcon(toolbar->previousIcon);
    if (toolbar->playIcon != NULL)
        DestroyIcon(toolbar->playIcon);
    if (toolbar->pauseIcon != NULL)
        DestroyIcon(toolbar->pauseIcon);
    if (toolbar->nextIcon != NULL)
        DestroyIcon(toolbar->nextIcon);
    if (toolbar->heartGreyIcon != NULL)
        DestroyIcon(toolbar->heartGreyIcon);
    if (toolbar->heartRedIcon != NULL)
        DestroyIcon(toolbar->heartRedIcon);
    if (toolbar->coInitialized)
        CoUninitialize();
    free(toolbar);
    return NULL;
}

__declspec(dllexport) void __stdcall KgTaskbarToolbar_UpdatePlayPause(KgTaskbarToolbar* toolbar, BOOL isPlaying)
{
    if (toolbar == NULL)
        return;

    toolbar->isPlaying = isPlaying ? 1 : 0;
    if (toolbar->buttonsAdded)
        KgUpdatePlayPauseCore(toolbar);
}

__declspec(dllexport) void __stdcall KgTaskbarToolbar_UpdateEnabled(KgTaskbarToolbar* toolbar, BOOL previousEnabled, BOOL playPauseEnabled, BOOL nextEnabled, BOOL likeEnabled)
{
    if (toolbar == NULL || !toolbar->buttonsAdded)
        return;

    KgUpdateEnabledCore(toolbar, previousEnabled, playPauseEnabled, nextEnabled);
    KgUpdateLikeCore(toolbar, likeEnabled);
}

__declspec(dllexport) void __stdcall KgTaskbarToolbar_UpdateLike(KgTaskbarToolbar* toolbar, BOOL isLiked, BOOL enabled)
{
    if (toolbar == NULL)
        return;

    toolbar->isLiked = isLiked ? 1 : 0;
    if (toolbar->buttonsAdded)
        KgUpdateLikeCore(toolbar, enabled);
}

__declspec(dllexport) void __stdcall KgTaskbarToolbar_Destroy(KgTaskbarToolbar* toolbar)
{
    if (toolbar == NULL)
        return;

    if (toolbar->hwnd != NULL)
    {
        if ((KgTaskbarToolbar*)GetPropW(toolbar->hwnd, KG_TASKBAR_PROP_NAME) == toolbar)
            RemovePropW(toolbar->hwnd, KG_TASKBAR_PROP_NAME);

        if (toolbar->originalWndProc != NULL)
            SetWindowLongPtrW(toolbar->hwnd, GWLP_WNDPROC, (LONG_PTR)toolbar->originalWndProc);
    }

    if (toolbar->taskbar != NULL)
        ITaskbarList3_Release(toolbar->taskbar);
    if (toolbar->previousIcon != NULL)
        DestroyIcon(toolbar->previousIcon);
    if (toolbar->playIcon != NULL)
        DestroyIcon(toolbar->playIcon);
    if (toolbar->pauseIcon != NULL)
        DestroyIcon(toolbar->pauseIcon);
    if (toolbar->nextIcon != NULL)
        DestroyIcon(toolbar->nextIcon);
    if (toolbar->heartGreyIcon != NULL)
        DestroyIcon(toolbar->heartGreyIcon);
    if (toolbar->heartRedIcon != NULL)
        DestroyIcon(toolbar->heartRedIcon);
    if (toolbar->coInitialized)
        CoUninitialize();

    free(toolbar);
}
