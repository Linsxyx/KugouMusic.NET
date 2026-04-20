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
    private const int VisualizerBarCount = 28;
    private const double VisualizerMinHeight = 8;
    private const double VisualizerHeightRange = 56;
    private const double AnalysisWindowSec = 15.0;
    private const double FallbackMixDurationSec = 9.6;
    private const double FallbackMixEntrySec = 4.6;
    private const double PreloadWindowSec = 18.0;
    private const int TailTelemetryCapacity = 320;
    private static readonly TimeSpan AudioLoadTimeout = TimeSpan.FromSeconds(12);
    private readonly FavoritePlaylistService _favoriteService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricsService _lyricsService;
    private readonly MusicClient _musicClient;
    private readonly DispatcherTimer _playbackTimer;

    private readonly DualTrackAudioPlayer _player;
    private readonly SemaphoreSlim _playSongLock = new(1, 1);

    private readonly PlaybackQueueManager _queueManager;
    private readonly KgSessionManager _sessionManager;
    private readonly List<PlaybackTelemetryPoint> _tailTelemetry = [];
    private readonly ISukiToastManager _toastManager;
    private readonly ITransitionAnalysisService _transitionAnalysisService;
    private TransitionProfile? _activeTransitionProfile;
    private string? _analysisFailureSongKey;
    private bool _autoTransitionStarted;

    private int _consecutiveFailures;

    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;
    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";
    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;
    private int _disposeState;
    private bool _incomingSteadyStateLogged;
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
    [ObservableProperty] private float _musicVolume = 0.8f;
    private TransitionProfile? _pendingTransitionProfile;
    private SongItem? _pendingTransitionSong;
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
        MusicClient musicClient, ISukiToastManager toastManager, ILogger<PlayerViewModel> logger,
        PlaybackQueueManager queueManager, LyricsService lyricsService, FavoritePlaylistService favoriteService,
        KgSessionManager sessionManager, ITransitionAnalysisService transitionAnalysisService)
    {
        _musicClient = musicClient;
        _toastManager = toastManager;
        _logger = logger;
        _queueManager = queueManager;
        _lyricsService = lyricsService;
        _favoriteService = favoriteService;
        _sessionManager = sessionManager;
        _transitionAnalysisService = transitionAnalysisService;

        _player = new DualTrackAudioPlayer();
        _player.PlaybackEnded += OnPlaybackEnded;
        MusicQuality = SettingsManager.Settings.MusicQuality;
        IsSeamlessTransitionEnabled = SettingsManager.Settings.EnableSeamlessTransition;
        IsNowPlayingVisualizerEnabled = SettingsManager.Settings.EnableNowPlayingVisualizer;
        QualitySelection = MusicQuality;
        UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        WeakReferenceMessenger.Default.Register<AddToNextMessage>(this,
            (_, m) => { _queueManager.AddToNext(m.Song, CurrentPlayingSong); });
        WeakReferenceMessenger.Default.Register<ShowPlaylistDialogMessage>(this,
            (_, m) => _ = ShowPlaylistDialogSafelyAsync(m.Song));
    }

    public AvaloniaList<SongItem> PlaybackQueue => _queueManager.PlaybackQueue;
    public AvaloniaList<LyricLineViewModel> LyricLines => _lyricsService.LyricLines;
    public AvaloniaList<AudioVisualizerBarViewModel> NowPlayingVisualizerBars { get; } = CreateVisualizerBars();
    public string[] QualityOptions { get; } = ["128", "320", "flac", "high"];

    public bool IsShuffleMode => _queueManager.IsShuffleMode;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1) return;

        CancelAndDisposeLoadCancellation();
        CancelAndDisposeTransitionCancellation();
        _playSongLock.Dispose();
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
        _queueManager.Clear();
        _lyricsService.Clear();
        GC.SuppressFinalize(this);
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
            ResetTransitionPipeline(true);
            ResetTailTelemetry();

            if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
            CurrentPlayingSong = song;
            CurrentPlayingSong.IsPlaying = true;

            IsLiked = _favoriteService.IsLiked(song.Hash);

            StopAndReset();

            var sourceInfo = await ResolvePlaybackSourceAsync(song, currentLoadCts.Token);
            if (sourceInfo.Source == null)
            {
                HandlePlayError(song);
                return;
            }

            StartLyricsLoad(song, sourceInfo.IsLocal);

            if (requestVersion != _playRequestVersion || currentLoadCts.IsCancellationRequested) return;

            var loadSuccess =
                await TryLoadStreamAsync(sourceInfo.Source, song.Name, AudioLoadTimeout, currentLoadCts.Token);

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
            ResetVisualizerBars();
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
        ResetTransitionPipeline(true);
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentLyricLine = null;
        CurrentPositionSeconds = 0;
        _lyricsService.Clear();
        ResetTailTelemetry();
        ResetVisualizerBars();
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
        var analysisSnapshot = _player.GetActiveAnalysisSnapshot();
        CaptureTailTelemetry(analysisSnapshot);
        UpdateNowPlayingVisualizer(analysisSnapshot);

        var activeLine = _lyricsService.SyncLyrics(pos.TotalMilliseconds);
        if (activeLine != CurrentLyricLine)
        {
            CurrentLyricLine = activeLine;
            CurrentLyricText = activeLine?.Content ?? "暂无歌词";
            CurrentLyricTrans = activeLine?.Translation ?? "";
        }

        // 1->2 的交叉完成后，需要为新激活的歌曲重新打开下一轮预加载/分析管线。
        if (_autoTransitionStarted &&
            !_player.IsCrossfading &&
            _pendingTransitionProfile == null &&
            _pendingTransitionSong == null &&
            _preparedNextTrack == null &&
            _preparedNextSong == null)
        {
/*#if DEBUG
            if (CurrentPlayingSong != null)
                _logger.LogInformation(
                    "智能过渡结束：当前歌曲《{SongName}》交叉阶段已完全结束，当前播放位置 {CurrentPositionSec:F2}s",
                    CurrentPlayingSong.Name,
                    pos.TotalSeconds);
#endif*/
            ResetTransitionPipeline(false);
        }

/*#if DEBUG
        if (_autoTransitionStarted &&
            _player.IsCrossfading &&
            !_incomingSteadyStateLogged &&
            _activeTransitionProfile != null &&
            CurrentPlayingSong != null &&
            pos.TotalSeconds >= _activeTransitionProfile.IncomingSettleSec)
        {
            _incomingSteadyStateLogged = true;
            _logger.LogInformation(
                "智能过渡稳态：当前歌曲《{SongName}》已在 {CurrentPositionSec:F2}s 左右进入正常播放参数区（预测 {PredictedSettleSec:F2}s，交叉总时长 {MixDurationSec:F2}s）",
                CurrentPlayingSong.Name,
                pos.TotalSeconds,
                _activeTransitionProfile.IncomingSettleSec,
                _activeTransitionProfile.MixDurationSec);
        }
#endif*/

        if (!IsSeamlessTransitionEnabled || _player.IsCrossfading || IsSwitchingQuality) return;

        var remainingSec = Math.Max(0, TotalDurationSeconds - pos.TotalSeconds);
        var requestVersion = _playRequestVersion;
        var currentLoadCts = _loadCancellation;
        if (currentLoadCts == null || currentLoadCts.IsCancellationRequested) return;

        if (remainingSec <= PreloadWindowSec) _ = EnsurePreparedNextTrackAsync(requestVersion, currentLoadCts.Token);

        if (remainingSec <= AnalysisWindowSec) _ = EnsureTransitionAnalysisAsync(requestVersion, currentLoadCts.Token);

        TryStartAutoCrossfade(remainingSec);
    }

    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5) return;
        if (_player.IsCrossfading)
        {
            _player.AbortCrossfade();
            ResetTransitionPipeline(false);
        }

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
            SetQualitySelectionSilently(value);
    }

    partial void OnQualitySelectionChanged(string value)
    {
        if (_isSyncingQualitySelection)
            return;

        if (string.IsNullOrWhiteSpace(value))
            return;

        if (string.Equals(value, MusicQuality, StringComparison.OrdinalIgnoreCase))
            return;

        _ = SwitchQualityAsync(value);
    }

    private void SetQualitySelectionSilently(string value)
    {
        if (string.Equals(QualitySelection, value, StringComparison.OrdinalIgnoreCase))
            return;

        _isSyncingQualitySelection = true;
        try
        {
            QualitySelection = value;
        }
        finally
        {
            _isSyncingQualitySelection = false;
        }
    }

    private void RevertQualitySelectionToCurrentQuality()
    {
        SetQualitySelectionSilently(MusicQuality);
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

    private static AvaloniaList<AudioVisualizerBarViewModel> CreateVisualizerBars()
    {
        var bars = new AvaloniaList<AudioVisualizerBarViewModel>();
        for (var i = 0; i < VisualizerBarCount; i++) bars.Add(new AudioVisualizerBarViewModel());

        return bars;
    }

    private void ResetVisualizerBars()
    {
        for (var i = 0; i < NowPlayingVisualizerBars.Count; i++)
        {
            NowPlayingVisualizerBars[i].Height = VisualizerMinHeight;
            NowPlayingVisualizerBars[i].Opacity = 0.22;
        }
    }

    private void UpdateNowPlayingVisualizer(AudioAnalysisSnapshot snapshot)
    {
        var spectrumBands = snapshot.SpectrumBands;
        if (spectrumBands == null || spectrumBands.Count == 0)
        {
            ResetVisualizerBars();
            return;
        }

        var energyBoost = Math.Clamp(snapshot.Rms * 10.5, 0d, 1d);
        var brightnessBoost = Math.Clamp(snapshot.Brightness * 1.45, 0d, 1d);

        for (var i = 0; i < NowPlayingVisualizerBars.Count; i++)
        {
            var sourceIndex = Math.Min(i, spectrumBands.Count - 1);
            var band = Math.Clamp(spectrumBands[sourceIndex], 0f, 1f);
            var emphasis = 1d - Math.Abs(i / (NowPlayingVisualizerBars.Count - 1d) - 0.5d) * 0.22d;
            var target = Math.Clamp((band * 0.72d + energyBoost * 0.2d + brightnessBoost * 0.08d) * emphasis, 0d, 1d);
            var targetHeight = VisualizerMinHeight + target * VisualizerHeightRange;
            var bar = NowPlayingVisualizerBars[i];

            var smoothing = targetHeight >= bar.Height ? 0.58d : 0.18d;
            bar.Height += (targetHeight - bar.Height) * smoothing;
            bar.Opacity = Math.Clamp(0.24 + target * 0.76, 0.24, 1d);
        }
    }

    public void SetSeamlessTransitionEnabled(bool enabled)
    {
        if (IsSeamlessTransitionEnabled == enabled) return;

        IsSeamlessTransitionEnabled = enabled;
        if (!enabled)
        {
            _player.AbortCrossfade();
            ResetTransitionPipeline(true);
        }
    }

    public void SetNowPlayingVisualizerEnabled(bool enabled)
    {
        if (IsNowPlayingVisualizerEnabled == enabled) return;

        IsNowPlayingVisualizerEnabled = enabled;
        if (!enabled) ResetVisualizerBars();
    }

    public async Task<bool> SwitchQualityAsync(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || !QualityOptions.Contains(quality, StringComparer.OrdinalIgnoreCase))
        {
            RevertQualitySelectionToCurrentQuality();
            return false;
        }

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
                RevertQualitySelectionToCurrentQuality();
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
                RevertQualitySelectionToCurrentQuality();
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
            ResetTransitionPipeline(true);
            var currentLoadCts = new CancellationTokenSource();
            _loadCancellation = currentLoadCts;

            _playbackTimer.Stop();
            _player.Stop();

            var loadSuccess = await TryLoadStreamAsync(url, currentSong.Name, AudioLoadTimeout, currentLoadCts.Token);
            if (!loadSuccess)
            {
                RevertQualitySelectionToCurrentQuality();
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
            RevertQualitySelectionToCurrentQuality();
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

    private async Task<(string? Source, bool IsLocal)> ResolvePlaybackSourceAsync(SongItem song,
        CancellationToken cancellationToken)
    {
        var localFilePath = song.LocalFilePath;
        var isLocalSong = !string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath);
        if (isLocalSong) return (localFilePath, true);

        cancellationToken.ThrowIfCancellationRequested();
        var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
        if (playData == null || playData.Status != 1) return (null, false);

        var url = playData.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return (url, false);
    }

    private void StartLyricsLoad(SongItem song, bool isLocal)
    {
        if (isLocal && !string.IsNullOrWhiteSpace(song.LocalFilePath))
            _ = _lyricsService.LoadLocalLyricsAsync(song.LocalFilePath);
        else
            _ = _lyricsService.LoadOnlineLyricsAsync(song.Hash, song.Name);
    }

    private void ResetTransitionPipeline(bool cancelPreparedTrack)
    {
        CancelAndDisposeTransitionCancellation();
        _autoTransitionStarted = false;
        _isAnalyzingTransition = false;
        _isPreparingNextTrack = false;
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
        _preparedNextIsLocal = false;
        _activeTransitionProfile = null;
        _incomingSteadyStateLogged = false;
        _analysisFailureSongKey = null;
        _prepareFailureSongKey = null;
        if (cancelPreparedTrack)
        {
            _player.AbortCrossfade();
            _player.CancelPrepared();
        }
    }

    private void ResetTailTelemetry()
    {
        _tailTelemetry.Clear();
    }

    private void CaptureTailTelemetry(AudioAnalysisSnapshot snapshot)
    {
        if (snapshot.DurationSeconds <= 0 || snapshot.PositionSeconds < 0) return;

        if (_tailTelemetry.Count > 0 && snapshot.PositionSeconds + 0.15 < _tailTelemetry[^1].PositionSeconds)
            _tailTelemetry.Clear();

        _tailTelemetry.Add(new PlaybackTelemetryPoint(snapshot.PositionSeconds, snapshot.DurationSeconds, snapshot.Rms,
            snapshot.Brightness));

        while (_tailTelemetry.Count > TailTelemetryCapacity) _tailTelemetry.RemoveAt(0);

        var minPosition = Math.Max(0, snapshot.PositionSeconds - 15.0);
        _tailTelemetry.RemoveAll(x => x.PositionSeconds < minPosition);
    }

    private TailPlaybackMetrics? BuildTailPlaybackMetrics()
    {
        if (_tailTelemetry.Count == 0) return null;

        var points = _tailTelemetry.Where(x => x.PositionSeconds >= _tailTelemetry[^1].PositionSeconds - 8.0).ToList();
        if (points.Count == 0) return null;

        var peakRms = points.Max(x => x.Rms);
        var silenceThreshold = Math.Max(peakRms * 0.18, 0.0025);
        var lastActive = points.LastOrDefault(x => x.Rms >= silenceThreshold);
        var lastPoint = points[^1];
        var tailSilenceSec = lastActive.PositionSeconds <= 0
            ? 0
            : Math.Max(0, lastPoint.PositionSeconds - lastActive.PositionSeconds);
        var averageRms = points.Average(x => x.Rms);
        var averageBrightness = points.Average(x => x.Brightness);
        return new TailPlaybackMetrics
        {
            TailRms = averageRms,
            TailBrightness = averageBrightness,
            TailSilenceSec = tailSilenceSec
        };
    }

    private SongItem? GetUpcomingSong()
    {
        if (CurrentPlayingSong == null || _queueManager.PlaybackQueue.Count <= 1) return null;

        var nextSong = _queueManager.GetNext(CurrentPlayingSong);
        return nextSong == CurrentPlayingSong ? null : nextSong;
    }

    private async Task EnsurePreparedNextTrackAsync(int requestVersion, CancellationToken cancellationToken)
    {
        if (_autoTransitionStarted || _isPreparingNextTrack || _player.IsCrossfading) return;

        var nextSong = GetUpcomingSong();
        if (nextSong == null) return;

        var nextSongKey = BuildSongTransitionKey(nextSong);
        if (_preparedNextSong == nextSong && _player.HasPreparedTrack &&
            !string.IsNullOrWhiteSpace(_preparedNextSource)) return;

        if (string.Equals(_prepareFailureSongKey, nextSongKey, StringComparison.Ordinal)) return;

        _isPreparingNextTrack = true;
        try
        {
            var transitionCts = EnsureTransitionCancellation();
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transitionCts.Token);
            var sourceInfo = await ResolvePlaybackSourceAsync(nextSong, linkedCts.Token);
            if (requestVersion != _playRequestVersion || linkedCts.IsCancellationRequested ||
                string.IsNullOrWhiteSpace(sourceInfo.Source)) return;

            if (!_player.PrepareNext(sourceInfo.Source))
            {
                _prepareFailureSongKey = nextSongKey;
                return;
            }

            _prepareFailureSongKey = null;
            _preparedNextSong = nextSong;
            _preparedNextSource = sourceInfo.Source;
            _preparedNextIsLocal = sourceInfo.IsLocal;
            _preparedNextTrack = new PreparedTrack
            {
                Id = nextSongKey,
                Source = sourceInfo.Source,
                IsLocal = sourceInfo.IsLocal,
                DurationSeconds = nextSong.DurationSeconds
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预加载下一首失败");
            _prepareFailureSongKey = nextSongKey;
        }
        finally
        {
            _isPreparingNextTrack = false;
        }
    }

    private async Task EnsureTransitionAnalysisAsync(int requestVersion, CancellationToken cancellationToken)
    {
        if (_autoTransitionStarted || _isAnalyzingTransition || _player.IsCrossfading || _preparedNextTrack == null ||
            _preparedNextSong == null) return;

        var nextSongKey = BuildSongTransitionKey(_preparedNextSong);
        if (_pendingTransitionSong == _preparedNextSong && _pendingTransitionProfile != null) return;

        if (string.Equals(_analysisFailureSongKey, nextSongKey, StringComparison.Ordinal)) return;

        var currentSong = CurrentPlayingSong;
        var currentSource = _player.ActiveSource;
        if (currentSong == null || string.IsNullOrWhiteSpace(currentSource)) return;

        _isAnalyzingTransition = true;
        try
        {
            var transitionCts = EnsureTransitionCancellation();
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transitionCts.Token);
            var currentTrack = new PreparedTrack
            {
                Id = BuildSongTransitionKey(currentSong),
                Source = currentSource,
                IsLocal = !string.IsNullOrWhiteSpace(currentSong.LocalFilePath) &&
                          File.Exists(currentSong.LocalFilePath),
                DurationSeconds = TotalDurationSeconds,
                TailMetrics = BuildTailPlaybackMetrics()
            };

            var profile =
                await _transitionAnalysisService.AnalyzeAsync(currentTrack, _preparedNextTrack, linkedCts.Token);
            if (requestVersion != _playRequestVersion || linkedCts.IsCancellationRequested) return;

            _analysisFailureSongKey = null;
            _pendingTransitionSong = _preparedNextSong;
            _pendingTransitionProfile = profile;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "过渡分析失败，回退默认参数");
            _analysisFailureSongKey = nextSongKey;
            _pendingTransitionSong = _preparedNextSong;
            _pendingTransitionProfile = TransitionProfile.Default with
            {
                MixDurationSec = FallbackMixDurationSec,
                MixEntrySec = FallbackMixEntrySec
            };
        }
        finally
        {
            _isAnalyzingTransition = false;
        }
    }

    private void TryStartAutoCrossfade(double remainingSec)
    {
        if (_autoTransitionStarted || _pendingTransitionProfile == null || _preparedNextSong == null ||
            !_player.HasPreparedTrack) return;

        if (_pendingTransitionSong != _preparedNextSong) return;

        if (remainingSec > _pendingTransitionProfile.MixEntrySec) return;

        if (!_player.StartCrossfade(_pendingTransitionProfile)) return;
/*#if DEBUG
        var nextSong = _preparedNextSong;
        var transitionProfile = _pendingTransitionProfile;
        var outgoingSong = CurrentPlayingSong;
        if (nextSong != null)
            _logger.LogInformation(
                "智能过渡启动：上一首《{OutgoingSong}》剩余 {RemainingSec:F2}s 开始淡出，下一首《{IncomingSong}》预计在 {IncomingSettleSec:F2}s 左右进入正常播放参数区，交叉将在 {MixDurationSec:F2}s 左右完全结束（起混点 {MixEntrySec:F2}s）",
                outgoingSong?.Name ?? "未知歌曲",
                remainingSec,
                nextSong.Name,
                transitionProfile.IncomingSettleSec,
                transitionProfile.MixDurationSec,
                transitionProfile.MixEntrySec);
#endif*/
        _autoTransitionStarted = true;
        _activeTransitionProfile = _pendingTransitionProfile;
        _incomingSteadyStateLogged = false;
        var oldSong = CurrentPlayingSong;
        if (oldSong != null) oldSong.IsPlaying = false;

        CurrentPlayingSong = _preparedNextSong;
        CurrentPlayingSong.IsPlaying = true;
        IsLiked = _favoriteService.IsLiked(CurrentPlayingSong.Hash);
        CurrentPositionSeconds = 0;
        TotalDurationSeconds = CurrentPlayingSong.DurationSeconds > 0
            ? CurrentPlayingSong.DurationSeconds
            : _player.GetDuration().TotalSeconds;
        CurrentLyricLine = null;
        CurrentLyricText = "歌词加载中...";
        CurrentLyricTrans = "";
        StartLyricsLoad(CurrentPlayingSong, _preparedNextIsLocal);
        ResetTailTelemetry();
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
        _preparedNextIsLocal = false;
    }

    private CancellationTokenSource EnsureTransitionCancellation()
    {
        if (_transitionWorkCancellation == null || _transitionWorkCancellation.IsCancellationRequested)
        {
            CancelAndDisposeTransitionCancellation();
            _transitionWorkCancellation = new CancellationTokenSource();
        }

        return _transitionWorkCancellation;
    }

    private static string BuildSongTransitionKey(SongItem song)
    {
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath)) return $"local:{song.LocalFilePath}";

        return $"remote:{song.Hash}:{song.DurationSeconds:0.###}";
    }

    private readonly record struct PlaybackTelemetryPoint(
        double PositionSeconds,
        double DurationSeconds,
        double Rms,
        double Brightness);
}