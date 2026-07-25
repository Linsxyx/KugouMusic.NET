#if KUGOU_LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public sealed class SystemMediaSessionService(
    ILogger<SystemMediaSessionService> logger) : ISystemMediaSessionService, IPathMethodHandler
{
    private const string BusName = "org.mpris.MediaPlayer2.KugouAvaloniaPlayer";
    private const string MediaObjectPath = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";
    private const string ErrorPrefix = "org.freedesktop.DBus.Error.";

    private readonly string _artworkCacheDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "media-session-artwork");

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Lock _connectionStateLock = new();
    private DBusConnection? _connection;
    private Task? _initializationTask;
    private Window? _mainWindow;
    private PlayerViewModel? _playerViewModel;
    private SongItem? _currentSong;
    private ObjectPath _currentTrackPath;
    private int _songUpdateVersion;
    private int _disposeState;
    private bool _isInitialized;
    private bool _isPlaying;
    private bool _isStopped = true;
    private double _positionSeconds;
    private double _durationSeconds;
    private string? _artUrl;

    public bool IsSupported => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));
    public string Path => MediaObjectPath;
    public bool HandlesChildPaths => false;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
        if (Volatile.Read(ref _disposeState) != 0 || _initializationTask != null)
            return;

        _mainWindow = mainWindow;
        _playerViewModel = playerViewModel;
        _currentSong = playerViewModel.DisplayedPlayingSong;
        _currentTrackPath = CreateTrackPath(_currentSong);
        _isPlaying = playerViewModel.IsPlayingAudio;
        _isStopped = _currentSong == null;
        _positionSeconds = playerViewModel.CurrentPositionSeconds;
        _durationSeconds = playerViewModel.TotalDurationSeconds;
        playerViewModel.PropertyChanged += OnPlayerPropertyChanged;
        _initializationTask = InitializeAsync(playerViewModel, _lifetimeCancellation.Token);
    }

    public async Task UpdateSongAsync(SongItem? song)
    {
        var trackChanged = !ReferenceEquals(_currentSong, song);
        _currentSong = song;
        if (trackChanged)
        {
            _currentTrackPath = CreateTrackPath(song);
            _isStopped = song == null;
        }

        var updateVersion = Interlocked.Increment(ref _songUpdateVersion);

        try
        {
            var artUrl = song == null
                ? null
                : await ResolveArtworkUrlAsync(song.Cover).ConfigureAwait(false);
            if (updateVersion != Volatile.Read(ref _songUpdateVersion))
                return;

            _artUrl = artUrl;
            EmitPlayerPropertiesChanged(
                ["PlaybackStatus", "Metadata", "CanGoNext", "CanGoPrevious", "CanPlay", "CanPause", "CanSeek"]);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Linux MPRIS 歌曲信息失败。");
        }
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
        var oldStatus = GetPlaybackStatus();
        _isPlaying = isPlaying;
        if (isPlaying)
            _isStopped = false;

        if (oldStatus == GetPlaybackStatus())
            return;

        EmitPlayerPropertiesChanged(["PlaybackStatus"]);
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        var oldDuration = _durationSeconds;
        var oldCanSeek = CanSeek();
        _positionSeconds = NormalizeSeconds(positionSeconds);
        _durationSeconds = NormalizeSeconds(durationSeconds);

        var changedProperties = new List<string>(2);
        if (Math.Abs(oldDuration - _durationSeconds) >= 0.001)
            changedProperties.Add("Metadata");
        if (oldCanSeek != CanSeek())
            changedProperties.Add("CanSeek");
        if (changedProperties.Count > 0)
            EmitPlayerPropertiesChanged(changedProperties);
    }

    public void Shutdown()
    {
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Dispose 后重复 Shutdown 保持幂等。
        }
        var player = Interlocked.Exchange(ref _playerViewModel, null);
        if (player != null)
            player.PropertyChanged -= OnPlayerPropertyChanged;
        _mainWindow = null;
        _currentSong = null;
        _currentTrackPath = default;
        _artUrl = null;

        DBusConnection? connection;
        lock (_connectionStateLock)
        {
            _isInitialized = false;
            connection = _connection;
            _connection = null;
        }
        DisposeConnection(connection);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        Shutdown();
        _ = DisposeInitializationResourcesAsync(_initializationTask ?? Task.CompletedTask);
    }

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        try
        {
            switch (request.InterfaceAsString)
            {
                case RootInterface:
                    HandleRootMethod(context);
                    break;
                case PlayerInterface:
                    HandlePlayerMethod(context);
                    break;
                case PropertiesInterface:
                    HandlePropertiesMethod(context);
                    break;
                case IntrospectableInterface when request.MemberAsString == "Introspect":
                    ReplyString(context, IntrospectionXml);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "处理 Linux MPRIS 方法调用失败。");
            if (!context.ReplySent)
                context.ReplyError($"{ErrorPrefix}Failed", "The media player could not process the request.");
        }

        await ValueTask.CompletedTask;
    }

    private async Task InitializeAsync(PlayerViewModel playerViewModel, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            logger.LogInformation("未检测到 DBus Session Bus，Linux MPRIS 媒体控件不可用。");
            return;
        }

        DBusConnection? connection = null;
        var lockTaken = false;
        try
        {
            await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            if (_isInitialized || cancellationToken.IsCancellationRequested)
                return;

            connection = new DBusConnection(DBusAddress.Session!);
            await connection.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            connection.AddMethodHandler(this);
            var acquired = await connection.TryRequestNameAsync(BusName, RequestNameOptions.None)
                .ConfigureAwait(false);
            if (!acquired)
            {
                logger.LogWarning("D-Bus 名称 {BusName} 已被占用，Linux MPRIS 服务未启动。", BusName);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_connectionStateLock)
            {
                if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposeState) != 0)
                    return;

                _connection = connection;
                connection = null;
                _isInitialized = true;
            }
            await UpdateSongAsync(playerViewModel.DisplayedPlayingSong).ConfigureAwait(false);
            EmitPlayerPropertiesChanged(
                ["PlaybackStatus", "Metadata", "CanPlay", "CanPause", "CanSeek", "Volume", "LoopStatus", "Shuffle"]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 应用关闭时取消初始化属于正常生命周期。
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Linux MPRIS 媒体控件初始化失败。");
        }
        finally
        {
            DisposeConnection(connection);
            if (lockTaken)
                _initializationLock.Release();
        }
    }

    private void HandleRootMethod(MethodContext context)
    {
        switch (context.Request.MemberAsString)
        {
            case "Raise":
                RaiseMainWindow();
                ReplyEmpty(context);
                break;
            case "Quit":
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandlePlayerMethod(MethodContext context)
    {
        switch (context.Request.MemberAsString)
        {
            case "Next":
                if (_currentSong != null)
                    DispatchTrackChange(playNext: true, preservePlaybackState: true);
                ReplyEmpty(context);
                break;
            case "Previous":
                if (_currentSong != null)
                    DispatchTrackChange(playNext: false, preservePlaybackState: true);
                ReplyEmpty(context);
                break;
            case "Pause":
                DispatchPlayerCommand(player =>
                {
                    if (player.IsPlayingAudio)
                        ((IPlaybackCommands)player).TogglePlayPause();
                });
                ReplyEmpty(context);
                break;
            case "PlayPause":
                _isStopped = false;
                DispatchTogglePlayPause();
                ReplyEmpty(context);
                break;
            case "Stop":
                _isStopped = true;
                _isPlaying = false;
                _positionSeconds = 0;
                DispatchStop();
                EmitPlayerPropertiesChanged(["PlaybackStatus"]);
                EmitSeeked(0);
                ReplyEmpty(context);
                break;
            case "Play":
                _isStopped = false;
                DispatchPlayerCommand(player =>
                {
                    if (!player.IsPlayingAudio)
                        ((IPlaybackCommands)player).TogglePlayPause();
                });
                ReplyEmpty(context);
                break;
            case "Seek":
                SeekByOffset(context);
                break;
            case "SetPosition":
                SetPosition(context);
                break;
            case "OpenUri":
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandlePropertiesMethod(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        switch (context.Request.MemberAsString)
        {
            case "Get":
                GetProperty(context, reader.ReadString(), reader.ReadString());
                break;
            case "GetAll":
                GetAllProperties(context, reader.ReadString());
                break;
            case "Set":
                SetProperty(context, reader.ReadString(), reader.ReadString(), reader.ReadVariantValue());
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void SeekByOffset(MethodContext context)
    {
        var offsetMicroseconds = context.Request.GetBodyReader().ReadInt64();
        if (!CanSeek())
        {
            ReplyEmpty(context);
            return;
        }

        var newPosition = _positionSeconds + offsetMicroseconds / 1_000_000d;
        if (newPosition > _durationSeconds)
        {
            DispatchTrackChange(playNext: true, preservePlaybackState: true);
            ReplyEmpty(context);
            return;
        }

        SetPlayerPosition(Math.Max(0, newPosition));
        ReplyEmpty(context);
    }

    private void SetPosition(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var trackId = reader.ReadObjectPath();
        var positionMicroseconds = reader.ReadInt64();
        if (!CanSeek() ||
            !trackId.Equals(_currentTrackPath) ||
            positionMicroseconds < 0 ||
            positionMicroseconds / 1_000_000d > _durationSeconds)
        {
            ReplyEmpty(context);
            return;
        }

        SetPlayerPosition(positionMicroseconds / 1_000_000d);
        ReplyEmpty(context);
    }

    private void SetPlayerPosition(double positionSeconds)
    {
        _positionSeconds = positionSeconds;

        DispatchPlayerCommand(player => player.CurrentPositionSeconds = positionSeconds);
        EmitSeeked(positionSeconds);
    }

    private void SetProperty(MethodContext context, string interfaceName, string propertyName, VariantValue value)
    {
        if (!IsKnownInterface(interfaceName))
        {
            ReplyDbusError(context, "UnknownInterface", $"Interface '{interfaceName}' is not available.");
            return;
        }

        if (!IsKnownProperty(interfaceName, propertyName))
        {
            ReplyDbusError(context, "UnknownProperty", $"Property '{propertyName}' is not available.");
            return;
        }

        try
        {
            if (interfaceName == RootInterface && propertyName == "Fullscreen")
            {
                ReplyDbusError(context, "NotSupported", "Fullscreen control is not supported.");
                return;
            }

            if (interfaceName == PlayerInterface)
            {
                switch (propertyName)
                {
                    case "Volume":
                    {
                        var volume = Math.Clamp(value.GetDouble(), 0, 1);
                        DispatchPlayerCommand(player => player.MusicVolume = (float)volume);
                        ReplyEmpty(context);
                        return;
                    }
                    case "Shuffle":
                    {
                        var shuffle = value.GetBool();
                        DispatchPlayerCommand(player =>
                        {
                            if (player.IsShuffleMode != shuffle)
                                player.ApplyPlayMode(shuffle ? PlayMode.Shuffle : PlayMode.Normal, saveSettings: true);
                        });
                        ReplyEmpty(context);
                        return;
                    }
                    case "LoopStatus":
                    {
                        var loopStatus = value.GetString();
                        if (loopStatus is not ("None" or "Track" or "Playlist"))
                        {
                            ReplyDbusError(context, "InvalidArgs", $"Unsupported LoopStatus '{loopStatus}'.");
                            return;
                        }

                        DispatchPlayerCommand(player =>
                        {
                            if (loopStatus == "Track")
                                player.ApplyPlayMode(PlayMode.RepeatOne, saveSettings: true);
                            else if (player.IsRepeatOneMode)
                                player.ApplyPlayMode(PlayMode.Normal, saveSettings: true);
                        });
                        ReplyEmpty(context);
                        return;
                    }
                    case "Rate":
                    {
                        var rate = value.GetDouble();
                        if (!double.IsFinite(rate) || rate < 0)
                        {
                            ReplyDbusError(context, "InvalidArgs", "Rate must be a finite non-negative number.");
                            return;
                        }

                        if (rate == 0)
                            PausePlayback();
                        // 此播放器仅支持 1.0；规范允许忽略无法采用的其他速率。
                        ReplyEmpty(context);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "收到类型不匹配的 Linux MPRIS 属性值：{Interface}.{Property}。",
                interfaceName, propertyName);
            ReplyDbusError(context, "InvalidArgs", $"Invalid value for property '{propertyName}'.");
            return;
        }

        ReplyDbusError(context, "PropertyReadOnly", $"Property '{propertyName}' is read-only.");
    }

    private void GetProperty(MethodContext context, string interfaceName, string propertyName)
    {
        if (!IsKnownInterface(interfaceName))
        {
            ReplyDbusError(context, "UnknownInterface", $"Interface '{interfaceName}' is not available.");
            return;
        }

        if (!IsKnownProperty(interfaceName, propertyName))
        {
            ReplyDbusError(context, "UnknownProperty", $"Property '{propertyName}' is not available.");
            return;
        }

        ReplyVariant(context, ReadProperty(interfaceName, propertyName));
    }

    private VariantValue ReadProperty(string interfaceName, string propertyName)
    {
        return interfaceName switch
        {
            RootInterface => propertyName switch
            {
                "CanQuit" or "Fullscreen" or "CanSetFullscreen" => VariantValue.Bool(false),
                "CanRaise" => VariantValue.Bool(true),
                "HasTrackList" => VariantValue.Bool(false),
                "Identity" => VariantValue.String("KA Music"),
                "DesktopEntry" => VariantValue.String("KugouAvaloniaPlayer"),
                "SupportedUriSchemes" or "SupportedMimeTypes" => VariantValue.Array(Array.Empty<string>()),
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
            },
            PlayerInterface => propertyName switch
            {
                "PlaybackStatus" => VariantValue.String(GetPlaybackStatus()),
                "LoopStatus" => VariantValue.String(GetLoopStatus()),
                "Rate" => VariantValue.Double(1),
                "Shuffle" => VariantValue.Bool(_playerViewModel?.IsShuffleMode ?? false),
                "Metadata" => BuildMetadata(),
                "Volume" => VariantValue.Double(_playerViewModel?.MusicVolume ?? 1),
                "Position" => VariantValue.Int64(ToMicroseconds(_positionSeconds)),
                "MinimumRate" or "MaximumRate" => VariantValue.Double(1),
                "CanGoNext" or "CanGoPrevious" or "CanPlay" or "CanPause" => VariantValue.Bool(_currentSong != null),
                "CanSeek" => VariantValue.Bool(CanSeek()),
                "CanControl" => VariantValue.Bool(true),
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(interfaceName), interfaceName, null)
        };
    }

    private void GetAllProperties(MethodContext context, string interfaceName)
    {
        if (!IsKnownInterface(interfaceName))
        {
            ReplyDbusError(context, "UnknownInterface", $"Interface '{interfaceName}' is not available.");
            return;
        }

        var properties = new Dictionary<string, VariantValue>();
        foreach (var propertyName in GetPropertyNames(interfaceName))
            properties[propertyName] = ReadProperty(interfaceName, propertyName);
        ReplyDictionary(context, properties);
    }

    private static string[] GetPropertyNames(string interfaceName)
    {
        return interfaceName switch
        {
            RootInterface =>
            [
                "CanQuit", "Fullscreen", "CanSetFullscreen", "CanRaise", "HasTrackList", "Identity",
                "DesktopEntry", "SupportedUriSchemes", "SupportedMimeTypes"
            ],
            PlayerInterface =>
            [
                "PlaybackStatus", "LoopStatus", "Rate", "Shuffle", "Metadata", "Volume", "Position",
                "MinimumRate", "MaximumRate", "CanGoNext", "CanGoPrevious", "CanPlay", "CanPause",
                "CanSeek", "CanControl"
            ],
            _ => []
        };
    }

    private static bool IsKnownInterface(string interfaceName) =>
        interfaceName is RootInterface or PlayerInterface;

    private static bool IsKnownProperty(string interfaceName, string propertyName) =>
        Array.IndexOf(GetPropertyNames(interfaceName), propertyName) >= 0;

    private VariantValue BuildMetadata()
    {
        var song = _currentSong;
        if (song == null)
            return new Dict<string, VariantValue>(new Dictionary<string, VariantValue>());

        var title = string.IsNullOrWhiteSpace(song.DisplayTitle) ? song.Name : song.DisplayTitle;
        var artist = song.Singer;
        var metadata = new Dictionary<string, VariantValue>
        {
            ["mpris:trackid"] = VariantValue.ObjectPath(_currentTrackPath),
            ["xesam:title"] = VariantValue.String(title),
            ["xesam:artist"] = VariantValue.Array(string.IsNullOrWhiteSpace(artist) ? Array.Empty<string>() : [artist]),
            ["mpris:length"] = VariantValue.Int64(ToMicroseconds(song.DurationSeconds > 0 ? song.DurationSeconds : _durationSeconds))
        };

        if (!string.IsNullOrWhiteSpace(_artUrl))
            metadata["mpris:artUrl"] = VariantValue.String(_artUrl);

        return new Dict<string, VariantValue>(metadata);
    }

    private void EmitPlayerPropertiesChanged(IReadOnlyList<string> propertyNames)
    {
        if (!_isInitialized || _connection == null)
            return;

        try
        {
            var changed = new Dictionary<string, VariantValue>();
            foreach (var propertyName in propertyNames)
                changed[propertyName] = ReadProperty(PlayerInterface, propertyName);

            EmitPropertiesChanged(PlayerInterface, changed);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送 Linux MPRIS 属性变更失败。");
        }
    }

    private void EmitPropertiesChanged(string interfaceName, Dictionary<string, VariantValue> changedProperties)
    {
        var connection = _connection;
        if (connection == null || changedProperties.Count == 0)
            return;

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: MediaObjectPath,
            @interface: PropertiesInterface,
            member: "PropertiesChanged",
            signature: "sa{sv}as");
        writer.WriteString(interfaceName);
        writer.WriteDictionary(changedProperties);
        writer.WriteArray(Array.Empty<string>());
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void EmitSeeked(double positionSeconds)
    {
        var connection = _connection;
        if (connection == null)
            return;

        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null, MediaObjectPath, PlayerInterface, "Seeked", "x");
            writer.WriteInt64(ToMicroseconds(positionSeconds));
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送 Linux MPRIS Seeked 信号失败。");
        }
    }

    private void RaiseMainWindow()
    {
        var window = _mainWindow;
        if (window == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!window.IsVisible)
                    window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "通过 Linux MPRIS 唤起主窗口失败。");
            }
        });
    }

    private void PausePlayback()
    {
        DispatchPlayerCommand(player =>
        {
            if (player.IsPlayingAudio)
                ((IPlaybackCommands)player).TogglePlayPause();
        });
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.MusicVolume):
                EmitPlayerPropertiesChanged(["Volume"]);
                break;
            case nameof(PlayerViewModel.IsShuffleMode):
                EmitPlayerPropertiesChanged(["Shuffle"]);
                break;
            case nameof(PlayerViewModel.IsRepeatOneMode):
                EmitPlayerPropertiesChanged(["LoopStatus"]);
                break;
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
                logger.LogDebug(ex, "执行 Linux MPRIS 播放器命令失败。");
            }
        });
    }

    private void DispatchTogglePlayPause()
    {
        DispatchPlayerCommand(player => ((IPlaybackCommands)player).TogglePlayPause());
    }

    private void DispatchStop()
    {
        DispatchPlayerCommand(player => ((IPlaybackCommands)player).Stop());
    }

    private void DispatchTrackChange(bool playNext, bool preservePlaybackState = false)
    {
        DispatchPlayerCommand(player =>
        {
            var commands = (IPlaybackCommands)player;
            _ = playNext
                ? commands.PlayNextAsync(preservePlaybackState)
                : commands.PlayPreviousAsync(preservePlaybackState);
        });
    }

    private void DisposeConnection(DBusConnection? connection)
    {
        if (connection == null)
            return;

        try
        {
            connection.RemoveMethodHandler(MediaObjectPath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "移除 Linux MPRIS D-Bus 方法处理器失败。");
        }

        try
        {
            connection.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "关闭 Linux MPRIS D-Bus 连接失败。");
        }
    }

    private async Task DisposeInitializationResourcesAsync(Task initializationTask)
    {
        try
        {
            await initializationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "等待 Linux MPRIS 初始化任务结束失败。");
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _initializationLock.Dispose();
        }
    }

    private async Task<string?> ResolveArtworkUrlAsync(string? cover)
    {
        if (string.IsNullOrWhiteSpace(cover))
            return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png")
                .ConfigureAwait(false);

        if (cover.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            return await CopyAssetArtworkAsync(cover).ConfigureAwait(false);

        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "file")
                return uri.AbsoluteUri;
        }

        return File.Exists(cover)
            ? new Uri(System.IO.Path.GetFullPath(cover)).AbsoluteUri
            : await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png")
                .ConfigureAwait(false);
    }

    private async Task<string?> CopyAssetArtworkAsync(string assetUri)
    {
        Directory.CreateDirectory(_artworkCacheDirectory);
        var extension = System.IO.Path.GetExtension(assetUri);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var cachePath = System.IO.Path.Combine(_artworkCacheDirectory, $"{GetStableHash(assetUri)}{extension}");
        if (!File.Exists(cachePath))
        {
            try
            {
                await using var source = AssetLoader.Open(new Uri(assetUri));
                await using var target = File.Create(cachePath);
                await source.CopyToAsync(target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "复制 Linux MPRIS 默认封面失败。");
                return null;
            }
        }

        return new Uri(cachePath).AbsoluteUri;
    }

    private static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyString(MethodContext context, string value)
    {
        using var writer = context.CreateReplyWriter("s");
        writer.WriteString(value);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyVariant(MethodContext context, VariantValue value)
    {
        using var writer = context.CreateReplyWriter("v");
        writer.WriteVariant(value);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyDictionary(MethodContext context, Dictionary<string, VariantValue> value)
    {
        using var writer = context.CreateReplyWriter("a{sv}");
        writer.WriteDictionary(value);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyDbusError(MethodContext context, string errorName, string message)
    {
        context.ReplyError($"{ErrorPrefix}{errorName}", message);
    }

    private string GetPlaybackStatus()
    {
        return _currentSong == null || _isStopped ? "Stopped" : _isPlaying ? "Playing" : "Paused";
    }

    private string GetLoopStatus()
    {
        return _playerViewModel?.IsRepeatOneMode == true ? "Track" : "None";
    }

    private bool CanSeek()
    {
        return _currentSong != null && _durationSeconds > 0;
    }

    private static ObjectPath CreateTrackPath(SongItem? song)
    {
        if (song == null)
            return default;

        var songKey = PlaybackQueueCacheService.BuildSongKey(song);
        return new ObjectPath($"/com/kugou/KAMusic/Track/{GetStableHash(songKey)}");
    }

    private static double NormalizeSeconds(double seconds)
    {
        return double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
    }

    private static long ToMicroseconds(double seconds)
    {
        var normalized = NormalizeSeconds(seconds);
        return (long)Math.Round(Math.Min(normalized, long.MaxValue / 1_000_000d) * 1_000_000d);
    }

    private static string GetStableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private const string IntrospectionXml = """
                                            <!DOCTYPE node PUBLIC "-//freedesktop//DTD D-BUS Object Introspection 1.0//EN"
                                             "http://www.freedesktop.org/standards/dbus/1.0/introspect.dtd">
                                            <node>
                                              <interface name="org.freedesktop.DBus.Introspectable">
                                                <method name="Introspect">
                                                  <arg name="xml_data" type="s" direction="out"/>
                                                </method>
                                              </interface>
                                              <interface name="org.freedesktop.DBus.Properties">
                                                <method name="Get">
                                                  <arg name="interface_name" type="s" direction="in"/>
                                                  <arg name="property_name" type="s" direction="in"/>
                                                  <arg name="value" type="v" direction="out"/>
                                                </method>
                                                <method name="GetAll">
                                                  <arg name="interface_name" type="s" direction="in"/>
                                                  <arg name="properties" type="a{sv}" direction="out"/>
                                                </method>
                                                <method name="Set">
                                                  <arg name="interface_name" type="s" direction="in"/>
                                                  <arg name="property_name" type="s" direction="in"/>
                                                  <arg name="value" type="v" direction="in"/>
                                                </method>
                                                <signal name="PropertiesChanged">
                                                  <arg name="interface_name" type="s"/>
                                                  <arg name="changed_properties" type="a{sv}"/>
                                                  <arg name="invalidated_properties" type="as"/>
                                                </signal>
                                              </interface>
                                              <interface name="org.mpris.MediaPlayer2">
                                                <method name="Raise"/>
                                                <method name="Quit"/>
                                                <property name="CanQuit" type="b" access="read"/>
                                                <property name="Fullscreen" type="b" access="readwrite"/>
                                                <property name="CanSetFullscreen" type="b" access="read"/>
                                                <property name="CanRaise" type="b" access="read"/>
                                                <property name="HasTrackList" type="b" access="read"/>
                                                <property name="Identity" type="s" access="read"/>
                                                <property name="DesktopEntry" type="s" access="read"/>
                                                <property name="SupportedUriSchemes" type="as" access="read"/>
                                                <property name="SupportedMimeTypes" type="as" access="read"/>
                                              </interface>
                                              <interface name="org.mpris.MediaPlayer2.Player">
                                                <method name="Next"/>
                                                <method name="Previous"/>
                                                <method name="Pause"/>
                                                <method name="PlayPause"/>
                                                <method name="Stop"/>
                                                <method name="Play"/>
                                                <method name="Seek">
                                                  <arg name="Offset" type="x" direction="in"/>
                                                </method>
                                                <method name="SetPosition">
                                                  <arg name="TrackId" type="o" direction="in"/>
                                                  <arg name="Position" type="x" direction="in"/>
                                                </method>
                                                <method name="OpenUri">
                                                  <arg name="Uri" type="s" direction="in"/>
                                                </method>
                                                <signal name="Seeked">
                                                  <arg name="Position" type="x"/>
                                                </signal>
                                                <property name="PlaybackStatus" type="s" access="read"/>
                                                <property name="LoopStatus" type="s" access="readwrite"/>
                                                <property name="Rate" type="d" access="readwrite"/>
                                                <property name="Shuffle" type="b" access="readwrite"/>
                                                <property name="Metadata" type="a{sv}" access="read"/>
                                                <property name="Volume" type="d" access="readwrite"/>
                                                <property name="Position" type="x" access="read">
                                                  <annotation name="org.freedesktop.DBus.Property.EmitsChangedSignal" value="false"/>
                                                </property>
                                                <property name="MinimumRate" type="d" access="read"/>
                                                <property name="MaximumRate" type="d" access="read"/>
                                                <property name="CanGoNext" type="b" access="read"/>
                                                <property name="CanGoPrevious" type="b" access="read"/>
                                                <property name="CanPlay" type="b" access="read"/>
                                                <property name="CanPause" type="b" access="read"/>
                                                <property name="CanSeek" type="b" access="read"/>
                                                <property name="CanControl" type="b" access="read">
                                                  <annotation name="org.freedesktop.DBus.Property.EmitsChangedSignal" value="false"/>
                                                </property>
                                              </interface>
                                            </node>
                                            """;
}
#endif
