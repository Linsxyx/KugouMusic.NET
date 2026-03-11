using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private const int MaxConsecutiveFailures = 5;
    private readonly FavoritePlaylistService _favoriteService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricsService _lyricsService;
    private readonly MusicClient _musicClient;
    private readonly DispatcherTimer _playbackTimer;

    private readonly SimpleAudioPlayer _player;

    private readonly PlaybackQueueManager _queueManager;
    private readonly ISukiToastManager _toastManager;

    private int _consecutiveFailures;

    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;
    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";
    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;
    [ObservableProperty] private bool _isDraggingProgress;
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isPlayingAudio;
    [ObservableProperty] private string _musicQuality = "128";
    [ObservableProperty] private float _musicVolume = 1.0f;
    [ObservableProperty] private double _totalDurationSeconds;

    public PlayerViewModel(
        MusicClient musicClient, ISukiToastManager toastManager, ILogger<PlayerViewModel> logger,
        PlaybackQueueManager queueManager, LyricsService lyricsService, FavoritePlaylistService favoriteService)
    {
        _musicClient = musicClient;
        _toastManager = toastManager;
        _logger = logger;
        _queueManager = queueManager;
        _lyricsService = lyricsService;
        _favoriteService = favoriteService;

        _player = new SimpleAudioPlayer();
        _player.PlaybackEnded += OnPlaybackEnded;

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (r, m) => { _queueManager.AddToNext(m.Song, CurrentPlayingSong); });
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            async (r, m) => { await _favoriteService.ShowAddToPlaylistDialogAsync(m.Song); });
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;

    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void Dispose()
    {
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

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _toastManager.CreateToast().OfType(NotificationType.Error).WithTitle("熔断保护").WithContent("连续多次失败，停止播放")
                .Queue();
            return;
        }

        // 交给队列管理器处理
        _queueManager.SetupQueue(song, contextList);

        if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
        CurrentPlayingSong = song;
        CurrentPlayingSong.IsPlaying = true;

        // 检查喜欢状态
        IsLiked = _favoriteService.IsLiked(song.Hash);

        StopAndReset();

        try
        {
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

                url = playData.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                _ = _lyricsService.LoadOnlineLyricsAsync(song.Hash, song.Name);
            }

            if (url != null && _player.Load(url))
            {
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

    // ================= 播放控制命令 =================
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

    // ================= 内部状态联动 =================
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
        var pos = _player.GetPosition();
        CurrentPositionSeconds = pos.TotalSeconds;

        // 让歌词服务处理高亮，返回当前行
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
}