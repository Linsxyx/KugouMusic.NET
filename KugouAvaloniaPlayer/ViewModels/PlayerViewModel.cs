using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private const int MaxConsecutiveFailures = 5;
    private const int VisualizerBarCount = 72;
    private const double VisualizerMinHeight = 18;
    private const double VisualizerHeightRange = 245;
    private const double AnalysisWindowSec = 15.0;
    private const double FallbackMixDurationSec = 6.8;
    private const double FallbackMixEntrySec = 4.6;
    private const double PreloadWindowSec = 18.0;
    private static readonly TimeSpan SeamlessVisualSwitchDelay = TimeSpan.FromSeconds(2);
    private const int TailTelemetryCapacity = 320;
    private static readonly TimeSpan AudioLoadTimeout = TimeSpan.FromSeconds(12);
    private readonly RecommendClient _discoveryClient;
    private readonly FavoritePlaylistService _favoriteService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricsService _lyricsService;
    private readonly DispatcherTimer _playbackTimer;
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IPlaybackSourceResolver _playbackSourceResolver;

    private readonly DualTrackAudioPlayer _player;
    private readonly SemaphoreSlim _playSongLock = new(1, 1);

    private readonly PlaybackQueueManager _queueManager;
    private readonly List<PlaybackTelemetryPoint> _tailTelemetry = [];
    private readonly ISukiToastManager _toastManager;
    private readonly ITransitionAnalysisService _transitionAnalysisService;
    private TransitionProfile? _activeTransitionProfile;
    private string? _analysisFailureSongKey;
    private bool _autoTransitionStarted;
    private CancellationTokenSource? _delayedVisualSwitchCancellation;
    [ObservableProperty] private SongItem? _displayedPlayingSong;
    private bool _isDelayingVisualSwitch;
    private int _lyricsLoadVersion;

    private int _consecutiveFailures;

    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;
    [ObservableProperty] private int _currentLyricIndex = -1;
    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";
    [ObservableProperty] private LyricLineViewModel? _nextLyricLine;
    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;
    private int _disposeState;
    private bool _isAnalyzingTransition;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isDraggingProgress;
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isNowPlayingVisualizerEnabled;
    [ObservableProperty] private bool _isPlayingAudio;
    private bool _isPreparingNextTrack;
    [ObservableProperty] private bool _isSeamlessTransitionEnabled;
    [ObservableProperty] private bool _isSwitchingQuality;
    private bool _isSyncingQualitySelection;
    private CancellationTokenSource? _loadCancellation;
    [ObservableProperty] private string _musicQuality = "128";
    [ObservableProperty] private double _nowPlayingArtworkOpacity = 1;
    [ObservableProperty] private double _nowPlayingArtworkTranslateY;
    [ObservableProperty] private double _nowPlayingLyricsOpacity = 1;
    [ObservableProperty] private double _nowPlayingLyricsTranslateY;
    [ObservableProperty] private float _musicVolume = 0.8f;
    private TransitionProfile? _pendingTransitionProfile;
    private SongItem? _pendingTransitionSong;
    [ObservableProperty] private string? _preparedNextCover;
    private int _playRequestVersion;
    private bool _preparedNextIsLocal;
    private SongItem? _preparedNextSong;
    private string? _preparedNextSource;
    private PreparedTrack? _preparedNextTrack;
    private string? _prepareFailureSongKey;
    [ObservableProperty] private string _qualitySelection = "128";
    [ObservableProperty] private double _totalDurationSeconds;
    private CancellationTokenSource? _transitionWorkCancellation;

    public PlayerViewModel(
        RecommendClient discoveryClient, ISukiToastManager toastManager, ILogger<PlayerViewModel> logger,
        PlaybackQueueManager queueManager, LyricsService lyricsService, FavoritePlaylistService favoriteService,
        ITransitionAnalysisService transitionAnalysisService, IPlaybackSourceResolver playbackSourceResolver,
        IPlaybackCoordinator playbackCoordinator)
    {
        _discoveryClient = discoveryClient;
        _toastManager = toastManager;
        _logger = logger;
        _queueManager = queueManager;
        _lyricsService = lyricsService;
        _favoriteService = favoriteService;
        _transitionAnalysisService = transitionAnalysisService;
        _playbackSourceResolver = playbackSourceResolver;
        _playbackCoordinator = playbackCoordinator;

        _player = playbackCoordinator.Player;
        _player.PlaybackEnded += OnPlaybackEnded;
        MusicQuality = SettingsManager.Settings.MusicQuality;
        IsSeamlessTransitionEnabled = SettingsManager.Settings.EnableSeamlessTransition;
        IsNowPlayingVisualizerEnabled = SettingsManager.Settings.EnableNowPlayingVisualizer;
        QualitySelection = MusicQuality;
        UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (_, m) =>
            {
                if (!AddSongToPersonalFmNext(m.Song))
                    _queueManager.AddToNext(m.Song, CurrentPlayingSong);
            });
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            (_, m) => _ = ShowPlaylistDialogSafelyAsync(m.Song));

        _queueManager.PlaybackQueue.CollectionChanged += OnPlaybackQueueCollectionChanged;
        PersonalFmStateChanged += SyncDisplayPlaybackQueue;
        SyncDisplayPlaybackQueue();
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<SongItem> DisplayPlaybackQueue { get; } = new();
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;
    public AvaloniaList<AudioVisualizerBarViewModel> NowPlayingVisualizerBars { get; } = CreateVisualizerBars();
    public string[] QualityOptions { get; } = ["128", "320", "flac", "high"];
    public int DisplayPlaybackQueueCount => DisplayPlaybackQueue.Count;

    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1) return;

        CancelAndDisposeLoadCancellation();
        CancelAndDisposeDelayedVisualSwitchCancellation();
        CancelAndDisposeTransitionCancellation();
        ClearPersonalFmSession();
        _playSongLock.Dispose();
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _queueManager.PlaybackQueue.CollectionChanged -= OnPlaybackQueueCollectionChanged;
        PersonalFmStateChanged -= SyncDisplayPlaybackQueue;
        _queueManager.Clear();
        _lyricsService.Clear();
        GC.SuppressFinalize(this);
    }

    public async Task PlaySongAsync(SongItem? song, IList<SongItem>? contextList = null, bool preservePersonalFmSession = false)
    {
        if (song == null) return;

        if (!preservePersonalFmSession && IsPersonalFmSessionActive)
            ClearPersonalFmSession();

        await _playSongLock.WaitAsync();
        var requestVersion = Interlocked.Increment(ref _playRequestVersion);

        try
        {
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
            CancelAndDisposeDelayedVisualSwitchCancellation();
            var currentLoadCts = new CancellationTokenSource();
            _loadCancellation = currentLoadCts;

            var sourceInfo = await ResolvePlaybackSourceAsync(song, currentLoadCts.Token);
            if (!sourceInfo.Success || string.IsNullOrWhiteSpace(sourceInfo.Source))
            {
                ShowPlaybackSourceFailure(sourceInfo.FailureReason);
                CancelAndDisposeLoadCancellation();
                if (sourceInfo.FailureReason == PlaybackSourceFailureReason.LoginRequired)
                    return;

                HandlePlayError(song, requestVersion);
                return;
            }

            _queueManager.SetupQueue(song, contextList);
            ResetTransitionPipeline(true);
            ResetTailTelemetry();

            if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
            CurrentPlayingSong = song;
            CurrentPlayingSong.IsPlaying = true;
            DisplayedPlayingSong = song;

            IsLiked = _favoriteService.IsLiked(song.Hash);

            StopAndReset();

            StartLyricsLoad(song, sourceInfo.IsLocal);

            if (requestVersion != _playRequestVersion || currentLoadCts.IsCancellationRequested) return;

            var loadSuccess =
                await _playbackCoordinator.LoadAsync(sourceInfo.Source, song.Name, AudioLoadTimeout,
                    currentLoadCts.Token);

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
                _ = EnsurePreparedNextTrackAsync(requestVersion, currentLoadCts.Token);
            }
            else
            {
                HandlePlayError(song, requestVersion);
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

    private void CancelAndDisposeLoadCancellation()
    {
        var cts = Interlocked.Exchange(ref _loadCancellation, null);
        if (cts == null) return;

        try
        {
            cts.Cancel();
            _playbackCoordinator.InvalidatePendingLoads();
        }
        catch (ObjectDisposedException)
        {
            // 已被其他路径释放时忽略，保证退出流程稳定
        }

        cts.Dispose();
    }

    private void CancelAndDisposeDelayedVisualSwitchCancellation()
    {
        _isDelayingVisualSwitch = false;
        var cts = Interlocked.Exchange(ref _delayedVisualSwitchCancellation, null);
        if (cts == null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void CancelAndDisposeTransitionCancellation()
    {
        var cts = Interlocked.Exchange(ref _transitionWorkCancellation, null);
        if (cts == null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void HandlePlayError(SongItem song, int requestVersion)
    {
        _consecutiveFailures++;
        _logger.LogWarning($"加载失败 ({_consecutiveFailures}/{MaxConsecutiveFailures}): {song.Name}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (requestVersion != Volatile.Read(ref _playRequestVersion))
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (requestVersion != Volatile.Read(ref _playRequestVersion))
                    return;

                PlayNextCommand.Execute(null);
            });
        });
    }

    private void ShowPlaybackSourceFailure(PlaybackSourceFailureReason reason)
    {
        if (reason != PlaybackSourceFailureReason.LoginRequired)
            return;

        _toastManager.CreateToast()
            .OfType(NotificationType.Warning)
            .WithTitle("请先登录")
            .WithContent("登录后才能播放音乐")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

}
