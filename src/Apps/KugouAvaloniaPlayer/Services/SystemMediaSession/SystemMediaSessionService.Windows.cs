#if KUGOU_WINDOWS
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public sealed class SystemMediaSessionService(
    IHttpClientFactory httpClientFactory,
    ILogger<SystemMediaSessionService> logger) : ISystemMediaSessionService
{
    private const string DefaultWindowTitle = "KA Music";
    private const uint NativeButtonPlay = 1;
    private const uint NativeButtonPause = 2;
    private const uint NativeButtonPrevious = 3;
    private const uint NativeButtonNext = 4;
    private static readonly TimeSpan TimelineUpdateInterval = TimeSpan.FromMilliseconds(750);
    private readonly string _artworkCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "media-session-artwork");

    private Window? _mainWindow;
    private PlayerViewModel? _playerViewModel;
    private KugouWinRtNativeApi? _nativeApi;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private int _songUpdateVersion;
    private bool _initializationAttempted;
    private bool _unavailableWarningLogged;

    public bool IsSupported => _nativeApi != null;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
        if (_initializationAttempted)
            return;

        _initializationAttempted = true;
        _mainWindow = mainWindow;
        _playerViewModel = playerViewModel;

        var hwnd = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            LogUnavailable("主窗口句柄不可用。");
            return;
        }

        try
        {
            _nativeApi = KugouWinRtNativeApi.Load(hwnd, OnNativeButtonPressed);
            _ = UpdateSongAsync(playerViewModel.DisplayedPlayingSong);
            UpdatePlaybackState(playerViewModel.IsPlayingAudio);
            UpdateTimeline(playerViewModel.CurrentPositionSeconds, playerViewModel.TotalDurationSeconds);
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or BadImageFormatException
                                   or EntryPointNotFoundException
                                   or InvalidOperationException)
        {
            _nativeApi?.Dispose();
            _nativeApi = null;
            LogUnavailable(
                $"加载或创建 {Path.Combine(AppContext.BaseDirectory, "KugouWinRtNative.dll")} 失败。",
                ex);
        }
    }

    public async Task UpdateSongAsync(SongItem? song)
    {
        UpdateWindowTitle(song);

        var nativeApi = _nativeApi;
        if (nativeApi == null)
            return;

        var updateVersion = Interlocked.Increment(ref _songUpdateVersion);
        try
        {
            var artworkPath = await ResolveArtworkPathAsync(song?.Cover);
            if (updateVersion != Volatile.Read(ref _songUpdateVersion) || nativeApi != _nativeApi)
                return;

            if (string.IsNullOrWhiteSpace(artworkPath) || !File.Exists(artworkPath))
                artworkPath = null;

            var title = song?.DisplayTitle ?? DefaultWindowTitle;
            var artist = song?.Singer ?? string.Empty;
            var status = await Task.Run(() =>
            {
                if (updateVersion != Volatile.Read(ref _songUpdateVersion)
                    || !ReferenceEquals(nativeApi, Volatile.Read(ref _nativeApi)))
                {
                    return (int?)null;
                }

                return nativeApi.UpdateMetadata(title, artist, artworkPath);
            });
            if (status is < 0)
            {
                logger.LogDebug(
                    "更新 Windows 系统媒体控件歌曲信息失败，HRESULT={Status}。",
                    KugouWinRtNativeApi.FormatStatus(status.Value));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Windows 系统媒体控件歌曲信息失败。");
        }
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
        var nativeApi = _nativeApi;
        if (nativeApi == null)
            return;

        try
        {
            var status = nativeApi.UpdatePlaybackState(isPlaying);
            if (status < 0)
            {
                logger.LogDebug(
                    "更新 Windows 系统媒体控件播放状态失败，HRESULT={Status}。",
                    KugouWinRtNativeApi.FormatStatus(status));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Windows 系统媒体控件播放状态失败。");
        }
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        var nativeApi = _nativeApi;
        if (nativeApi == null)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastTimelineUpdate < TimelineUpdateInterval)
            return;

        _lastTimelineUpdate = now;
        var duration = Math.Max(0, durationSeconds);
        var position = Math.Clamp(positionSeconds, 0, duration > 0 ? duration : Math.Max(0, positionSeconds));

        try
        {
            var status = nativeApi.UpdateTimeline(
                SecondsToMilliseconds(position),
                SecondsToMilliseconds(duration));
            if (status < 0)
            {
                logger.LogDebug(
                    "更新 Windows 系统媒体控件播放进度失败，HRESULT={Status}。",
                    KugouWinRtNativeApi.FormatStatus(status));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Windows 系统媒体控件播放进度失败。");
        }
    }

    public void Shutdown()
    {
        Interlocked.Increment(ref _songUpdateVersion);
        _nativeApi?.Dispose();
        _nativeApi = null;

        ResetWindowTitle();
        _mainWindow = null;
        _playerViewModel = null;
        _initializationAttempted = false;
        _lastTimelineUpdate = DateTimeOffset.MinValue;
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void OnNativeButtonPressed(uint button)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var player = _playerViewModel;
            if (player == null)
                return;

            switch (button)
            {
                case NativeButtonPlay when !player.IsPlayingAudio:
                case NativeButtonPause when player.IsPlayingAudio:
                    WeakReferenceMessenger.Default.Send(
                        new PlaybackControlMessage(PlaybackControlAction.TogglePlayPause));
                    break;
                case NativeButtonPrevious:
                    WeakReferenceMessenger.Default.Send(
                        new PlaybackControlMessage(PlaybackControlAction.PreviousTrack));
                    break;
                case NativeButtonNext:
                    WeakReferenceMessenger.Default.Send(
                        new PlaybackControlMessage(PlaybackControlAction.NextTrack));
                    break;
            }
        });
    }

    private void LogUnavailable(string reason, Exception? exception = null)
    {
        if (_unavailableWarningLogged)
            return;

        _unavailableWarningLogged = true;
        if (exception == null)
        {
            logger.LogWarning(
                "Windows 系统媒体控件不可用，已禁用该可选功能，应用将继续运行。原因: {Reason}",
                reason);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Windows 系统媒体控件不可用，已禁用该可选功能，应用将继续运行。原因: {Reason}",
                reason);
        }
    }

    private void UpdateWindowTitle(SongItem? song)
    {
        var window = _mainWindow;
        if (window == null)
            return;

        var title = BuildWindowTitle(song);
        Dispatcher.UIThread.Post(() => window.Title = title);
    }

    private void ResetWindowTitle()
    {
        var window = _mainWindow;
        if (window == null)
            return;

        Dispatcher.UIThread.Post(() => window.Title = DefaultWindowTitle);
    }

    private static string BuildWindowTitle(SongItem? song)
    {
        var songTitle = song?.DisplayTitle?.Trim();
        if (string.IsNullOrWhiteSpace(songTitle))
            return DefaultWindowTitle;

        var singer = song?.Singer?.Trim();
        return string.IsNullOrWhiteSpace(singer)
            ? songTitle
            : $"{songTitle} - {singer}";
    }

    private async Task<string?> ResolveArtworkPathAsync(string? cover)
    {
        if (string.IsNullOrWhiteSpace(cover))
            return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");

        if (cover.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            return await CopyAssetArtworkAsync(cover);

        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return await DownloadArtworkAsync(uri);

            if (uri.IsFile)
                return uri.LocalPath;
        }

        return File.Exists(cover)
            ? cover
            : await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");
    }

    private async Task<string?> DownloadArtworkAsync(Uri uri)
    {
        Directory.CreateDirectory(_artworkCacheDirectory);
        var cachePath = Path.Combine(
            _artworkCacheDirectory,
            $"{GetStableHash(uri.AbsoluteUri)}{GetArtworkExtension(uri)}");
        if (File.Exists(cachePath))
            return cachePath;

        try
        {
            using var response = await httpClientFactory.CreateClient().GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");

            var contentExtension = GetArtworkExtension(response.Content.Headers.ContentType?.MediaType);
            if (!string.Equals(
                    contentExtension,
                    Path.GetExtension(cachePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                cachePath = Path.Combine(
                    _artworkCacheDirectory,
                    $"{GetStableHash(uri.AbsoluteUri)}{contentExtension}");
            }

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(cachePath);
            await source.CopyToAsync(target);
            return cachePath;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "下载 Windows 系统媒体控件封面失败。");
            return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");
        }
    }

    private async Task<string?> CopyAssetArtworkAsync(string assetUri)
    {
        Directory.CreateDirectory(_artworkCacheDirectory);
        var extension = Path.GetExtension(assetUri);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var cachePath = Path.Combine(
            _artworkCacheDirectory,
            $"{GetStableHash(assetUri)}{extension}");
        if (File.Exists(cachePath))
            return cachePath;

        try
        {
            await using var source = AssetLoader.Open(new Uri(assetUri));
            await using var target = File.Create(cachePath);
            await source.CopyToAsync(target);
            return cachePath;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "复制 Windows 系统媒体控件默认封面失败。");
            return null;
        }
    }

    private static long SecondsToMilliseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
            return 0;

        return seconds >= long.MaxValue / 1000d
            ? long.MaxValue
            : (long)Math.Round(seconds * 1000d);
    }

    private static string GetStableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetArtworkExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return IsSupportedArtworkExtension(extension) ? extension : ".jpg";
    }

    private static string GetArtworkExtension(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }

    private static bool IsSupportedArtworkExtension(string? extension)
    {
        return extension?.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";
    }
}
#endif
