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
    private CancellationTokenSource? _loadCancellation;
    [ObservableProperty] private string _musicQuality = "128";
    [ObservableProperty] private float _musicVolume = 1.0f;
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
        UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (_, m) => { _queueManager.AddToNext(m.Song, CurrentPlayingSong); });
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            async void (_, m) => { await _favoriteService.ShowAddToPlaylistDialogAsync(m.Song); });
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;

    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
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
            if (string.IsNullOrEmpty(_sessionManager.Session.Token) || _sessionManager.Session.UserId == "0")
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
                _toastManager.CreateToast().OfType(NotificationType.Error).WithTitle("熔断保护").WithContent("连续多次失败，停止播放")
                    .Queue();
                _consecutiveFailures = 0;
                return;
            }

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            var currentLoadCts = new CancellationTokenSource();
            _loadCancellation = currentLoadCts;

            _queueManager.SetupQueue(song, contextList);

            if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
            CurrentPlayingSong = song;
            CurrentPlayingSong.IsPlaying = true;

            IsLiked = _favoriteService.IsLiked(song.Hash);

            StopAndReset();

            string? url;
            if (File.Exists(song.LocalFilePath))
            {
                url = song.LocalFilePath;
                _ = _lyricsService.LoadLocalLyricsAsync(song.LocalFilePath);
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

    public float[] GetEqPreset(string preset)
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