using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan AudioLoadTimeout = TimeSpan.FromSeconds(12);
    private readonly FavoritePlaylistService _favoriteService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricsService _lyricsService;
    private readonly MusicClient _musicClient;
    private readonly DispatcherTimer _playbackTimer;

    private readonly SimpleAudioPlayer _player;
    private readonly SemaphoreSlim _playSongLock = new(1, 1);

    private readonly PlaybackQueueManager _queueManager;
    private readonly KgSessionManager _sessionManager;
    private readonly ISukiToastManager _toastManager;
    private int _disposeState;

    private int _consecutiveFailures;

    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;
    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";
    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isDraggingProgress;
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isPlayingAudio;
    [ObservableProperty] private bool _isSwitchingQuality;
    private CancellationTokenSource? _loadCancellation;
    [ObservableProperty] private string _musicQuality = "128";
    [ObservableProperty] private string _qualitySelection = "128";
    [ObservableProperty] private float _musicVolume = 0.8f;
    private int _playRequestVersion;
    [ObservableProperty] private double _totalDurationSeconds;

    public PlayerViewModel(
        MusicClient musicClient, ISukiToastManager toastManager, ILogger<PlayerViewModel> logger,
        PlaybackQueueManager queueManager, LyricsService lyricsService, FavoritePlaylistService favoriteService,
        KgSessionManager sessionManager)
    {
        _musicClient = musicClient;
        _toastManager = toastManager;
        _logger = logger;
        _queueManager = queueManager;
        _lyricsService = lyricsService;
        _favoriteService = favoriteService;
        _sessionManager = sessionManager;

        _player = new SimpleAudioPlayer();
        _player.PlaybackEnded += OnPlaybackEnded;
        MusicQuality = SettingsManager.Settings.MusicQuality;
        QualitySelection = MusicQuality;
        UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (_, m) => { _queueManager.AddToNext(m.Song, CurrentPlayingSong); });
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            (_, m) => _ = ShowPlaylistDialogSafelyAsync(m.Song));
    }

    private async Task ShowPlaylistDialogSafelyAsync(SongItem song)
    {
        try
        {
            await _favoriteService.ShowAddToPlaylistDialogAsync(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开添加到歌单对话框失败");
        }
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;
    public string[] QualityOptions { get; } = ["128", "320", "flac", "high"];

    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1) return;

        CancelAndDisposeLoadCancellation();
        _playSongLock.Dispose();
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
        _queueManager.Clear();
        _lyricsService.Clear();
        GC.SuppressFinalize(this);
    }

    public async Task PlaySongAsync(SongItem? song, IList<SongItem>? contextList = null)
    {
        if (song == null) return;

        await _playSongLock.WaitAsync();
        var requestVersion = Interlocked.Increment(ref _playRequestVersion);

        try
        {
            var localFilePath = song.LocalFilePath;
            var isLocalSong = !string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath);

            if (!isLocalSong &&
                (string.IsNullOrEmpty(_sessionManager.Session.Token) || _sessionManager.Session.UserId == "0"))
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("请先登录")
                    .WithContent("登录后才能播放音乐")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("熔断保护")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .WithContent("连续多次失败，停止播放")
                    .Queue();
                _consecutiveFailures = 0;
                return;
            }

            CancelAndDisposeLoadCancellation();
            var currentLoadCts = new CancellationTokenSource();
            _loadCancellation = currentLoadCts;

            _queueManager.SetupQueue(song, contextList);

            if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
            CurrentPlayingSong = song;
            CurrentPlayingSong.IsPlaying = true;

            IsLiked = _favoriteService.IsLiked(song.Hash);

            StopAndReset();

            string? url;
            if (isLocalSong)
            {
                url = localFilePath;
                _ = _lyricsService.LoadLocalLyricsAsync(localFilePath!);
            }
            else
            {
                var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
                if (playData == null || playData.Status != 1)
                {
                    HandlePlayError(song);
                    return;
                }

                url = playData.Urls?.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                _ = _lyricsService.LoadOnlineLyricsAsync(song.Hash, song.Name);
            }

            if (requestVersion != _playRequestVersion || currentLoadCts.IsCancellationRequested) return;

            var loadSuccess = url != null &&
                              await TryLoadStreamAsync(url, song.Name, AudioLoadTimeout, currentLoadCts.Token);

            if (loadSuccess)
            {
                if (requestVersion != _playRequestVersion || currentLoadCts.IsCancellationRequested)
                {
                    _player.Stop();
                    return;
                }

                _consecutiveFailures = 0;
                _player.SetVolume(MusicVolume);
                _player.Play();
                IsPlayingAudio = true;
                TotalDurationSeconds =
                    song.DurationSeconds > 0 ? song.DurationSeconds : _player.GetDuration().TotalSeconds;
                _playbackTimer.Start();
                CurrentLyricText = "歌词加载中...";
            }
            else
            {
                HandlePlayError(song);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"播放出错: {ex.Message}");
            StopAndReset();
        }
        finally
        {
            _playSongLock.Release();
        }
    }

    private async Task<bool> TryLoadStreamAsync(
        string url,
        string songName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var loadTask = Task.Run(() => _player.Load(url), cancellationToken);
            var completed = await Task.WhenAny(loadTask, Task.Delay(timeout, cancellationToken));
            if (completed != loadTask)
            {
                if (cancellationToken.IsCancellationRequested) return false;
                _logger.LogWarning("加载歌曲超时: {SongName}, timeout={Timeout}s", songName, timeout.TotalSeconds);
                return false;
            }

            return await loadTask;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void CancelAndDisposeLoadCancellation()
    {
        var cts = Interlocked.Exchange(ref _loadCancellation, null);
        if (cts == null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被其他路径释放时忽略，保证退出流程稳定
        }

        cts.Dispose();
    }

    private void HandlePlayError(SongItem song)
    {
        _consecutiveFailures++;
        _logger.LogWarning($"加载失败 ({_consecutiveFailures}/{MaxConsecutiveFailures}): {song.Name}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
        });
    }

    [RelayCommand]
    private async Task PlaySong(SongItem? song)
    {
        await PlaySongAsync(song);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            IsPlayingAudio = false;
            _playbackTimer.Stop();
        }
        else
        {
            _player.Play();
            IsPlayingAudio = true;
            _playbackTimer.Start();
        }
    }

    [RelayCommand]
    private async Task PlayNext()
    {
        await PlaySongAsync(_queueManager.GetNext(CurrentPlayingSong));
    }

    [RelayCommand]
    private async Task PlayPrevious()
    {
        await PlaySongAsync(_queueManager.GetPrevious(CurrentPlayingSong));
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        _queueManager.ToggleShuffle(CurrentPlayingSong);
        OnPropertyChanged(nameof(IsShuffleMode)); // 通知 UI 更新随机图标
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _queueManager.Clear();
        StopAndReset();
    }

    [RelayCommand]
    private void RemoveFromQueue(SongItem song)
    {
        _queueManager.Remove(song);
        if (_queueManager.PlaybackQueue.Count == 0) StopAndReset();
    }

    [RelayCommand]
    private async Task ToggleLike()
    {
        if (CurrentPlayingSong == null) return;
        IsLiked = await _favoriteService.ToggleLikeAsync(CurrentPlayingSong, IsLiked);
    }

    private void StopAndReset()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
        _lyricsService.Clear();
    }

    private void OnPlaybackEnded()
    {
        Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped || IsDraggingProgress) return;

        IsBuffering = _player.IsStalled;

        var pos = _player.GetPosition();
        CurrentPositionSeconds = pos.TotalSeconds;

        var activeLine = _lyricsService.SyncLyrics(pos.TotalMilliseconds);
        if (activeLine != CurrentLyricLine)
        {
            CurrentLyricLine = activeLine;
            CurrentLyricText = activeLine?.Content ?? "暂无歌词";
            CurrentLyricTrans = activeLine?.Translation ?? "";
        }
    }

    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5) return;
        _player.SetPosition(TimeSpan.FromSeconds(value));
        _lyricsService.SyncLyrics(value * 1000);
    }

    partial void OnMusicVolumeChanged(float value)
    {
        _player.SetVolume(value);
    }

    partial void OnMusicQualityChanged(string value)
    {
        SettingsManager.Settings.MusicQuality = value;
        SettingsManager.Save();

        if (!string.Equals(QualitySelection, value, StringComparison.OrdinalIgnoreCase))
            QualitySelection = value;
    }

    partial void OnQualitySelectionChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (string.Equals(value, MusicQuality, StringComparison.OrdinalIgnoreCase))
            return;

        _ = SwitchQualityAsync(value);
    }

    // 暴露给 MainWindow 初始化时调用
    public async Task LoadLikeListAsync()
    {
        await _favoriteService.LoadLikeListAsync();
    }


    public void ApplyCustomEQ(float[] gains)
    {
        _player.SetEQ(gains);
        OnPropertyChanged(nameof(MusicQuality));
    }

    public void UpdateAudioEffects(string preset, bool surround)
    {
        if (preset == "自定义")
            _player.SetEQ(SettingsManager.Settings.CustomEqGains);
        else
            _player.SetEQ(GetEqPreset(preset));
        _player.SetSurround(surround);
    }

    public async Task<bool> SwitchQualityAsync(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || !QualityOptions.Contains(quality, StringComparer.OrdinalIgnoreCase))
            return false;

        if (string.Equals(MusicQuality, quality, StringComparison.OrdinalIgnoreCase))
            return true;

        var currentSong = CurrentPlayingSong;
        if (currentSong == null)
        {
            MusicQuality = quality;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(currentSong.LocalFilePath) && File.Exists(currentSong.LocalFilePath))
        {
            MusicQuality = quality;
            return true;
        }

        await _playSongLock.WaitAsync();
        IsSwitchingQuality = true;
        try
        {
            currentSong = CurrentPlayingSong;
            if (currentSong == null)
            {
                MusicQuality = quality;
                return true;
            }

            var playData = await _musicClient.GetPlayInfoAsync(currentSong.Hash, quality);
            if (playData == null || playData.Status != 1)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("切换音质失败")
                    .WithContent("当前音质暂不可用，已保持原音质。")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return false;
            }

            var url = playData.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (string.IsNullOrWhiteSpace(url))
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("切换音质失败")
                    .WithContent("没有获取到新的播放地址。")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return false;
            }

            var wasPlaying = _player.IsPlaying;
            var resumePosition = CurrentPositionSeconds;

            CancelAndDisposeLoadCancellation();
            var currentLoadCts = new CancellationTokenSource();
            _loadCancellation = currentLoadCts;

            _playbackTimer.Stop();
            _player.Stop();

            var loadSuccess = await TryLoadStreamAsync(url, currentSong.Name, AudioLoadTimeout, currentLoadCts.Token);
            if (!loadSuccess)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("切换音质失败")
                    .WithContent("新的音频流加载失败。")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return false;
            }

            MusicQuality = quality;
            _player.SetVolume(MusicVolume);
            TotalDurationSeconds =
                currentSong.DurationSeconds > 0 ? currentSong.DurationSeconds : _player.GetDuration().TotalSeconds;

            var safePosition = Math.Clamp(resumePosition, 0, Math.Max(TotalDurationSeconds - 0.25, 0));
            _player.SetPosition(TimeSpan.FromSeconds(safePosition));
            CurrentPositionSeconds = safePosition;

            var activeLine = _lyricsService.SyncLyrics(safePosition * 1000);
            CurrentLyricLine = activeLine;
            CurrentLyricText = activeLine?.Content ?? CurrentLyricText;
            CurrentLyricTrans = activeLine?.Translation ?? CurrentLyricTrans;

            if (wasPlaying)
            {
                _player.Play();
                _playbackTimer.Start();
                IsPlayingAudio = true;
            }
            else
            {
                IsPlayingAudio = false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换音质失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("切换音质失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return false;
        }
        finally
        {
            IsSwitchingQuality = false;
            _playSongLock.Release();
        }
    }

    private float[] GetEqPreset(string preset)
    {
        return preset switch
        {
            "流行" => [-2f, 0f, -5.0f, -1.0f, 0f, 0.0f, 0f, -3.0f, 0f, 0f],
            "摇滚" => [4.0f, 1.0f, -2.0f, 0f, 0f, -2.0f, 0f, -2.0f, 1.0f, 4.0f],
            "爵士" => [0f, 0f, 0f, -1.0f, -1.0f, -3.0f, 0f, 0f, 0f, 0f],
            "古典" => [0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 3.0f, 1.0f, 6.0f, 2.0f, 6.0f],
            "嘻哈" => [3.0f, 0f, -3.0f, 0f, 0f, -3.0f, 0f, 0.0f, 0f, 2.0f],
            "布鲁斯" => [2.0f, 2.0f, -6.0f, -2.0f, 3.0f, 1.0f, 0f, 1.0f, 0.0f, 2.0f],
            "电子音乐" => [3.0f, 1.0f, -1.0f, 0f, 0f, -3.0f, 0f, 0f, 0f, 0f],
            "金属" => [2.0f, 0f, 0f, -1.0f, -1.0f, -4.0f, 0f, 0f, 0f, 0f],
            _ => [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]
        };
    }
}
