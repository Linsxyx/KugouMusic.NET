using KuGou.Net.Clients;
using KgTest.Models;
using SimpleAudio;

namespace KgTest.Services;

internal sealed class TerminalPlaybackService : IDisposable
{
    private const double AnalysisWindowSec = 15.0;
    private const double FallbackMixDurationSec = 6.8;
    private const double FallbackMixEntrySec = 4.6;
    private const double PreloadWindowSec = 18.0;
    private readonly SongClient _songClient;
    private readonly TerminalLyricsService _lyricsService;
    private readonly TerminalSettingsStore _settingsStore;
    private readonly TerminalAppSettings _settings;
    private readonly DualTrackAudioPlayer _player = new();
    private readonly ITransitionAnalysisService _transitionAnalysisService = new ManagedBassTransitionAnalysisService();
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private readonly Random _random = new();
    private List<TerminalSongItem> _queue = [];
    private TransitionProfile? _pendingTransitionProfile;
    private TerminalSongItem? _pendingTransitionSong;
    private PreparedTrack? _preparedNextTrack;
    private TerminalSongItem? _preparedNextSong;
    private string? _preparedNextSource;
    private string? _activeSource;
    private CancellationTokenSource? _transitionCancellation;
    private int _currentIndex = -1;
    private int _disposeState;
    private int _playVersion;
    private bool _autoTransitionStarted;
    private bool _isAnalyzingTransition;
    private bool _isPreparingNextTrack;

    public TerminalPlaybackService(
        SongClient songClient,
        TerminalLyricsService lyricsService,
        TerminalSettingsStore settingsStore,
        TerminalAppSettings settings)
    {
        _songClient = songClient;
        _lyricsService = lyricsService;
        _settingsStore = settingsStore;
        _settings = settings;
        _player.PlaybackEnded += HandlePlaybackEnded;
        ApplySettings();
    }

    public string StatusMessage { get; private set; } = "就绪";

    public async Task PlayAsync(TerminalSongItem song, IReadOnlyList<TerminalSongItem>? context = null)
    {
        await _playLock.WaitAsync();
        try
        {
            if (context is { Count: > 0 })
            {
                _queue = context.Select(CloneSong).ToList();
                _currentIndex = Math.Max(0, _queue.FindIndex(x => SameSong(x, song)));
                if (_settings.Shuffle)
                {
                    ShuffleQueue(song);
                }
            }
            else if (_queue.Count == 0 || _currentIndex < 0)
            {
                _queue = [CloneSong(song)];
                _currentIndex = 0;
            }

            await PlayCurrentCoreAsync();
        }
        finally
        {
            _playLock.Release();
        }
    }

    public async Task PlayNextAsync()
    {
        await _playLock.WaitAsync();
        try
        {
            if (_queue.Count == 0)
            {
                return;
            }

            _currentIndex = (_currentIndex + 1) % _queue.Count;
            await PlayCurrentCoreAsync();
        }
        finally
        {
            _playLock.Release();
        }
    }

    public async Task PlayPreviousAsync()
    {
        await _playLock.WaitAsync();
        try
        {
            if (_queue.Count == 0)
            {
                return;
            }

            _currentIndex--;
            if (_currentIndex < 0)
            {
                _currentIndex = _queue.Count - 1;
            }

            await PlayCurrentCoreAsync();
        }
        finally
        {
            _playLock.Release();
        }
    }

    public void TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            StatusMessage = "已暂停";
            return;
        }

        _player.Play();
        StatusMessage = "播放中";
    }

    public void Stop()
    {
        _player.Stop();
        StatusMessage = "已停止";
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (_player.IsStopped)
        {
            return;
        }

        ResetTransitionPipeline(cancelPreparedTrack: true);
        var duration = _player.GetDuration();
        var current = _player.GetPosition();
        var target = current + delta;
        if (duration > TimeSpan.Zero)
        {
            target = TimeSpan.FromSeconds(Math.Clamp(target.TotalSeconds, 0, duration.TotalSeconds));
        }
        else if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        _player.SetPosition(target);
        StatusMessage = delta.TotalSeconds >= 0 ? $"快进到 {target:mm\\:ss}" : $"快退到 {target:mm\\:ss}";
    }

    public void ChangeVolume(float delta)
    {
        _settings.Volume = Math.Clamp(_settings.Volume + delta, 0f, 1f);
        _player.SetVolume(_settings.Volume);
        _settingsStore.Save(_settings);
    }

    public void ToggleShuffle()
    {
        _settings.Shuffle = !_settings.Shuffle;
        ResetTransitionPipeline(cancelPreparedTrack: true);
        var current = CurrentSong;
        if (_settings.Shuffle && current != null)
        {
            ShuffleQueue(current);
        }
        else if (current != null)
        {
            _currentIndex = Math.Max(0, _queue.FindIndex(x => SameSong(x, current)));
        }

        _settingsStore.Save(_settings);
    }

    public void ToggleSurround()
    {
        _settings.EnableSurround = !_settings.EnableSurround;
        _player.SetSurround(_settings.EnableSurround);
        _settingsStore.Save(_settings);
    }

    public void ToggleSeamlessTransition()
    {
        _settings.EnableSeamlessTransition = !_settings.EnableSeamlessTransition;
        if (!_settings.EnableSeamlessTransition)
        {
            ResetTransitionPipeline(cancelPreparedTrack: true);
        }

        StatusMessage = _settings.EnableSeamlessTransition ? "智能过渡已开启" : "智能过渡已关闭";
        _settingsStore.Save(_settings);
    }

    public void CycleQuality()
    {
        _settings.MusicQuality = _settings.MusicQuality switch
        {
            "128" => "320",
            "320" => "flac",
            "flac" => "high",
            _ => "128"
        };
        ResetTransitionPipeline(cancelPreparedTrack: true);
        StatusMessage = $"音质已切换到 {_settings.MusicQuality}";
        _settingsStore.Save(_settings);
    }

    public void CycleLyricMode()
    {
        _settings.LyricMode = _settings.LyricMode switch
        {
            TerminalLyricMode.Original => TerminalLyricMode.Translation,
            TerminalLyricMode.Translation => TerminalLyricMode.Romanization,
            TerminalLyricMode.Romanization => TerminalLyricMode.Combined,
            _ => TerminalLyricMode.Original
        };
        _settingsStore.Save(_settings);
    }

    public void SetEqBand(int index, float delta)
    {
        if (index < 0 || index >= _settings.CustomEqGains.Length)
        {
            return;
        }

        _settings.CustomEqGains[index] = Math.Clamp(_settings.CustomEqGains[index] + delta, -12f, 12f);
        _player.SetEQ(_settings.CustomEqGains);
        _settingsStore.Save(_settings);
    }

    public PlaybackStateSnapshot GetSnapshot()
    {
        var duration = _player.GetDuration();
        if (duration == TimeSpan.Zero && CurrentSong?.DurationSeconds > 0)
        {
            duration = TimeSpan.FromSeconds(CurrentSong.DurationSeconds);
        }

        var analysis = _player.GetActiveAnalysisSnapshot();
        var snapshot = new PlaybackStateSnapshot
        {
            CurrentSong = CurrentSong,
            Queue = _queue,
            IsPlaying = _player.IsPlaying,
            IsPaused = _player.IsPaused,
            IsShuffle = _settings.Shuffle,
            EnableSurround = _settings.EnableSurround,
            EnableSeamlessTransition = _settings.EnableSeamlessTransition,
            IsCrossfading = _player.IsCrossfading,
            Volume = _settings.Volume,
            Position = _player.GetPosition(),
            Duration = duration,
            LyricMode = _settings.LyricMode,
            LyricWindow = _lyricsService.GetWindow(_player.GetPosition(), _settings.LyricMode),
            SpectrumBands = analysis.SpectrumBands ?? [],
            Rms = analysis.Rms
        };

        UpdateTransitionPipeline(snapshot);
        return snapshot;
    }

    public string MusicQuality => _settings.MusicQuality;

    public float[] EqGains => _settings.CustomEqGains;

    public TerminalSongItem? CurrentSong =>
        _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;

    public IReadOnlyList<TerminalSongItem> Queue => _queue;

    private async Task PlayCurrentCoreAsync()
    {
        var song = CurrentSong;
        if (song == null)
        {
            return;
        }

        foreach (var item in _queue)
        {
            item.IsPlaying = false;
        }

        song.IsPlaying = true;
        var requestVersion = Interlocked.Increment(ref _playVersion);
        ResetTransitionPipeline(cancelPreparedTrack: true);
        StatusMessage = $"正在获取播放地址：{song.Name}";

        var playData = await _songClient.GetPlayInfoAsync(song.Hash, _settings.MusicQuality);
        var url = playData?.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (playData?.Status != 1 || string.IsNullOrWhiteSpace(url))
        {
            StatusMessage = $"无法播放：{song.Name}";
            return;
        }

        if (requestVersion != _playVersion || !_player.Load(url))
        {
            StatusMessage = $"音频加载失败：{song.Name}";
            return;
        }

        _activeSource = url;
        ApplySettings();
        _player.Play();
        StatusMessage = $"播放中：{song.Name}";
        _ = _lyricsService.LoadOnlineLyricsAsync(song.Hash, song.Name);
    }

    private void UpdateTransitionPipeline(PlaybackStateSnapshot snapshot)
    {
        if (!_settings.EnableSeamlessTransition ||
            _player.IsStopped ||
            _player.IsCrossfading ||
            _queue.Count <= 1 ||
            CurrentSong == null)
        {
            return;
        }

        if (_autoTransitionStarted && !_player.IsCrossfading)
        {
            ResetTransitionPipeline(cancelPreparedTrack: false);
            return;
        }

        var remainingSec = Math.Max(0, snapshot.Duration.TotalSeconds - snapshot.Position.TotalSeconds);
        if (remainingSec <= PreloadWindowSec)
        {
            _ = EnsurePreparedNextTrackAsync(_playVersion);
        }

        if (remainingSec <= AnalysisWindowSec)
        {
            _ = EnsureTransitionAnalysisAsync(_playVersion);
        }

        TryStartAutoCrossfade(remainingSec);
    }

    private async Task EnsurePreparedNextTrackAsync(int requestVersion)
    {
        if (_autoTransitionStarted || _isPreparingNextTrack || _player.IsCrossfading)
        {
            return;
        }

        var nextSong = GetUpcomingSong();
        if (nextSong == null)
        {
            return;
        }

        if (_preparedNextSong != null && SameSong(_preparedNextSong, nextSong) && _player.HasPreparedTrack)
        {
            return;
        }

        _isPreparingNextTrack = true;
        try
        {
            var playData = await _songClient.GetPlayInfoAsync(nextSong.Hash, _settings.MusicQuality);
            var url = playData?.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (requestVersion != _playVersion || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (!_player.PrepareNext(url))
            {
                return;
            }

            _preparedNextSong = nextSong;
            _preparedNextSource = url;
            _preparedNextTrack = new PreparedTrack
            {
                Id = BuildSongTransitionKey(nextSong),
                Source = url,
                IsLocal = false,
                DurationSeconds = nextSong.DurationSeconds
            };
            StatusMessage = $"已预加载下一首：{nextSong.Name}";
        }
        catch
        {
            // Preload is opportunistic.
        }
        finally
        {
            _isPreparingNextTrack = false;
        }
    }

    private async Task EnsureTransitionAnalysisAsync(int requestVersion)
    {
        var currentSong = CurrentSong;
        var nextSong = _preparedNextSong;
        var nextTrack = _preparedNextTrack;
        if (_autoTransitionStarted ||
            _isAnalyzingTransition ||
            _player.IsCrossfading ||
            currentSong == null ||
            nextSong == null ||
            nextTrack == null ||
            string.IsNullOrWhiteSpace(_activeSource))
        {
            return;
        }

        if (_pendingTransitionProfile != null && _pendingTransitionSong != null && SameSong(_pendingTransitionSong, nextSong))
        {
            return;
        }

        _isAnalyzingTransition = true;
        try
        {
            var cts = EnsureTransitionCancellation();
            var currentTrack = new PreparedTrack
            {
                Id = BuildSongTransitionKey(currentSong),
                Source = _activeSource,
                IsLocal = false,
                DurationSeconds = currentSong.DurationSeconds
            };

            var profile = await _transitionAnalysisService.AnalyzeAsync(currentTrack, nextTrack, cts.Token);
            if (requestVersion != _playVersion || cts.IsCancellationRequested || nextSong != _preparedNextSong)
            {
                return;
            }

            _pendingTransitionSong = nextSong;
            _pendingTransitionProfile = profile;
            StatusMessage = $"智能过渡已就绪：{profile.MixDurationSec:0.0}s";
        }
        catch
        {
            _pendingTransitionSong = nextSong;
            _pendingTransitionProfile = BuildFallbackTransitionProfile(FallbackMixEntrySec);
        }
        finally
        {
            _isAnalyzingTransition = false;
        }
    }

    private void TryStartAutoCrossfade(double remainingSec)
    {
        if (_autoTransitionStarted || _preparedNextSong == null || !_player.HasPreparedTrack)
        {
            return;
        }

        if (_pendingTransitionProfile == null || _pendingTransitionSong == null || !SameSong(_pendingTransitionSong, _preparedNextSong))
        {
            if (remainingSec > FallbackMixEntrySec)
            {
                return;
            }

            _pendingTransitionSong = _preparedNextSong;
            _pendingTransitionProfile = BuildFallbackTransitionProfile(remainingSec);
        }

        if (remainingSec > _pendingTransitionProfile.MixEntrySec)
        {
            return;
        }

        if (!_player.StartCrossfade(_pendingTransitionProfile, remainingSec))
        {
            return;
        }

        var oldSong = CurrentSong;
        if (oldSong != null)
        {
            oldSong.IsPlaying = false;
        }

        var nextIndex = _queue.FindIndex(x => SameSong(x, _preparedNextSong));
        if (nextIndex >= 0)
        {
            _currentIndex = nextIndex;
        }

        if (CurrentSong != null)
        {
            CurrentSong.IsPlaying = true;
            _ = _lyricsService.LoadOnlineLyricsAsync(CurrentSong.Hash, CurrentSong.Name);
        }

        _autoTransitionStarted = true;
        _activeSource = _preparedNextSource;
        StatusMessage = $"智能过渡：{oldSong?.Name} -> {_preparedNextSong.Name}";
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
    }

    private TerminalSongItem? GetUpcomingSong()
    {
        if (_queue.Count <= 1 || CurrentSong == null)
        {
            return null;
        }

        var nextIndex = (_currentIndex + 1) % _queue.Count;
        return nextIndex == _currentIndex ? null : _queue[nextIndex];
    }

    private void ResetTransitionPipeline(bool cancelPreparedTrack)
    {
        CancelTransitionWork();
        _autoTransitionStarted = false;
        _isAnalyzingTransition = false;
        _isPreparingNextTrack = false;
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
        if (cancelPreparedTrack)
        {
            _player.AbortCrossfade();
            _player.CancelPrepared();
        }
    }

    private CancellationTokenSource EnsureTransitionCancellation()
    {
        if (_transitionCancellation == null || _transitionCancellation.IsCancellationRequested)
        {
            CancelTransitionWork();
            _transitionCancellation = new CancellationTokenSource();
        }

        return _transitionCancellation;
    }

    private void CancelTransitionWork()
    {
        if (_transitionCancellation == null)
        {
            return;
        }

        try
        {
            _transitionCancellation.Cancel();
        }
        catch
        {
            // Ignore cancellation races.
        }

        _transitionCancellation.Dispose();
        _transitionCancellation = null;
    }

    private static TransitionProfile BuildFallbackTransitionProfile(double availableOverlapSec)
    {
        var overlapSec = Math.Clamp(Math.Min(availableOverlapSec, FallbackMixEntrySec), 2.4, FallbackMixEntrySec);
        var releaseSec = Math.Max(1.4, FallbackMixDurationSec - overlapSec);
        return TransitionProfile.Default with
        {
            MixEntrySec = FallbackMixEntrySec,
            MixDurationSec = overlapSec + releaseSec,
            OverlapSec = overlapSec,
            ReleaseSec = releaseSec,
            MixBreathSec = Math.Clamp(TransitionProfile.Default.MixBreathSec, 0.95, overlapSec * 0.68),
            IncomingSettleSec = overlapSec + releaseSec * 0.55,
            OutgoingDuckStrength = 0.5f,
            IncomingGainCap = 0.94f,
            OutgoingToneDepth = 0.42f,
            IncomingToneDepth = 0.18f,
            OutgoingReverbAmount = 0.08f,
            IncomingReverbAmount = 0.04f,
            StereoWidth = 0.07f,
            Confidence = 0.2f
        };
    }

    private static string BuildSongTransitionKey(TerminalSongItem song)
    {
        return $"remote:{song.Hash}:{song.DurationSeconds:0.###}";
    }

    private void ApplySettings()
    {
        _player.SetVolume(_settings.Volume);
        _player.SetEQ(_settings.CustomEqGains);
        _player.SetSurround(_settings.EnableSurround);
    }

    private void ShuffleQueue(TerminalSongItem current)
    {
        var rest = _queue.Where(x => !SameSong(x, current)).ToList();
        for (var i = rest.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }

        _queue = [current, .. rest];
        _currentIndex = 0;
    }

    private void HandlePlaybackEnded()
    {
        _ = Task.Run(PlayNextAsync);
    }

    private static bool SameSong(TerminalSongItem a, TerminalSongItem b)
    {
        return !string.IsNullOrWhiteSpace(a.Hash) &&
               string.Equals(a.Hash, b.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private static TerminalSongItem CloneSong(TerminalSongItem song)
    {
        return new TerminalSongItem
        {
            Name = song.Name,
            Singer = song.Singer,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId,
            AudioId = song.AudioId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            Source = song.Source,
            Privilege = song.Privilege
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
        {
            return;
        }

        _player.PlaybackEnded -= HandlePlaybackEnded;
        CancelTransitionWork();
        _player.Dispose();
        _playLock.Dispose();
    }
}
