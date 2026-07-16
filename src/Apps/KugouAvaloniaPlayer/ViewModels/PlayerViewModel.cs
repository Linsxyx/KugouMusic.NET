using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaLyrics;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private const int MaxConsecutiveFailures = 5;
    private const float VolumeStep = 0.05f;
    private static readonly string[] PlaybackSpeedOptionLabels = ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x"];
    private const double AnalysisWindowSec = 15.0;
    private const double FallbackMixDurationSec = 6.8;
    private const double FallbackMixEntrySec = 4.6;
    private const double PreloadWindowSec = 18.0;
    private static readonly TimeSpan SeamlessVisualSwitchDelay = TimeSpan.FromSeconds(2);
    private const int TailTelemetryCapacity = 320;
    private static readonly TimeSpan LocalAudioLoadTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RemoteAudioLoadTimeout = TimeSpan.FromSeconds(60);
    private readonly PlaybackAudioEffectsService _audioEffectsService;
    private readonly FavoritePlaylistService _favoriteService;
    private readonly PlaybackHistoryService _historyService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricsService _lyricsService;
    private readonly PersonalFmService _personalFmService;
    private readonly PlaybackQueueCacheService _queueCacheService;
    private readonly DispatcherTimer _playbackTimer;
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IPlaybackSourceResolver _playbackSourceResolver;
    private readonly ISystemMediaSessionService _systemMediaSessionService;
    private readonly PlaybackVisualizerService _visualizerService;

    private readonly DualTrackAudioPlayer _player;
    private CancellationTokenSource? _queueCacheSaveCancellation;
    private readonly SemaphoreSlim _playSongLock = new(1, 1);

    private readonly PlaybackQueueManager _queueManager;
    private readonly List<PlaybackTelemetryPoint> _tailTelemetry = [];
    private readonly ISukiToastManager _toastManager;
    private readonly ITransitionAnalysisService _transitionAnalysisService;
    private TransitionProfile? _activeTransitionProfile;
    private string? _analysisFailureSongKey;
    private bool _autoTransitionStarted;
    private CancellationTokenSource? _delayedVisualSwitchCancellation;
    [ObservableProperty]
    public partial SongItem? DisplayedPlayingSong { get; set; }

    private bool _isDelayingVisualSwitch;
    private int _lyricsLoadVersion;

    private int _consecutiveFailures;
    private bool _isRestoringCachedQueue;

    [ObservableProperty]
    public partial LyricLineViewModel? CurrentLyricLine { get; set; }

    [ObservableProperty]
    public partial int CurrentLyricIndex { get; set; } = -1;

    [ObservableProperty]
    public partial LyricLineViewModel? NextLyricLine { get; set; }

    [ObservableProperty]
    public partial SongItem? CurrentPlayingSong { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPosition))]
    public partial double CurrentPositionSeconds { get; set; }

    private int _disposeState;
    private bool _isAnalyzingTransition;
    [ObservableProperty]
    public partial bool IsBuffering { get; set; }

    [ObservableProperty]
    public partial bool IsDraggingProgress { get; set; }

    [ObservableProperty]
    public partial bool IsLiked { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlayingVisualizerEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsPlayingAudio { get; set; }

    private bool _isPreparingNextTrack;
    [ObservableProperty]
    public partial bool IsSeamlessTransitionEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSwitchingQuality { get; set; }

    private bool _isSyncingQualitySelection;
    private CancellationTokenSource? _loadCancellation;
    [ObservableProperty]
    public partial string MusicQuality { get; set; } = AudioQuality.Default;

    [ObservableProperty]
    public partial float PlaybackSpeed { get; set; } = 1.0f;

    [ObservableProperty]
    public partial double NowPlayingArtworkOpacity { get; set; } = 1;

    [ObservableProperty]
    public partial double NowPlayingArtworkTranslateY { get; set; }

    [ObservableProperty]
    public partial double NowPlayingLyricsOpacity { get; set; } = 1;

    [ObservableProperty]
    public partial double NowPlayingLyricsTranslateY { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMuted))]
    public partial float MusicVolume { get; set; }

    private float _memorizedVolume;

    public bool IsMuted => MusicVolume == 0;

    public void ToggleMute(bool? isMute = null) {
        isMute ??= !IsMuted;
        if (isMute is true) {
            if (MusicVolume != 0)
                _memorizedVolume = MusicVolume;
            MusicVolume = 0;
        } else
            MusicVolume = _memorizedVolume;
    }

    private TransitionProfile? _pendingTransitionProfile;
    private SongItem? _pendingTransitionSong;
    [ObservableProperty]
    public partial string? PreparedNextCover { get; set; }

    private int _playRequestVersion;
    private bool _preparedNextIsLocal;
    private SongItem? _preparedNextSong;
    private string? _preparedNextSource;
    private PreparedTrack? _preparedNextTrack;
    private string? _prepareFailureSongKey;
    [ObservableProperty]
    public partial string PlaybackSpeedSelection { get; set; } = "1x";
    [ObservableProperty]
    public partial string QualitySelection { get; set; } = AudioQuality.Default;

    [ObservableProperty]
    public partial double TotalDurationSeconds { get; set; }

    private CancellationTokenSource? _transitionWorkCancellation;
    private bool _isSyncingPlaybackSpeedSelection;

    public PlayerViewModel(
        ISukiToastManager toastManager, ILogger<PlayerViewModel> logger,
        PlaybackQueueManager queueManager, LyricsService lyricsService, FavoritePlaylistService favoriteService,
        PlaybackHistoryService historyService,
        PersonalFmService personalFmService, PlaybackQueueCacheService queueCacheService,
        PlaybackAudioEffectsService audioEffectsService,
        PlaybackVisualizerService visualizerService,
        ITransitionAnalysisService transitionAnalysisService, IPlaybackSourceResolver playbackSourceResolver,
        IPlaybackCoordinator playbackCoordinator, ISystemMediaSessionService systemMediaSessionService)
    {
        _toastManager = toastManager;
        _logger = logger;
        _queueManager = queueManager;
        _lyricsService = lyricsService;
        _favoriteService = favoriteService;
        _historyService = historyService;
        _personalFmService = personalFmService;
        _queueCacheService = queueCacheService;
        _audioEffectsService = audioEffectsService;
        _visualizerService = visualizerService;
        _transitionAnalysisService = transitionAnalysisService;
        _playbackSourceResolver = playbackSourceResolver;
        _playbackCoordinator = playbackCoordinator;
        _systemMediaSessionService = systemMediaSessionService;

        _player = playbackCoordinator.Player;
        _player.PlaybackEnded += OnPlaybackEnded;
        MusicQuality = SettingsManager.Settings.MusicQuality;
        PlaybackSpeed = Math.Clamp(SettingsManager.Settings.PlaybackSpeed, 0.5f, 2.0f);
        IsSeamlessTransitionEnabled = SettingsManager.Settings.EnableSeamlessTransition;
        IsNowPlayingVisualizerEnabled = SettingsManager.Settings.EnableNowPlayingVisualizer;
        MusicVolume = Math.Clamp(SettingsManager.Settings.MusicVolume, 0f, 1f);
        QualitySelection = MusicQuality;
        PlaybackSpeedSelection = FormatPlaybackSpeedSelection(PlaybackSpeed);
        UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);
        _audioEffectsService.Initialize(SettingsManager.Settings.EnableVolumeNormalization);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (_, m) =>
            {
                if (!AddSongToPersonalFmNext(m.Song))
                    if (m.Song == CurrentPlayingSong) {
                        _toastManager.CreateToast()
                                     .OfType(NotificationType.Error)
                                     .WithTitle("添加失败")
                                     .WithContent("当前正在播放的歌曲不能添加到下一首")
                                     .Dismiss()
                                     .After(TimeSpan.FromSeconds(3))
                                     .Dismiss()
                                     .ByClicking()
                                     .Queue();
                        return;
                    }

                _queueManager.AddToNext(m.Song, CurrentPlayingSong);
            });
        WeakReferenceMessenger.Default.Register<AddLoadedSongsToQueueMessage>(this,
            (_, m) => AddLoadedSongsToQueue(m.Songs));
        WeakReferenceMessenger.Default.Register<ShowSongBatchActionDialogMessage>(this,
            (_, m) => _ = ShowSongBatchActionDialogSafelyAsync(m.Songs, m.AllowAddToPlaylist));
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            (_, m) => _ = ShowPlaylistDialogSafelyAsync(m.Song));
        WeakReferenceMessenger.Default.Register<ReplacePlaybackQueueMessage>(this,
            (_, m) => _ = ReplacePlaybackQueueAsync(m.Songs, m.StartSong));
        WeakReferenceMessenger.Default.Register<PlaybackControlMessage>(this,
            (_, m) => HandlePlaybackControlMessage(m.Action));

        _queueManager.PlaybackQueue.CollectionChanged += OnPlaybackQueueCollectionChanged;
        _personalFmService.StateChanged += OnPersonalFmServiceStateChanged;
        PersonalFmStateChanged += SyncDisplayPlaybackQueue;
        SyncDisplayPlaybackQueue();
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<SongItem> DisplayPlaybackQueue { get; } = new();
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;
    public AvaloniaList<LyricLine> RenderLyricLines => _lyricsService.RenderLyricLines;
    public VisualizerBandState[] NowPlayingVisualizerBars => _visualizerService.Bars;
    public event Action? VisualizerUpdated
    {
        add => _visualizerService.Updated += value;
        remove => _visualizerService.Updated -= value;
    }
    public string[] QualityOptions { get; } = AudioQuality.Ordered.AsValueEnumerable().ToArray();
    public string[] PlaybackSpeedOptions { get; } = PlaybackSpeedOptionLabels;
    public int DisplayPlaybackQueueCount => DisplayPlaybackQueue.Count;
    public bool HasDisplayPlaybackQueue => DisplayPlaybackQueue.Count > 0;
    public TimeSpan CurrentPosition => TimeSpan.FromSeconds(CurrentPositionSeconds);

    public bool IsRepeatOneMode => _queueManager.IsRepeatOneMode;
    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void ChangeVolume(float delta)
    {
        MusicVolume = Math.Clamp(MusicVolume + delta, 0f, 1f);
    }

    public void IncreaseVolume()
    {
        ChangeVolume(VolumeStep);
    }

    public void DecreaseVolume()
    {
        ChangeVolume(-VolumeStep);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1) return;

        CancelAndDisposeLoadCancellation();
        CancelAndDisposeDelayedVisualSwitchCancellation();
        CancelAndDisposeTransitionCancellation();
        CancelAndDisposeQueueCacheSaveCancellation();
        ClearPersonalFmSession();
        _playSongLock.Dispose();
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _queueManager.PlaybackQueue.CollectionChanged -= OnPlaybackQueueCollectionChanged;
        _personalFmService.StateChanged -= OnPersonalFmServiceStateChanged;
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
                    .WithContent("连续多次失败，停止播放，建议重新登录")
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

            var normalizationGain =
                await _audioEffectsService.ResolveNormalizationGainAsync(sourceInfo.Source, sourceInfo.IsLocal, song.DurationSeconds,
                    currentLoadCts.Token);

            var loadSuccess =
                await _playbackCoordinator.LoadAsync(
                    sourceInfo.Source,
                    song.Name,
                    normalizationGain,
                    GetAudioLoadTimeout(sourceInfo.IsLocal),
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
                _ = _historyService.RecordPlayedAsync(song);
                _ = EnsurePreparedNextTrackAsync(requestVersion, currentLoadCts.Token);
            }
            else
            {
                HandlePlayError(song, requestVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放出错");
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
        _logger.LogWarning(
            "加载失败 ({ConsecutiveFailures}/{MaxConsecutiveFailures}): {SongName}. Detail={Detail}",
            _consecutiveFailures,
            MaxConsecutiveFailures,
            song.Name,
            _player.LastErrorDetail ?? "n/a");
        _toastManager.CreateToast()
            .OfType(NotificationType.Warning)
            .WithTitle("加载失败")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .WithContent($"{song.Name}")
            .Queue();
        Dispatcher.UIThread.Post(() =>
        {
            var currentIndex = PlaybackQueue.IndexOf(song);
            if (currentIndex >= 0)
            {
                PlaybackQueue.RemoveAt(currentIndex);
                _logger.LogInformation("已从队列中移除失败歌曲: {SongName}", song.Name);
            }
            if (PlaybackQueue.Count > 0)
            {
                PlayNextCommand.Execute(null);
            }
            else
            {
                StopAndReset();
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("播放失败")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .WithContent("队列中没有可播放的歌曲")
                    .Queue();
            }
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

    private void HandlePlaybackControlMessage(PlaybackControlAction action)
    {
        switch (action)
        {
            case PlaybackControlAction.TogglePlayPause:
                TogglePlayPause();
                break;
            case PlaybackControlAction.PreviousTrack:
                _ = PlayPrevious();
                break;
            case PlaybackControlAction.NextTrack:
                _ = PlayNext();
                break;
        }
    }

    private static TimeSpan GetAudioLoadTimeout(bool isLocal)
    {
        return isLocal ? LocalAudioLoadTimeout : RemoteAudioLoadTimeout;
    }

}
