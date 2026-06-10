#if KUGOU_MACOS
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed partial class DesktopLyricWindowChromeService : IDesktopLyricWindowChromeService
{
    private const ulong NSWindowCollectionBehaviorTransient = 1UL << 3;
    private const ulong NSWindowCollectionBehaviorIgnoresCycle = 1UL << 6;

    private static readonly IntPtr SelCollectionBehavior = sel_registerName("collectionBehavior");
    private static readonly IntPtr SelSetCollectionBehavior = sel_registerName("setCollectionBehavior:");

    public void HideFromWindowSwitcher(Window window)
    {
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return;

        var nsWindow = platformHandle.Handle;
        var currentBehavior = objc_msgSend_UIntPtr(nsWindow, SelCollectionBehavior).ToUInt64();
        var nextBehavior = new UIntPtr(
            currentBehavior | NSWindowCollectionBehaviorTransient | NSWindowCollectionBehaviorIgnoresCycle);

        objc_msgSend_UIntPtr_arg(nsWindow, SelSetCollectionBehavior, nextBehavior);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial UIntPtr objc_msgSend_UIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_UIntPtr_arg(IntPtr receiver, IntPtr selector, UIntPtr arg1);
}
#endif
