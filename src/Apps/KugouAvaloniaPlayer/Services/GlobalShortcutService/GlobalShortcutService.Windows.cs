#if KUGOU_WINDOWS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services.GlobalShortcutService;

public sealed partial class GlobalShortcutService
{
    private const uint WmHotKey = 0x0312;
    private readonly Dictionary<GlobalShortcutAction, int> _registeredIds = new();
    private Win32Properties.CustomWndProcHookCallback? _wndProcHook;

    private partial bool GetPlatformSupport()
    {
        return true;
    }

    private partial void InitializePlatform(Window mainWindow)
    {
        if (_wndProcHook != null)
            return;

        _wndProcHook = WindowProc;
        Win32Properties.AddWndProcHookCallback(mainWindow, _wndProcHook);
    }

    private partial bool TryRegisterPlatformShortcut(GlobalShortcutAction action, GlobalShortcutGesture gesture, out string? errorMessage)
    {
        errorMessage = null;
        var handle = _mainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            errorMessage = "主窗口尚未准备好。";
            return false;
        }

        var hotKeyId = 10_000 + (int)action;
        if (!RegisterHotKey(handle, hotKeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key)))
        {
            var errorCode = Marshal.GetLastWin32Error();
            errorMessage = errorCode == 1409
                ? "该组合已被系统或其他应用占用。"
                : $"注册失败 ({errorCode})";
            return false;
        }

        _registeredIds[action] = hotKeyId;
        return true;
    }

    private partial void UnregisterPlatformShortcut(GlobalShortcutAction action)
    {
        if (!_registeredIds.Remove(action, out var hotKeyId))
            return;

        var handle = _mainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            UnregisterHotKey(handle, hotKeyId);
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey)
            return IntPtr.Zero;

        var hotKeyId = wParam.ToInt32();
        foreach (var pair in _registeredIds)
        {
            if (pair.Value != hotKeyId)
                continue;

            handled = true;
            DispatchAction(pair.Key);
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(GlobalShortcutModifiers modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Alt))
            native |= 0x0001;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Control))
            native |= 0x0002;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Shift))
            native |= 0x0004;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Meta))
            native |= 0x0008;
        return native;
    }

    private static uint ToVirtualKey(Avalonia.Input.Key key)
    {
        return key switch
        {
            Avalonia.Input.Key.Space => 0x20,
            Avalonia.Input.Key.Left => 0x25,
            Avalonia.Input.Key.Up => 0x26,
            Avalonia.Input.Key.Right => 0x27,
            Avalonia.Input.Key.Down => 0x28,
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => (uint)('A' + (key - Avalonia.Input.Key.A)),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => (uint)('0' + (key - Avalonia.Input.Key.D0)),
            _ => (uint)key
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
#endif