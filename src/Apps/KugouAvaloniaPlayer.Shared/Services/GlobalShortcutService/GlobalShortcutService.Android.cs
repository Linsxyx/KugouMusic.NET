#if KUGOU_ANDROID
using Avalonia.Controls;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services.GlobalShortcutService;

// Android has no global hotkeys; media keys arrive through the media session instead.
public sealed partial class GlobalShortcutService
{
    private partial bool GetPlatformSupport() => false;

    private partial void InitializePlatform(Window mainWindow)
    {
    }

    private partial bool TryRegisterPlatformShortcut(GlobalShortcutAction action, GlobalShortcutGesture gesture,
        out string? errorMessage, out GlobalShortcutRegistrationFailureKind failureKind)
    {
        errorMessage = "Android 不支持全局快捷键。";
        failureKind = GlobalShortcutRegistrationFailureKind.UnsupportedPlatform;
        return false;
    }

    private partial void UnregisterPlatformShortcut(GlobalShortcutAction action)
    {
    }
}
#endif
