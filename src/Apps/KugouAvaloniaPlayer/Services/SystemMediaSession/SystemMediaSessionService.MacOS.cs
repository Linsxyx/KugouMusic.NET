#if KUGOU_MACOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public sealed class SystemMediaSessionService(
    ILogger<SystemMediaSessionService> logger) : ISystemMediaSessionService
{
    private static readonly TimeSpan TimelineUpdateInterval = TimeSpan.FromMilliseconds(750);
    private static readonly IntPtr SelAlloc = sel_registerName("alloc");
    private static readonly IntPtr SelInit = sel_registerName("init");
    private static readonly IntPtr SelRelease = sel_registerName("release");
    private static readonly IntPtr SelSharedCenter = sel_registerName("defaultCenter");
    private static readonly IntPtr SelSharedCommandCenter = sel_registerName("sharedCommandCenter");
    private static readonly IntPtr SelSetNowPlayingInfo = sel_registerName("setNowPlayingInfo:");
    private static readonly IntPtr SelSetPlaybackState = sel_registerName("setPlaybackState:");
    private static readonly IntPtr SelRespondsToSelector = sel_registerName("respondsToSelector:");
    private static readonly IntPtr SelDictionary = sel_registerName("dictionary");
    private static readonly IntPtr SelSetObjectForKey = sel_registerName("setObject:forKey:");
    private static readonly IntPtr SelStringWithUtf8String = sel_registerName("stringWithUTF8String:");
    private static readonly IntPtr SelNumberWithDouble = sel_registerName("numberWithDouble:");
    private static readonly IntPtr SelAddTargetAction = sel_registerName("addTarget:action:");
    private static readonly IntPtr SelRemoveTarget = sel_registerName("removeTarget:");
    private static readonly IntPtr SelSetEnabled = sel_registerName("setEnabled:");
    private static readonly IntPtr SelPositionTime = sel_registerName("positionTime");
    private static readonly IntPtr SelPlay = sel_registerName("kugouPlay:");
    private static readonly IntPtr SelPause = sel_registerName("kugouPause:");
    private static readonly IntPtr SelTogglePlayPause = sel_registerName("kugouTogglePlayPause:");
    private static readonly IntPtr SelNext = sel_registerName("kugouNext:");
    private static readonly IntPtr SelPrevious = sel_registerName("kugouPrevious:");
    private static readonly IntPtr SelChangePlaybackPosition = sel_registerName("kugouChangePlaybackPosition:");

    private static readonly CommandActionDelegate s_playHandler = HandlePlayCommand;
    private static readonly CommandActionDelegate s_pauseHandler = HandlePauseCommand;
    private static readonly CommandActionDelegate s_togglePlayPauseHandler = HandleTogglePlayPauseCommand;
    private static readonly CommandActionDelegate s_nextHandler = HandleNextCommand;
    private static readonly CommandActionDelegate s_previousHandler = HandlePreviousCommand;
    private static readonly CommandActionDelegate s_changePlaybackPositionHandler = HandleChangePlaybackPositionCommand;
    private static IntPtr s_mediaPlayerFramework;
    private static IntPtr s_commandTargetClass;
    private static SystemMediaSessionService? s_currentInstance;

    private readonly List<IntPtr> _registeredCommands = [];
    private PlayerViewModel? _playerViewModel;
    private IntPtr _commandCenter;
    private IntPtr _commandTarget;
    private IntPtr _nowPlayingInfoCenter;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private SongItem? _currentSong;
    private bool _isInitialized;
    private bool _isPlaying;
    private double _positionSeconds;
    private double _durationSeconds;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
        if (_isInitialized)
            return;

        try
        {
            LoadMediaPlayerFramework();
            _playerViewModel = playerViewModel;
            _currentSong = playerViewModel.DisplayedPlayingSong;
            _isPlaying = playerViewModel.IsPlayingAudio;
            _positionSeconds = playerViewModel.CurrentPositionSeconds;
            _durationSeconds = playerViewModel.TotalDurationSeconds;

            EnsureCommandTargetClass();
            _commandTarget = objc_msgSend(objc_msgSend(s_commandTargetClass, SelAlloc), SelInit);
            _nowPlayingInfoCenter = objc_msgSend(objc_getClass("MPNowPlayingInfoCenter"), SelSharedCenter);
            _commandCenter = objc_msgSend(objc_getClass("MPRemoteCommandCenter"), SelSharedCommandCenter);

            s_currentInstance = this;
            RegisterRemoteCommands();
            _isInitialized = true;
            UpdateNowPlayingInfo();
            logger.LogInformation("macOS 系统媒体控件已启用。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "macOS 系统媒体控件初始化失败。");
            Shutdown();
        }
    }

    public Task UpdateSongAsync(SongItem? song)
    {
        _currentSong = song;
        UpdateNowPlayingInfo();
        return Task.CompletedTask;
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
        if (_isPlaying == isPlaying)
            return;

        _isPlaying = isPlaying;
        UpdateNowPlayingInfo();
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        _positionSeconds = Math.Max(0, positionSeconds);
        _durationSeconds = Math.Max(0, durationSeconds);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastTimelineUpdate < TimelineUpdateInterval)
            return;

        _lastTimelineUpdate = now;
        UpdateNowPlayingInfo();
    }

    public void Shutdown()
    {
        if (_commandCenter != IntPtr.Zero && _commandTarget != IntPtr.Zero)
        {
            foreach (var command in _registeredCommands)
            {
                if (command != IntPtr.Zero)
                    objc_msgSend_void_IntPtr(command, SelRemoveTarget, _commandTarget);
            }
        }

        _registeredCommands.Clear();

        if (_nowPlayingInfoCenter != IntPtr.Zero)
        {
            objc_msgSend_void_IntPtr(_nowPlayingInfoCenter, SelSetNowPlayingInfo, IntPtr.Zero);
            TrySetPlaybackState(MacPlaybackStateStopped);
        }

        if (_commandTarget != IntPtr.Zero)
        {
            objc_msgSend(_commandTarget, SelRelease);
            _commandTarget = IntPtr.Zero;
        }

        if (ReferenceEquals(s_currentInstance, this))
            s_currentInstance = null;

        _playerViewModel = null;
        _currentSong = null;
        _commandCenter = IntPtr.Zero;
        _nowPlayingInfoCenter = IntPtr.Zero;
        _isInitialized = false;
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void RegisterRemoteCommands()
    {
        RegisterCommand("playCommand", SelPlay);
        RegisterCommand("pauseCommand", SelPause);
        RegisterCommand("togglePlayPauseCommand", SelTogglePlayPause);
        RegisterCommand("nextTrackCommand", SelNext);
        RegisterCommand("previousTrackCommand", SelPrevious);
        RegisterCommand("changePlaybackPositionCommand", SelChangePlaybackPosition);
    }

    private void RegisterCommand(string commandSelector, IntPtr actionSelector)
    {
        if (_commandCenter == IntPtr.Zero || _commandTarget == IntPtr.Zero)
            return;

        var commandSel = sel_registerName(commandSelector);
        if (!objc_msgSend_boolReturn_IntPtr(_commandCenter, SelRespondsToSelector, commandSel))
            return;

        var command = objc_msgSend(_commandCenter, commandSel);
        if (command == IntPtr.Zero)
            return;

        objc_msgSend_bool(command, SelSetEnabled, true);
        objc_msgSend_IntPtr_IntPtr(command, SelAddTargetAction, _commandTarget, actionSelector);
        _registeredCommands.Add(command);
    }

    private void UpdateNowPlayingInfo()
    {
        if (!_isInitialized || _nowPlayingInfoCenter == IntPtr.Zero)
            return;

        try
        {
            var info = objc_msgSend(objc_getClass("NSMutableDictionary"), SelDictionary);
            var song = _currentSong;
            var title = song?.DisplayTitle;
            if (string.IsNullOrWhiteSpace(title))
                title = "KA Music";

            SetString(info, "MPMediaItemPropertyTitle", title);
            SetString(info, "MPMediaItemPropertyArtist", song?.Singer ?? string.Empty);
            SetDouble(info, "MPMediaItemPropertyPlaybackDuration",
                song?.DurationSeconds > 0 ? song.DurationSeconds : _durationSeconds);
            SetDouble(info, "MPNowPlayingInfoPropertyElapsedPlaybackTime", _positionSeconds);
            SetDouble(info, "MPNowPlayingInfoPropertyPlaybackRate", _isPlaying ? 1 : 0);
            SetDouble(info, "MPNowPlayingInfoPropertyDefaultPlaybackRate", 1);

            objc_msgSend_void_IntPtr(_nowPlayingInfoCenter, SelSetNowPlayingInfo, info);
            TrySetPlaybackState(_currentSong == null
                ? MacPlaybackStateStopped
                : _isPlaying
                    ? MacPlaybackStatePlaying
                    : MacPlaybackStatePaused);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 macOS 系统媒体控件信息失败。");
        }
    }

    private void SetString(IntPtr dictionary, string mediaPlayerKeyName, string value)
    {
        var key = GetMediaPlayerStringConstant(mediaPlayerKeyName);
        if (key == IntPtr.Zero)
            return;

        var text = CreateNSString(value);
        if (text != IntPtr.Zero)
            objc_msgSend_IntPtr_IntPtr(dictionary, SelSetObjectForKey, text, key);
    }

    private void SetDouble(IntPtr dictionary, string mediaPlayerKeyName, double value)
    {
        var key = GetMediaPlayerStringConstant(mediaPlayerKeyName);
        if (key == IntPtr.Zero)
            return;

        var number = objc_msgSend_double(objc_getClass("NSNumber"), SelNumberWithDouble, Math.Max(0, value));
        if (number != IntPtr.Zero)
            objc_msgSend_IntPtr_IntPtr(dictionary, SelSetObjectForKey, number, key);
    }

    private void TrySetPlaybackState(long state)
    {
        if (_nowPlayingInfoCenter == IntPtr.Zero)
            return;

        try
        {
            if (!objc_msgSend_boolReturn_IntPtr(_nowPlayingInfoCenter, SelRespondsToSelector, SelSetPlaybackState))
                return;

            objc_msgSend_long(_nowPlayingInfoCenter, SelSetPlaybackState, state);
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 macOS 系统媒体控件播放状态失败。");
        }
    }

    private void DispatchPlayerCommand(Action<PlayerViewModel> action)
    {
        var player = _playerViewModel;
        if (player == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action(player);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "执行 macOS 系统媒体控件命令失败。");
            }
        });
    }

    private static long HandlePlayCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        s_currentInstance?.DispatchPlayerCommand(player =>
        {
            if (!player.IsPlayingAudio && player.TogglePlayPauseCommand.CanExecute(null))
                player.TogglePlayPauseCommand.Execute(null);
        });
        return RemoteCommandSuccess;
    }

    private static long HandlePauseCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        s_currentInstance?.DispatchPlayerCommand(player =>
        {
            if (player.IsPlayingAudio && player.TogglePlayPauseCommand.CanExecute(null))
                player.TogglePlayPauseCommand.Execute(null);
        });
        return RemoteCommandSuccess;
    }

    private static long HandleTogglePlayPauseCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        s_currentInstance?.DispatchPlayerCommand(player =>
        {
            if (player.TogglePlayPauseCommand.CanExecute(null))
                player.TogglePlayPauseCommand.Execute(null);
        });
        return RemoteCommandSuccess;
    }

    private static long HandleNextCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        s_currentInstance?.DispatchPlayerCommand(player =>
        {
            if (player.PlayNextCommand.CanExecute(null))
                player.PlayNextCommand.Execute(null);
        });
        return RemoteCommandSuccess;
    }

    private static long HandlePreviousCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        s_currentInstance?.DispatchPlayerCommand(player =>
        {
            if (player.PlayPreviousCommand.CanExecute(null))
                player.PlayPreviousCommand.Execute(null);
        });
        return RemoteCommandSuccess;
    }

    private static long HandleChangePlaybackPositionCommand(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        var targetPosition = objc_msgSend_retDouble(commandEvent, SelPositionTime);
        s_currentInstance?.DispatchPlayerCommand(player => player.CurrentPositionSeconds = Math.Max(0, targetPosition));
        return RemoteCommandSuccess;
    }

    private static void EnsureCommandTargetClass()
    {
        if (s_commandTargetClass != IntPtr.Zero)
            return;

        var existingClass = objc_getClass("KugouMacMediaCommandTarget");
        if (existingClass != IntPtr.Zero)
        {
            s_commandTargetClass = existingClass;
            return;
        }

        var nsObject = objc_getClass("NSObject");
        var targetClass = objc_allocateClassPair(nsObject, "KugouMacMediaCommandTarget", IntPtr.Zero);
        if (targetClass == IntPtr.Zero)
            throw new InvalidOperationException("无法创建 macOS 媒体控制命令目标。");

        AddCommandMethod(targetClass, SelPlay, s_playHandler);
        AddCommandMethod(targetClass, SelPause, s_pauseHandler);
        AddCommandMethod(targetClass, SelTogglePlayPause, s_togglePlayPauseHandler);
        AddCommandMethod(targetClass, SelNext, s_nextHandler);
        AddCommandMethod(targetClass, SelPrevious, s_previousHandler);
        AddCommandMethod(targetClass, SelChangePlaybackPosition, s_changePlaybackPositionHandler);
        objc_registerClassPair(targetClass);
        s_commandTargetClass = targetClass;
    }

    private static void AddCommandMethod(IntPtr targetClass, IntPtr selector, CommandActionDelegate handler)
    {
        var imp = Marshal.GetFunctionPointerForDelegate(handler);
        if (!class_addMethod(targetClass, selector, imp, "q@:@"))
            throw new InvalidOperationException("无法注册 macOS 媒体控制命令处理方法。");
    }

    private static IntPtr GetMediaPlayerStringConstant(string symbolName)
    {
        var handle = LoadMediaPlayerFramework();
        if (handle == IntPtr.Zero)
            return CreateNSString(symbolName);

        var symbol = dlsym(handle, symbolName);
        return symbol == IntPtr.Zero ? CreateNSString(symbolName) : Marshal.ReadIntPtr(symbol);
    }

    private static IntPtr LoadMediaPlayerFramework()
    {
        if (s_mediaPlayerFramework != IntPtr.Zero)
            return s_mediaPlayerFramework;

        s_mediaPlayerFramework = dlopen("/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer", RtldLazy);
        return s_mediaPlayerFramework;
    }

    private static IntPtr CreateNSString(string value)
    {
        var utf8 = StringToHGlobalUtf8(value);
        try
        {
            return objc_msgSend_IntPtr(objc_getClass("NSString"), SelStringWithUtf8String, utf8);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8);
        }
    }

    private static IntPtr StringToHGlobalUtf8(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value + '\0');
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return buffer;
    }

    private const int RtldLazy = 1;
    private const long RemoteCommandSuccess = 0;
    private const long MacPlaybackStateStopped = 1;
    private const long MacPlaybackStatePlaying = 2;
    private const long MacPlaybackStatePaused = 3;

    private delegate long CommandActionDelegate(IntPtr self, IntPtr selector, IntPtr commandEvent);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_retDouble(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_boolReturn_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_double(IntPtr receiver, IntPtr selector, double arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg1);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);
}
#endif
