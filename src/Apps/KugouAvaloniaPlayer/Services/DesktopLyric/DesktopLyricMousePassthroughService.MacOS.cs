#if KUGOU_MACOS
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed class DesktopLyricMousePassthroughService : IDesktopLyricMousePassthroughService
{
    private static readonly IntPtr SelSetIgnoresMouseEvents = sel_registerName("setIgnoresMouseEvents:");

    public bool IsSupported => true;
    public bool SupportsSelectiveHitTesting => false;

    public void Apply(Window window, DesktopLyricHitTestLayout layout)
    {
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null) return;

        var nsWindow = platformHandle.Handle;
        if (nsWindow == IntPtr.Zero) return;

        objc_msgSend_bool(nsWindow, SelSetIgnoresMouseEvents, layout.Mode != DesktopLyricHitTestMode.FullWindow);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg1);
}
#endif
