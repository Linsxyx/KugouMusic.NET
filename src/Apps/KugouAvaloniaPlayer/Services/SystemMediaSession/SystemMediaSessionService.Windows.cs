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
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public sealed class SystemMediaSessionService(
    IHttpClientFactory httpClientFactory,
    ILogger<SystemMediaSessionService> logger) : ISystemMediaSessionService
{
    private static readonly TimeSpan TimelineUpdateInterval = TimeSpan.FromMilliseconds(750);
    private readonly string _artworkCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "media-session-artwork");

    private PlayerViewModel? _playerViewModel;
    private SystemMediaTransportControls? _transportControls;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private int _songUpdateVersion;
    private bool _isInitialized;

    public bool IsSupported => true;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
        if (_isInitialized)
            return;

        var hwnd = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            logger.LogWarning("Windows 系统媒体控件初始化失败: 主窗口句柄不可用。");
            return;
        }

        try
        {
            _playerViewModel = playerViewModel;
            _transportControls = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
            _transportControls.IsEnabled = true;
            _transportControls.IsPlayEnabled = true;
            _transportControls.IsPauseEnabled = true;
            _transportControls.IsPreviousEnabled = true;
            _transportControls.IsNextEnabled = true;
            _transportControls.IsStopEnabled = false;
            _transportControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _transportControls.ButtonPressed += OnButtonPressed;
            _isInitialized = true;
            _ = UpdateSongAsync(playerViewModel.DisplayedPlayingSong);
            UpdatePlaybackState(playerViewModel.IsPlayingAudio);
            UpdateTimeline(playerViewModel.CurrentPositionSeconds, playerViewModel.TotalDurationSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Windows 系统媒体控件初始化失败。");
            _transportControls = null;
            _playerViewModel = null;
        }
    }

    public async Task UpdateSongAsync(SongItem? song)
    {
        var controls = _transportControls;
        if (controls == null)
            return;

        var updateVersion = Interlocked.Increment(ref _songUpdateVersion);
        try
        {
            var updater = controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = song?.DisplayTitle ?? "KA Music";
            updater.MusicProperties.Artist = song?.Singer ?? string.Empty;

            var artworkPath = await ResolveArtworkPathAsync(song?.Cover);
            if (updateVersion != Volatile.Read(ref _songUpdateVersion))
                return;

            if (!string.IsNullOrWhiteSpace(artworkPath) && File.Exists(artworkPath))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(artworkPath);
                updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(storageFile);
            }
            else
            {
                updater.Thumbnail = null;
            }

            updater.Update();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Windows 系统媒体控件歌曲信息失败。");
        }
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
        var controls = _transportControls;
        if (controls == null)
            return;

        controls.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        var controls = _transportControls;
        if (controls == null)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastTimelineUpdate < TimelineUpdateInterval)
            return;

        _lastTimelineUpdate = now;
        var duration = Math.Max(0, durationSeconds);
        var position = Math.Clamp(positionSeconds, 0, duration > 0 ? duration : Math.Max(0, positionSeconds));

        try
        {
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = TimeSpan.FromSeconds(position),
                EndTime = TimeSpan.FromSeconds(duration),
                MaxSeekTime = TimeSpan.FromSeconds(duration)
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "更新 Windows 系统媒体控件播放进度失败。");
        }
    }

    public void Shutdown()
    {
        if (_transportControls != null)
        {
            _transportControls.ButtonPressed -= OnButtonPressed;
            _transportControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _transportControls.IsEnabled = false;
            _transportControls = null;
        }

        _playerViewModel = null;
        _isInitialized = false;
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var player = _playerViewModel;
        if (player == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play when !player.IsPlayingAudio:
                case SystemMediaTransportControlsButton.Pause when player.IsPlayingAudio:
                    if (player.TogglePlayPauseCommand.CanExecute(null))
                        player.TogglePlayPauseCommand.Execute(null);
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    if (player.PlayPreviousCommand.CanExecute(null))
                        player.PlayPreviousCommand.Execute(null);
                    break;
                case SystemMediaTransportControlsButton.Next:
                    if (player.PlayNextCommand.CanExecute(null))
                        player.PlayNextCommand.Execute(null);
                    break;
            }
        });
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

        return File.Exists(cover) ? cover : await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");
    }

    private async Task<string?> DownloadArtworkAsync(Uri uri)
    {
        Directory.CreateDirectory(_artworkCacheDirectory);
        var cachePath = Path.Combine(_artworkCacheDirectory, $"{GetStableHash(uri.AbsoluteUri)}{GetArtworkExtension(uri)}");
        if (File.Exists(cachePath))
            return cachePath;

        try
        {
            using var response = await httpClientFactory.CreateClient().GetAsync(uri);
            if (!response.IsSuccessStatusCode)
                return await CopyAssetArtworkAsync("avares://KugouAvaloniaPlayer/Assets/default_song.png");

            var contentExtension = GetArtworkExtension(response.Content.Headers.ContentType?.MediaType);
            if (!string.Equals(contentExtension, Path.GetExtension(cachePath), StringComparison.OrdinalIgnoreCase))
                cachePath = Path.Combine(_artworkCacheDirectory, $"{GetStableHash(uri.AbsoluteUri)}{contentExtension}");

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

        var cachePath = Path.Combine(_artworkCacheDirectory, $"{GetStableHash(assetUri)}{extension}");
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
