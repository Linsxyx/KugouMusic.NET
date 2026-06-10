#if KUGOU_WINDOWS
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed partial class DesktopLyricWindowChromeService : IDesktopLyricWindowChromeService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public void HideFromWindowSwitcher(Window window)
    {
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return;

        var hwnd = platformHandle.Handle;
        var currentStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        var nextStyle = new IntPtr((currentStyle.ToInt64() | WsExToolWindow) & ~WsExAppWindow);
        if (nextStyle == currentStyle) return;

        SetWindowLongPtr(hwnd, GwlExStyle, nextStyle);
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
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, newLong.ToInt32()));
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
#endif
