#if KUGOU_LINUX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.GlobalShortcutService;

public sealed partial class GlobalShortcutService
{
    private const int X11KeyPress = 2;
    private const int X11GrabModeAsync = 1;
    private const int X11BadAccess = 10;
    private const uint X11ShiftMask = 1;
    private const uint X11LockMask = 1 << 1;
    private const uint X11ControlMask = 1 << 2;
    private const uint X11Mod1Mask = 1 << 3;
    private const uint X11Mod2Mask = 1 << 4;
    private const uint X11Mod4Mask = 1 << 6;

    private static readonly uint[] X11IgnoredModifiers =
    [
        0,
        X11LockMask,
        X11Mod2Mask,
        X11LockMask | X11Mod2Mask
    ];

    private static readonly object X11ErrorSyncRoot = new();
    private static readonly XErrorHandler s_x11ErrorHandler = X11ErrorHandler;
    private static int s_x11LastErrorCode;
    private static bool s_x11TrapErrors;
    private readonly Dictionary<GlobalShortcutAction, LinuxShortcutRegistration> _linuxRegistrations = new();

    private readonly object _x11SyncRoot = new();
    private IntPtr _x11Display;
    private Thread? _x11EventThread;
    private IntPtr _x11RootWindow;
    private volatile bool _x11StopRequested;

    private partial bool GetPlatformSupport()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
    }

    private partial void InitializePlatform(Window mainWindow)
    {
        if (_x11Display != IntPtr.Zero)
            return;

        var displayName = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            _logger.LogInformation("未检测到 X11 DISPLAY，Linux 全局快捷键不可用。");
            return;
        }

        XInitThreads();
        _x11Display = XOpenDisplay(IntPtr.Zero);
        if (_x11Display == IntPtr.Zero)
        {
            _logger.LogWarning("无法连接到 X11 显示服务器，Linux 全局快捷键不可用。");
            return;
        }

        _x11RootWindow = XDefaultRootWindow(_x11Display);
        XSetErrorHandler(s_x11ErrorHandler);

        _x11StopRequested = false;
        _x11EventThread = new Thread(RunLinuxEventLoop)
        {
            Name = "KugouGlobalShortcutX11",
            IsBackground = true
        };
        _x11EventThread.Start();
    }

    private partial bool TryRegisterPlatformShortcut(GlobalShortcutAction action, GlobalShortcutGesture gesture,
        out string? errorMessage)
    {
        errorMessage = null;
        if (_x11Display == IntPtr.Zero || _x11RootWindow == IntPtr.Zero)
        {
            errorMessage = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
                ? "当前会话不是 X11，暂不支持全局快捷键。"
                : "无法连接到 X11，暂不支持全局快捷键。";
            return false;
        }

        var keysym = ToX11Keysym(gesture.Key);
        if (keysym == IntPtr.Zero)
        {
            errorMessage = "该按键暂不支持 Linux 全局快捷键。";
            return false;
        }

        byte keycode;
        lock (_x11SyncRoot)
        {
            keycode = XKeysymToKeycode(_x11Display, keysym);
        }

        if (keycode == 0)
        {
            errorMessage = "无法解析该按键在当前键盘布局下的键位。";
            return false;
        }

        var baseModifiers = ToX11Modifiers(gesture.Modifiers);
        if (!TryGrabX11Key(keycode, baseModifiers, out errorMessage))
            return false;

        _linuxRegistrations[action] = new LinuxShortcutRegistration(keycode, baseModifiers);
        return true;
    }

    private partial void UnregisterPlatformShortcut(GlobalShortcutAction action)
    {
        if (_x11Display == IntPtr.Zero || !_linuxRegistrations.Remove(action, out var registration))
            return;

        lock (_x11SyncRoot)
        {
            foreach (var ignored in X11IgnoredModifiers)
                XUngrabKey(_x11Display, registration.KeyCode, registration.BaseModifiers | ignored, _x11RootWindow);
            XFlush(_x11Display);
        }
    }

    private bool TryGrabX11Key(byte keycode, uint baseModifiers, out string? errorMessage)
    {
        errorMessage = null;
        lock (_x11SyncRoot)
        {
            lock (X11ErrorSyncRoot)
            {
                s_x11LastErrorCode = 0;
                s_x11TrapErrors = true;
            }

            foreach (var ignored in X11IgnoredModifiers)
                XGrabKey(_x11Display, keycode, baseModifiers | ignored, _x11RootWindow, true, X11GrabModeAsync,
                    X11GrabModeAsync);

            XSync(_x11Display, false);

            int errorCode;
            lock (X11ErrorSyncRoot)
            {
                errorCode = s_x11LastErrorCode;
                s_x11LastErrorCode = 0;
                s_x11TrapErrors = false;
            }

            if (errorCode == 0)
                return true;

            foreach (var ignored in X11IgnoredModifiers)
                XUngrabKey(_x11Display, keycode, baseModifiers | ignored, _x11RootWindow);
            XFlush(_x11Display);

            errorMessage = errorCode == X11BadAccess
                ? "该组合已被系统或其他应用占用。"
                : $"X11 注册失败 ({errorCode})";
            return false;
        }
    }

    private void RunLinuxEventLoop()
    {
        while (!_x11StopRequested)
        {
            var xEvent = default(XEvent);
            var hasEvent = false;

            lock (_x11SyncRoot)
            {
                if (_x11Display != IntPtr.Zero && XPending(_x11Display) > 0)
                {
                    XNextEvent(_x11Display, out xEvent);
                    hasEvent = true;
                }
            }

            if (!hasEvent)
            {
                Thread.Sleep(20);
                continue;
            }

            var keyEvent = xEvent.KeyEvent;
            if (keyEvent.type != X11KeyPress)
                continue;

            var normalizedState = keyEvent.state & ~(X11LockMask | X11Mod2Mask);
            foreach (var pair in _linuxRegistrations)
            {
                if (pair.Value.KeyCode != keyEvent.keycode)
                    continue;

                if (pair.Value.BaseModifiers != normalizedState)
                    continue;

                DispatchAction(pair.Key);
                break;
            }
        }
    }

    private static uint ToX11Modifiers(GlobalShortcutModifiers modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Shift))
            native |= X11ShiftMask;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Control))
            native |= X11ControlMask;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Alt))
            native |= X11Mod1Mask;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Meta))
            native |= X11Mod4Mask;
        return native;
    }

    private static IntPtr ToX11Keysym(Key key)
    {
        return key switch
        {
            Key.Space => new IntPtr(0x20),
            Key.Left => new IntPtr(0xFF51),
            Key.Up => new IntPtr(0xFF52),
            Key.Right => new IntPtr(0xFF53),
            Key.Down => new IntPtr(0xFF54),
            >= Key.A and <= Key.Z => new IntPtr('a' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => new IntPtr('0' + (key - Key.D0)),
            _ => IntPtr.Zero
        };
    }

    private static int X11ErrorHandler(IntPtr display, IntPtr errorEventPtr)
    {
        var errorEvent = Marshal.PtrToStructure<XErrorEvent>(errorEventPtr);
        lock (X11ErrorSyncRoot)
        {
            if (s_x11TrapErrors)
                s_x11LastErrorCode = errorEvent.error_code;
        }

        return 0;
    }

    [DllImport("libX11.so.6")]
    private static extern int XInitThreads();

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display_name);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern void XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grab_window,
        bool owner_events,
        int pointer_mode, int keyboard_mode);

    [DllImport("libX11.so.6")]
    private static extern void XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grab_window);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern void XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport("libX11.so.6")]
    private static extern void XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern void XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    private sealed record LinuxShortcutRegistration(byte KeyCode, uint BaseModifiers);

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int x_root;
        public int y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int Type;

        [FieldOffset(0)] public XKeyEvent KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public ulong resourceid;
        public ulong serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEventPtr);
}
#endif