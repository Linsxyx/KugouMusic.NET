#if KUGOU_WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services;

public sealed class WindowsTaskbarThumbnailToolbar : IDisposable
{
    public const uint PreviousButtonId = 1001;
    public const uint PlayPauseButtonId = 1002;
    public const uint NextButtonId = 1003;
    public const uint LikeButtonId = 1004;

    private readonly TaskbarButtonClickCallback _callbackThunk;
    private readonly Action<uint> _clickHandler;
    private readonly IntPtr _hwnd;
    private bool _disposed;
    private bool _isLiked;
    private bool _isPlaying;
    private IntPtr _nativeHandle;

    public WindowsTaskbarThumbnailToolbar(
        Window window,
        IntPtr hwnd,
        string previousIconPath,
        string playIconPath,
        string pauseIconPath,
        string nextIconPath,
        string heartGreyIconPath,
        string heartRedIconPath,
        Action<uint> clickHandler)
    {
        _ = window;
        _hwnd = hwnd;
        _clickHandler = clickHandler;
        _callbackThunk = HandleNativeButtonClick;

        PreviousIconPath = previousIconPath;
        PlayIconPath = playIconPath;
        PauseIconPath = pauseIconPath;
        NextIconPath = nextIconPath;
        HeartGreyIconPath = heartGreyIconPath;
        HeartRedIconPath = heartRedIconPath;
    }

    private string HeartGreyIconPath { get; }
    private string HeartRedIconPath { get; }
    private string NextIconPath { get; }
    private string PauseIconPath { get; }
    private string PlayIconPath { get; }
    private string PreviousIconPath { get; }

    public bool Initialize()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero || _nativeHandle != IntPtr.Zero)
            return false;

        if (!File.Exists(PreviousIconPath) ||
            !File.Exists(PlayIconPath) ||
            !File.Exists(PauseIconPath) ||
            !File.Exists(NextIconPath) ||
            !File.Exists(HeartGreyIconPath) ||
            !File.Exists(HeartRedIconPath))
        {
            return false;
        }

        _nativeHandle = NativeMethods.KgTaskbarToolbar_Create(
            _hwnd,
            PreviousIconPath,
            PlayIconPath,
            PauseIconPath,
            NextIconPath,
            HeartGreyIconPath,
            HeartRedIconPath,
            _callbackThunk,
            IntPtr.Zero);

        if (_nativeHandle == IntPtr.Zero)
            return false;

        UpdatePlayPause(_isPlaying);
        UpdateLike(_isLiked, enabled: false);
        return true;
    }

    public void UpdateEnabled(
        bool previousEnabled,
        bool playPauseEnabled,
        bool nextEnabled,
        bool likeEnabled)
    {
        if (_nativeHandle == IntPtr.Zero)
            return;

        NativeMethods.KgTaskbarToolbar_UpdateEnabled(
            _nativeHandle,
            previousEnabled,
            playPauseEnabled,
            nextEnabled,
            likeEnabled);
    }

    public void UpdatePlayPause(bool isPlaying)
    {
        _isPlaying = isPlaying;
        if (_nativeHandle == IntPtr.Zero)
            return;

        NativeMethods.KgTaskbarToolbar_UpdatePlayPause(_nativeHandle, isPlaying);
    }

    public void UpdateLike(bool isLiked, bool enabled)
    {
        _isLiked = isLiked;
        if (_nativeHandle == IntPtr.Zero)
            return;

        NativeMethods.KgTaskbarToolbar_UpdateLike(_nativeHandle, isLiked, enabled);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_nativeHandle != IntPtr.Zero)
        {
            NativeMethods.KgTaskbarToolbar_Destroy(_nativeHandle);
            _nativeHandle = IntPtr.Zero;
        }
    }

    private void HandleNativeButtonClick(uint buttonId, IntPtr userData)
    {
        _ = userData;
        _clickHandler(buttonId);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TaskbarButtonClickCallback(uint buttonId, IntPtr userData);

    private static class NativeMethods
    {
        [DllImport("KugouTaskbarNative.dll", EntryPoint = "KgTaskbarToolbar_Create", CharSet = CharSet.Unicode)]
        public static extern IntPtr KgTaskbarToolbar_Create(
            IntPtr hwnd,
            string previousIconPath,
            string playIconPath,
            string pauseIconPath,
            string nextIconPath,
            string heartGreyIconPath,
            string heartRedIconPath,
            TaskbarButtonClickCallback callback,
            IntPtr userData);

        [DllImport("KugouTaskbarNative.dll", EntryPoint = "KgTaskbarToolbar_UpdatePlayPause")]
        public static extern void KgTaskbarToolbar_UpdatePlayPause(
            IntPtr toolbar,
            [MarshalAs(UnmanagedType.Bool)] bool isPlaying);

        [DllImport("KugouTaskbarNative.dll", EntryPoint = "KgTaskbarToolbar_UpdateEnabled")]
        public static extern void KgTaskbarToolbar_UpdateEnabled(
            IntPtr toolbar,
            [MarshalAs(UnmanagedType.Bool)] bool previousEnabled,
            [MarshalAs(UnmanagedType.Bool)] bool playPauseEnabled,
            [MarshalAs(UnmanagedType.Bool)] bool nextEnabled,
            [MarshalAs(UnmanagedType.Bool)] bool likeEnabled);

        [DllImport("KugouTaskbarNative.dll", EntryPoint = "KgTaskbarToolbar_UpdateLike")]
        public static extern void KgTaskbarToolbar_UpdateLike(
            IntPtr toolbar,
            [MarshalAs(UnmanagedType.Bool)] bool isLiked,
            [MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("KugouTaskbarNative.dll", EntryPoint = "KgTaskbarToolbar_Destroy")]
        public static extern void KgTaskbarToolbar_Destroy(IntPtr toolbar);
    }
}
#else
using System;
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services;

public sealed class WindowsTaskbarThumbnailToolbar : IDisposable
{
    public const uint PreviousButtonId = 1001;
    public const uint PlayPauseButtonId = 1002;
    public const uint NextButtonId = 1003;
    public const uint LikeButtonId = 1004;

    public WindowsTaskbarThumbnailToolbar(
        Window window,
        IntPtr hwnd,
        string previousIconPath,
        string playIconPath,
        string pauseIconPath,
        string nextIconPath,
        string heartGreyIconPath,
        string heartRedIconPath,
        Action<uint> clickHandler)
    {
    }

    public bool Initialize() => false;

    public void UpdateEnabled(
        bool previousEnabled,
        bool playPauseEnabled,
        bool nextEnabled,
        bool likeEnabled)
    {
    }

    public void UpdatePlayPause(bool isPlaying)
    {
    }

    public void UpdateLike(bool isLiked, bool enabled)
    {
    }

    public void Dispose()
    {
    }
}
#endif
