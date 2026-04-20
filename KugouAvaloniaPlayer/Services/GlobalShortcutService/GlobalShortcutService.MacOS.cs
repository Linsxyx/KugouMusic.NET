#if KUGOU_MACOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.GlobalShortcutService;

public sealed partial class GlobalShortcutService
{
    private const uint CarbonEventClassKeyboard = 0x6B657962;
    private const uint CarbonEventHotKeyPressed = 6;
    private const uint CarbonEventParamDirectObject = 0x2D2D2D2D;
    private const uint CarbonTypeEventHotKeyId = 0x686B6964;
    private const uint CarbonCmdKey = 1 << 8;
    private const uint CarbonShiftKey = 1 << 9;
    private const uint CarbonOptionKey = 1 << 11;
    private const uint CarbonControlKey = 1 << 12;

    private static GlobalShortcutService? s_currentInstance;

    private readonly Dictionary<GlobalShortcutAction, IntPtr> _macRegistrations = new();
    private EventHandlerDelegate? _macEventHandler;
    private IntPtr _macEventHandlerRef;

    private partial bool GetPlatformSupport()
    {
        return true;
    }

    private partial void InitializePlatform(Window mainWindow)
    {
        if (_macEventHandlerRef != IntPtr.Zero)
            return;

        s_currentInstance = this;
        _macEventHandler = HandleMacHotKeyEvent;
        var eventType = new EventTypeSpec
        {
            eventClass = CarbonEventClassKeyboard,
            eventKind = CarbonEventHotKeyPressed
        };

        var status = InstallEventHandler(
            GetApplicationEventTarget(),
            _macEventHandler,
            1,
            ref eventType,
            IntPtr.Zero,
            out _macEventHandlerRef);

        if (status != 0)
        {
            _logger.LogWarning("macOS 全局快捷键事件处理器安装失败，状态码 {Status}", status);
            _macEventHandlerRef = IntPtr.Zero;
        }
    }

    private partial bool TryRegisterPlatformShortcut(GlobalShortcutAction action, GlobalShortcutGesture gesture, out string? errorMessage)
    {
        errorMessage = null;
        if (_macEventHandlerRef == IntPtr.Zero)
        {
            errorMessage = "macOS 全局快捷键初始化失败。";
            return false;
        }

        var keyCode = ToMacVirtualKey(gesture.Key);
        if (keyCode is null)
        {
            errorMessage = "该按键暂不支持 macOS 全局快捷键。";
            return false;
        }

        var hotKeyId = new EventHotKeyId
        {
            signature = 0x4B41484B,
            id = 20_000u + (uint)action
        };

        var status = RegisterEventHotKey(
            keyCode.Value,
            ToMacModifiers(gesture.Modifiers),
            ref hotKeyId,
            GetApplicationEventTarget(),
            0,
            out var hotKeyRef);

        if (status != 0)
        {
            errorMessage = status == -9878
                ? "该组合已被系统或其他应用占用。"
                : $"注册失败 ({status})";
            return false;
        }

        _macRegistrations[action] = hotKeyRef;
        return true;
    }

    private partial void UnregisterPlatformShortcut(GlobalShortcutAction action)
    {
        if (!_macRegistrations.Remove(action, out var hotKeyRef) || hotKeyRef == IntPtr.Zero)
            return;

        UnregisterEventHotKey(hotKeyRef);
    }

    private static int HandleMacHotKeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        var status = GetEventParameter(
            theEvent,
            CarbonEventParamDirectObject,
            CarbonTypeEventHotKeyId,
            IntPtr.Zero,
            (uint)Marshal.SizeOf<EventHotKeyId>(),
            out _,
            out var hotKeyId);

        if (status != 0 || s_currentInstance == null)
            return status;

        foreach (var pair in s_currentInstance._macRegistrations)
        {
            if (hotKeyId.id != 20_000u + (uint)pair.Key)
                continue;

            s_currentInstance.DispatchAction(pair.Key);
            return 0;
        }

        return 0;
    }

    private static uint ToMacModifiers(GlobalShortcutModifiers modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Meta))
            native |= CarbonCmdKey;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Shift))
            native |= CarbonShiftKey;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Alt))
            native |= CarbonOptionKey;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Control))
            native |= CarbonControlKey;
        return native;
    }

    private static uint? ToMacVirtualKey(Avalonia.Input.Key key)
    {
        return key switch
        {
            Avalonia.Input.Key.Space => 49,
            Avalonia.Input.Key.Left => 123,
            Avalonia.Input.Key.Right => 124,
            Avalonia.Input.Key.Down => 125,
            Avalonia.Input.Key.Up => 126,
            Avalonia.Input.Key.A => 0,
            Avalonia.Input.Key.B => 11,
            Avalonia.Input.Key.C => 8,
            Avalonia.Input.Key.D => 2,
            Avalonia.Input.Key.E => 14,
            Avalonia.Input.Key.F => 3,
            Avalonia.Input.Key.G => 5,
            Avalonia.Input.Key.H => 4,
            Avalonia.Input.Key.I => 34,
            Avalonia.Input.Key.J => 38,
            Avalonia.Input.Key.K => 40,
            Avalonia.Input.Key.L => 37,
            Avalonia.Input.Key.M => 46,
            Avalonia.Input.Key.N => 45,
            Avalonia.Input.Key.O => 31,
            Avalonia.Input.Key.P => 35,
            Avalonia.Input.Key.Q => 12,
            Avalonia.Input.Key.R => 15,
            Avalonia.Input.Key.S => 1,
            Avalonia.Input.Key.T => 17,
            Avalonia.Input.Key.U => 32,
            Avalonia.Input.Key.V => 9,
            Avalonia.Input.Key.W => 13,
            Avalonia.Input.Key.X => 7,
            Avalonia.Input.Key.Y => 16,
            Avalonia.Input.Key.Z => 6,
            Avalonia.Input.Key.D0 => 29,
            Avalonia.Input.Key.D1 => 18,
            Avalonia.Input.Key.D2 => 19,
            Avalonia.Input.Key.D3 => 20,
            Avalonia.Input.Key.D4 => 21,
            Avalonia.Input.Key.D5 => 23,
            Avalonia.Input.Key.D6 => 22,
            Avalonia.Input.Key.D7 => 26,
            Avalonia.Input.Key.D8 => 28,
            Avalonia.Input.Key.D9 => 25,
            _ => null
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint signature;
        public uint id;
    }

    private delegate int EventHandlerDelegate(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(
        IntPtr target,
        EventHandlerDelegate handler,
        uint numTypes,
        ref EventTypeSpec list,
        IntPtr userData,
        out IntPtr handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        ref EventHotKeyId inHotKeyId,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(IntPtr inHotKey);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        out uint outActualSize,
        out EventHotKeyId outData);
}
#endif