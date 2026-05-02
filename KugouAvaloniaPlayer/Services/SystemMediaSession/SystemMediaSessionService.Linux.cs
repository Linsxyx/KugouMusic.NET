#if KUGOU_LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private static readonly TimeSpan TimelineUpdateInterval = TimeSpan.FromMilliseconds(750);
    private static readonly ObjectPath TrackObjectPath = new("/org/mpris/MediaPlayer2/Track/CurrentTrack");

    private readonly string _artworkCacheDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "media-session-artwork");

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private DBusConnection? _connection;
    private PlayerViewModel? _playerViewModel;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private SongItem? _currentSong;
    private int _songUpdateVersion;
    private bool _isInitialized;
    private bool _isPlaying;
    private double _positionSeconds;
    private double _durationSeconds;
    private string? _artUrl;

    public bool IsSupported => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));
    public string Path => MediaObjectPath;
    public bool HandlesChildPaths => false;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
        if (_isInitialized)
            return;

        _playerViewModel = playerViewModel;
        _currentSong = playerViewModel.DisplayedPlayingSong;
        _isPlaying = playerViewModel.IsPlayingAudio;
        _positionSeconds = playerViewModel.CurrentPositionSeconds;
        _durationSeconds = playerViewModel.TotalDurationSeconds;
        _ = InitializeAsync(playerViewModel);
    }

    public async Task UpdateSongAsync(SongItem? song)
    {
        _currentSong = song;
        var updateVersion = Interlocked.Increment(ref _songUpdateVersion);

        try
        {
            var artUrl = await ResolveArtworkUrlAsync(song?.Cover);
            if (updateVersion != Volatile.Read(ref _songUpdateVersion))
                return;

            _artUrl = artUrl;
            EmitPlayerPropertiesChanged(["Metadata", "CanPlay", "CanPause", "CanSeek"]);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Linux MPRIS 歌曲信息失败。");
        }
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
        if (_isPlaying == isPlaying)
            return;

        _isPlaying = isPlaying;
        EmitPlayerPropertiesChanged(["PlaybackStatus"]);
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        _positionSeconds = Math.Max(0, positionSeconds);
        _durationSeconds = Math.Max(0, durationSeconds);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastTimelineUpdate < TimelineUpdateInterval)
            return;

        _lastTimelineUpdate = now;
        EmitPlayerPropertiesChanged(["Position", "Metadata", "CanSeek"]);
    }

    public void Shutdown()
    {
        _isInitialized = false;
        _playerViewModel = null;
        _currentSong = null;
        _artUrl = null;

        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection == null)
            return;

        try
        {
            connection.RemoveMethodHandler(MediaObjectPath);
            connection.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "关闭 Linux MPRIS 会话失败。");
        }
    }

    public void Dispose()
    {
        Shutdown();
        _initializationLock.Dispose();
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
                context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message);
        }

        await ValueTask.CompletedTask;
    }

    private async Task InitializeAsync(PlayerViewModel playerViewModel)
    {
        if (!IsSupported)
        {
            logger.LogInformation("未检测到 DBus Session Bus，Linux MPRIS 媒体控件不可用。");
            return;
        }

        await _initializationLock.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            var connection = new DBusConnection(DBusAddress.Session!);
            await connection.ConnectAsync();
            connection.AddMethodHandler(this);
            await connection.RequestNameAsync(BusName, RequestNameOptions.ReplaceExisting);

            _connection = connection;
            _isInitialized = true;
            await UpdateSongAsync(playerViewModel.DisplayedPlayingSong);
            EmitPlayerPropertiesChanged(["PlaybackStatus", "Metadata", "Position"]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Linux MPRIS 媒体控件初始化失败。");
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private void HandleRootMethod(MethodContext context)
    {
        switch (context.Request.MemberAsString)
        {
            case "Raise":
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
                DispatchPlayerCommand(player => player.PlayNextCommand.Execute(null));
                ReplyEmpty(context);
                break;
            case "Previous":
                DispatchPlayerCommand(player => player.PlayPreviousCommand.Execute(null));
                ReplyEmpty(context);
                break;
            case "Pause":
                DispatchPlayerCommand(player =>
                {
                    if (player.IsPlayingAudio)
                        player.TogglePlayPauseCommand.Execute(null);
                });
                ReplyEmpty(context);
                break;
            case "PlayPause":
                DispatchPlayerCommand(player => player.TogglePlayPauseCommand.Execute(null));
                ReplyEmpty(context);
                break;
            case "Stop":
                DispatchPlayerCommand(player =>
                {
                    if (player.IsPlayingAudio)
                        player.TogglePlayPauseCommand.Execute(null);
                    player.CurrentPositionSeconds = 0;
                });
                ReplyEmpty(context);
                break;
            case "Play":
                DispatchPlayerCommand(player =>
                {
                    if (!player.IsPlayingAudio)
                        player.TogglePlayPauseCommand.Execute(null);
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
                ReplyVariant(context, GetProperty(reader.ReadString(), reader.ReadString()));
                break;
            case "GetAll":
                ReplyDictionary(context, GetAllProperties(reader.ReadString()));
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
        var newPosition = _positionSeconds + offsetMicroseconds / 1_000_000d;
        SetPlayerPosition(context, newPosition);
    }

    private void SetPosition(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var trackId = reader.ReadObjectPath();
        var positionMicroseconds = reader.ReadInt64();
        if (!trackId.Equals(TrackObjectPath))
        {
            ReplyEmpty(context);
            return;
        }

        SetPlayerPosition(context, positionMicroseconds / 1_000_000d);
    }

    private void SetPlayerPosition(MethodContext context, double positionSeconds)
    {
        var duration = _durationSeconds;
        var safePosition = Math.Clamp(positionSeconds, 0, duration > 0 ? duration : Math.Max(0, positionSeconds));
        _positionSeconds = safePosition;

        DispatchPlayerCommand(player => player.CurrentPositionSeconds = safePosition);
        EmitSeeked(safePosition);
        EmitPlayerPropertiesChanged(["Position"]);
        ReplyEmpty(context);
    }

    private void SetProperty(MethodContext context, string interfaceName, string propertyName, VariantValue value)
    {
        if (interfaceName == PlayerInterface && propertyName == "Volume")
        {
            var volume = Math.Clamp(value.GetDouble(), 0, 1);
            DispatchPlayerCommand(player => player.MusicVolume = (float)volume);
            ReplyEmpty(context);
            return;
        }

        context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", $"{propertyName} is read-only.");
    }

    private VariantValue GetProperty(string interfaceName, string propertyName)
    {
        if (interfaceName == RootInterface)
            return propertyName switch
            {
                "CanQuit" => VariantValue.Bool(false),
                "Fullscreen" => VariantValue.Bool(false),
                "CanSetFullscreen" => VariantValue.Bool(false),
                "CanRaise" => VariantValue.Bool(true),
                "HasTrackList" => VariantValue.Bool(false),
                "Identity" => VariantValue.String("KA Music"),
                "DesktopEntry" => VariantValue.String("KugouAvaloniaPlayer"),
                "SupportedUriSchemes" => VariantValue.Array(Array.Empty<string>()),
                "SupportedMimeTypes" => VariantValue.Array(Array.Empty<string>()),
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
            };

        if (interfaceName == PlayerInterface)
            return propertyName switch
            {
                "PlaybackStatus" => VariantValue.String(GetPlaybackStatus()),
                "LoopStatus" => VariantValue.String("None"),
                "Rate" => VariantValue.Double(1),
                "Shuffle" => VariantValue.Bool(_playerViewModel?.IsShuffleMode ?? false),
                "Metadata" => BuildMetadata(),
                "Volume" => VariantValue.Double(_playerViewModel?.MusicVolume ?? 1),
                "Position" => VariantValue.Int64(ToMicroseconds(_positionSeconds)),
                "MinimumRate" => VariantValue.Double(1),
                "MaximumRate" => VariantValue.Double(1),
                "CanGoNext" => VariantValue.Bool(true),
                "CanGoPrevious" => VariantValue.Bool(true),
                "CanPlay" => VariantValue.Bool(_currentSong != null),
                "CanPause" => VariantValue.Bool(_currentSong != null),
                "CanSeek" => VariantValue.Bool(_durationSeconds > 0),
                "CanControl" => VariantValue.Bool(true),
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
            };

        throw new ArgumentOutOfRangeException(nameof(interfaceName), interfaceName, null);
    }

    private Dictionary<string, VariantValue> GetAllProperties(string interfaceName)
    {
        var properties = new Dictionary<string, VariantValue>();
        foreach (var propertyName in GetPropertyNames(interfaceName))
            properties[propertyName] = GetProperty(interfaceName, propertyName);
        return properties;
    }

    private string[] GetPropertyNames(string interfaceName)
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

    private VariantValue BuildMetadata()
    {
        var song = _currentSong;
        var title = song?.DisplayTitle;
        if (string.IsNullOrWhiteSpace(title))
            title = "KA Music";

        var artist = song?.Singer;
        var metadata = new Dictionary<string, VariantValue>
        {
            ["mpris:trackid"] = VariantValue.ObjectPath(TrackObjectPath),
            ["xesam:title"] = VariantValue.String(title),
            ["xesam:artist"] = VariantValue.Array(string.IsNullOrWhiteSpace(artist) ? Array.Empty<string>() : [artist]),
            ["mpris:length"] = VariantValue.Int64(ToMicroseconds(song?.DurationSeconds > 0 ? song.DurationSeconds : _durationSeconds))
        };

        if (!string.IsNullOrWhiteSpace(_artUrl))
            metadata["mpris:artUrl"] = VariantValue.String(_artUrl);

        return new Dict<string, VariantValue>(metadata);
    }

    private void EmitPlayerPropertiesChanged(string[] propertyNames)
    {
        if (!_isInitialized || _connection == null)
            return;

        try
        {
            var changed = new Dictionary<string, VariantValue>();
            foreach (var propertyName in propertyNames)
                changed[propertyName] = GetProperty(PlayerInterface, propertyName);

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

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, MediaObjectPath, PlayerInterface, "Seeked", "x");
        writer.WriteInt64(ToMicroseconds(positionSeconds));
        connection.TrySendMessage(writer.CreateMessage());
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

    private async Task<string?> ResolveArtworkUrlAsync(string? cover)
    {
        if (string.IsNullOrWhiteSpace(cover))
            return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");

        if (cover.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            return await CopyAssetArtworkAsync(cover);

        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "file")
                return uri.AbsoluteUri;
        }

        return File.Exists(cover)
            ? new Uri(System.IO.Path.GetFullPath(cover)).AbsoluteUri
            : await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");
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
                await source.CopyToAsync(target);
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

    private string GetPlaybackStatus()
    {
        return _currentSong == null ? "Stopped" : _isPlaying ? "Playing" : "Paused";
    }

    private static long ToMicroseconds(double seconds)
    {
        return (long)Math.Round(Math.Max(0, seconds) * 1_000_000d);
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
                                                <property name="Position" type="x" access="read"/>
                                                <property name="MinimumRate" type="d" access="read"/>
                                                <property name="MaximumRate" type="d" access="read"/>
                                                <property name="CanGoNext" type="b" access="read"/>
                                                <property name="CanGoPrevious" type="b" access="read"/>
                                                <property name="CanPlay" type="b" access="read"/>
                                                <property name="CanPause" type="b" access="read"/>
                                                <property name="CanSeek" type="b" access="read"/>
                                                <property name="CanControl" type="b" access="read"/>
                                              </interface>
                                            </node>
                                            """;
}
#endif
