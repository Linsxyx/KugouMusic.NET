#if KUGOU_WINDOWS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed class DesktopLyricMousePassthroughService : IDesktopLyricMousePassthroughService
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExLayered = 0x80000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private readonly Dictionary<IntPtr, IntPtr> _originalExStyles = new();

    public bool IsSupported => true;
    public bool SupportsSelectiveHitTesting => false;

    public void Apply(Window window, DesktopLyricHitTestLayout layout)
    {
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null) return;

        var hwnd = platformHandle.Handle;
        var currentStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        var enabled = layout.Mode != DesktopLyricHitTestMode.FullWindow;

        if (enabled)
        {
            if (!_originalExStyles.ContainsKey(hwnd))
                _originalExStyles[hwnd] = currentStyle;

            var nextStyle = new IntPtr(currentStyle.ToInt64() | WsExLayered | WsExTransparent);
            if (nextStyle == currentStyle) return;

            SetWindowLongPtr(hwnd, GwlExStyle, nextStyle);
            RefreshWindowStyle(hwnd);
            return;
        }

        if (_originalExStyles.Remove(hwnd, out var originalStyle))
        {
            if (originalStyle == currentStyle) return;

            SetWindowLongPtr(hwnd, GwlExStyle, originalStyle);
            RefreshWindowStyle(hwnd);
            return;
        }

        var fallbackStyle = new IntPtr(currentStyle.ToInt64() & ~WsExTransparent);
        if (fallbackStyle == currentStyle) return;

        SetWindowLongPtr(hwnd, GwlExStyle, fallbackStyle);
        RefreshWindowStyle(hwnd);
    }

    private static void RefreshWindowStyle(IntPtr hwnd)
    {
        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong)
    {
        if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, newLong);
        return new IntPtr(SetWindowLong32(hWnd, nIndex, newLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
#endif
