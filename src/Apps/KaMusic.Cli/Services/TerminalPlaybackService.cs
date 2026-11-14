using KuGou.Net.Abstractions;
using KuGou.Net.Clients;
using KaMusic.Cli.Models;
using SimpleAudio;

namespace KaMusic.Cli.Services;

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
    private readonly TerminalVipEntitlementService? _vipEntitlement;
    private readonly TerminalPersonalFmService? _personalFm;
    private readonly Func<bool> _isAuthenticated;
    private readonly DualTrackAudioPlayer _player = new();
    private readonly ITransitionAnalysisService _transitionAnalysisService = new ManagedBassTransitionAnalysisService();
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private readonly Random _random = new();
    private List<TerminalSongItem> _queue = [];

    // 歌单原始顺序（未被随机播放打乱），用于关闭随机播放时恢复顺序。
    private List<TerminalSongItem> _queueDefaultOrder = [];
    private bool _isPersonalFmQueue;
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
        TerminalAppSettings settings,
        TerminalVipEntitlementService? vipEntitlement = null,
        Func<bool>? isAuthenticated = null,
        TerminalPersonalFmService? personalFm = null)
    {
        _songClient = songClient;
        _lyricsService = lyricsService;
        _settingsStore = settingsStore;
        _settings = settings;
        _vipEntitlement = vipEntitlement;
        _isAuthenticated = isAuthenticated ?? (() => true);
        _personalFm = personalFm;
        _player.PlaybackEnded += HandlePlaybackEnded;
        ApplySettings();
    }

    public event Action<TerminalSongItem>? LoginRequired;

    public string StatusMessage { get; private set; } = "就绪";

    public Task PlayAsync(TerminalSongItem song, IReadOnlyList<TerminalSongItem>? context = null) =>
        PlayCoreAsync(song, context, isPersonalFm: false);

    public async Task<bool> StartPersonalFmAsync(
        IReadOnlyList<TerminalSongItem> songs,
        TerminalPersonalFmMode mode,
        TerminalPersonalFmSongPoolId songPoolId,
        TerminalSongItem? startSong = null)
    {
        if (_personalFm == null || songs.Count == 0)
        {
            return false;
        }

        var request = await _personalFm.StartAsync(songs, mode, songPoolId, startSong);
        if (request == null)
        {
            return false;
        }

        await PlayCoreAsync(request.Song, request.ContextList, isPersonalFm: true);
        return true;
    }

    public async Task<bool> DislikeCurrentPersonalFmAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0 || _personalFm is not { IsActive: true })
        {
            return false;
        }

        var result = await _personalFm.DislikeCurrentAsync();
        if (!string.IsNullOrWhiteSpace(result.UnavailableMessage))
        {
            StatusMessage = result.UnavailableMessage;
        }

        if (!result.Success || result.PlaybackRequest == null)
        {
            return false;
        }

        await PlayCoreAsync(result.PlaybackRequest.Song, result.PlaybackRequest.ContextList, isPersonalFm: true);
        return true;
    }

    private async Task PlayCoreAsync(
        TerminalSongItem song,
        IReadOnlyList<TerminalSongItem>? context,
        bool isPersonalFm)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (song.RequiresOnlineAuth && !_isAuthenticated())
        {
            StatusMessage = $"请先登录：登录后才能播放在线音乐《{song.Name}》（按 F5 扫码登录 / F6 短信登录）";
            LoginRequired?.Invoke(song);
            return;
        }

        if (!isPersonalFm)
        {
            _personalFm?.Clear();
        }

        await _playLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return;
            }

            // 队列页选择只改变当前项；不能把随机后的队列当成新的歌单原序。
            if (ReferenceEquals(context, _queue))
            {
                context = null;
            }

            if (context is { Count: > 0 })
            {
                _queue = context.Select(CloneSong).ToList();
                // 与 PC PlaybackQueueManager 一样，两种顺序共享同一批队列条目。
                _queueDefaultOrder = _queue.ToList();
                _isPersonalFmQueue = isPersonalFm;

                // 先用对象引用精确定位（歌单里可能存在 Hash 相同的重复曲目），再退回按身份匹配。
                var index = ResolveSongIndex(context, song);
                _currentIndex = index >= 0 ? index : 0;
                if (!isPersonalFm && _settings.Shuffle)
                {
                    ShuffleQueue(_queue[_currentIndex]);
                }
            }
            else if (_queue.Count == 0)
            {
                _queue = [CloneSong(song)];
                _queueDefaultOrder = _queue.ToList();
                _currentIndex = 0;
                _isPersonalFmQueue = isPersonalFm;
            }
            else
            {
                // 没有上下文时不要沿用旧队列：否则会把上一次（可能是每日推荐/私人 FM）的队列
                // 当成当前歌单继续播放，表现为“播放列表混入推荐音乐”。
                var existingIndex = ResolveSongIndex(_queue, song);
                if (existingIndex < 0)
                {
                    _queue = [CloneSong(song)];
                    _queueDefaultOrder = _queue.ToList();
                    _currentIndex = 0;
                    _isPersonalFmQueue = isPersonalFm;
                }
                else
                {
                    _currentIndex = existingIndex;
                    _isPersonalFmQueue = isPersonalFm;
                }
            }

            await PlayCurrentCoreAsync();
        }
        finally
        {
            _playLock.Release();
        }
    }

    public Task PlayNextAsync() => PlayNextInternalAsync(trackFinished: false);

    private async Task PlayNextInternalAsync(bool trackFinished)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (_personalFm is { IsActive: true })
        {
            var playtimeSeconds = Math.Max(0, (int)Math.Floor(_player.GetPosition().TotalSeconds));
            var isOverplay = trackFinished || HasCurrentSongReachedTail();
            var result = await _personalFm.PlayNextAsync(playtimeSeconds, isOverplay, trackFinished);
            if (!string.IsNullOrWhiteSpace(result.UnavailableMessage))
            {
                StatusMessage = result.UnavailableMessage;
            }

            if (result.Success && result.PlaybackRequest != null)
            {
                await PlayCoreAsync(result.PlaybackRequest.Song, result.PlaybackRequest.ContextList, isPersonalFm: true);
            }

            return;
        }

        await _playLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0 || _queue.Count == 0)
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
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (_personalFm is { IsActive: true })
        {
            var request = await _personalFm.PlayPreviousAsync();
            if (request == null)
            {
                StatusMessage = "已是私人 FM 第一首";
                return;
            }

            await PlayCoreAsync(request.Song, request.ContextList, isPersonalFm: true);
            return;
        }

        await _playLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0 || _queue.Count == 0)
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

    public void SeekToRatio(double ratio)
    {
        if (_player.IsStopped)
        {
            return;
        }

        var duration = _player.GetDuration();
        if (duration <= TimeSpan.Zero && CurrentSong?.DurationSeconds > 0)
        {
            duration = TimeSpan.FromSeconds(CurrentSong.DurationSeconds);
        }

        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        ResetTransitionPipeline(cancelPreparedTrack: true);
        var target = TimeSpan.FromSeconds(duration.TotalSeconds * Math.Clamp(ratio, 0.0, 1.0));
        _player.SetPosition(target);
        StatusMessage = $"跳转到 {target:mm\\:ss}";
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

    public void RemoveFromQueueAt(int index)
    {
        if (index < 0 || index >= _queue.Count || index == _currentIndex)
        {
            return;
        }

        ResetTransitionPipeline(cancelPreparedTrack: true);
        var removed = _queue[index];
        _queue.RemoveAt(index);
        _queueDefaultOrder.Remove(removed);
        if (index < _currentIndex)
        {
            _currentIndex--;
        }

        StatusMessage = $"已从队列移除：{removed.Name}";
    }

    public void EnqueueNext(TerminalSongItem song)
    {
        if (_personalFm is { IsActive: true } && _personalFm.AddToNext(song))
        {
            _queue = _personalFm.GetQueueSongs().Select(CloneSong).ToList();
            _queueDefaultOrder = _queue.ToList();
            _isPersonalFmQueue = true;
            _currentIndex = _queue.Count > 0 ? 0 : -1;
            if (CurrentSong != null)
            {
                CurrentSong.IsPlaying = true;
            }

            ResetTransitionPipeline(cancelPreparedTrack: true);
            StatusMessage = $"已加入私人 FM 下一首：{song.Name}";
            return;
        }

        var current = CurrentSong;
        // PC 的 AddToNext 会移动已存在的歌曲，而不是再追加一份。
        // 队列内操作优先按引用定位，避免移动同 Hash 的另一条记录。
        var existingIndex = ResolveSongIndex(_queue, song);
        var entry = existingIndex >= 0 ? _queue[existingIndex] : CloneSong(song);
        if (existingIndex >= 0)
        {
            _queue.RemoveAt(existingIndex);
            _queueDefaultOrder.Remove(entry);
        }

        var playIndex = current == null ? -1 : _queue.IndexOf(current);
        var originalIndex = current == null ? -1 : _queueDefaultOrder.IndexOf(current);
        _queue.Insert(playIndex >= 0 ? playIndex + 1 : _queue.Count, entry);
        _queueDefaultOrder.Insert(originalIndex >= 0 ? originalIndex + 1 : _queueDefaultOrder.Count, entry);
        _currentIndex = current == null ? -1 : _queue.IndexOf(current);

        ResetTransitionPipeline(cancelPreparedTrack: true);
        StatusMessage = $"已加入下一首：{song.Name}";
    }

    public void ToggleShuffle()
    {
        _settings.Shuffle = !_settings.Shuffle;
        ResetTransitionPipeline(cancelPreparedTrack: true);
        var current = CurrentSong;
        if (_settings.Shuffle && !_isPersonalFmQueue && current != null)
        {
            ShuffleQueue(current);
        }
        else if (!_settings.Shuffle && !_isPersonalFmQueue && _queueDefaultOrder.Count == _queue.Count)
        {
            // 关闭随机播放：回到歌单原始顺序，并重新定位当前歌曲。
            var index = ResolveSongIndex(_queueDefaultOrder, current);
            if (index >= 0)
            {
                _queue = _queueDefaultOrder.ToList();
                _currentIndex = index;
            }
        }
        else if (current != null)
        {
            _currentIndex = _queue.IndexOf(current);
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
        _settings.MusicQuality = AudioQuality.GetNext(_settings.MusicQuality);
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
        var position = _player.GetPosition();
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
            Position = position,
            Duration = duration,
            LyricMode = _settings.LyricMode,
            LyricWindow = _lyricsService.GetWindow(position, _settings.LyricMode),
            LyricLines = _lyricsService.GetRichWindow(position, _settings.LyricMode, 5),
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

    // 根据歌曲来源（本地文件、远程流或在线酷狗曲目）校验登录状态并加载播放。
    private async Task PlayCurrentCoreAsync()
    {
        var song = CurrentSong;
        if (song == null)
        {
            return;
        }

        if (song.RequiresOnlineAuth && !_isAuthenticated())
        {
            StatusMessage = $"请先登录：登录后才能播放在线音乐《{song.Name}》（按 F5 扫码登录 / F6 短信登录）";
            LoginRequired?.Invoke(song);
            return;
        }

        foreach (var item in _queue)
        {
            item.IsPlaying = false;
        }

        song.IsPlaying = true;
        var requestVersion = Interlocked.Increment(ref _playVersion);
        ResetTransitionPipeline(cancelPreparedTrack: true);

        if (song.IsLocalFile && !string.IsNullOrWhiteSpace(song.LocalFilePath))
        {
            if (Volatile.Read(ref _disposeState) != 0 || requestVersion != _playVersion || !_player.Load(song.LocalFilePath))
            {
                StatusMessage = $"本地音频加载失败：{song.Name}";
                return;
            }

            _activeSource = song.LocalFilePath;
            ApplySettings();
            _player.Play();
            StatusMessage = $"播放中（本地）：{song.Name}";
            _ = _lyricsService.LoadSongLyricsAsync(song);
            return;
        }

        if (song.IsRemoteStream && !string.IsNullOrWhiteSpace(song.RemoteUrl))
        {
            if (Volatile.Read(ref _disposeState) != 0 || requestVersion != _playVersion || !_player.Load(song.RemoteUrl))
            {
                StatusMessage = $"远程音频加载失败：{song.Name}";
                return;
            }

            _activeSource = song.RemoteUrl;
            ApplySettings();
            _player.Play();
            StatusMessage = $"播放中：{song.Name}";
            _ = _lyricsService.LoadSongLyricsAsync(song);
            return;
        }

        StatusMessage = $"正在获取播放地址：{song.Name}";

        var playData = await _songClient.GetPlayInfoAsync(song.Hash, _settings.MusicQuality);
        var url = playData?.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if ((playData?.Status != 1 || string.IsNullOrWhiteSpace(url)) && _vipEntitlement != null)
        {
            StatusMessage = $"正在恢复账号与 VIP 权益：{song.Name}";
            if (await _vipEntitlement.TryRecoverPlaybackAccountAsync())
            {
                playData = await _songClient.GetPlayInfoAsync(song.Hash, _settings.MusicQuality);
                url = playData?.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }
        }

        if (playData?.Status != 1 || string.IsNullOrWhiteSpace(url))
        {
            StatusMessage = $"无法播放：{song.Name}";
            return;
        }

        if (Volatile.Read(ref _disposeState) != 0 || requestVersion != _playVersion || !_player.Load(url))
        {
            StatusMessage = $"音频加载失败：{song.Name}";
            return;
        }

        _activeSource = url;
        ApplySettings();
        _player.Play();
        StatusMessage = $"播放中：{song.Name}";
        _ = _lyricsService.LoadSongLyricsAsync(song);
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
            string? source;
            var isLocal = false;
            if (nextSong.IsLocalFile && !string.IsNullOrWhiteSpace(nextSong.LocalFilePath))
            {
                source = nextSong.LocalFilePath;
                isLocal = true;
            }
            else if (nextSong.IsRemoteStream && !string.IsNullOrWhiteSpace(nextSong.RemoteUrl))
            {
                source = nextSong.RemoteUrl;
            }
            else
            {
                if (nextSong.RequiresOnlineAuth && !_isAuthenticated())
                {
                    return;
                }

                var playData = await _songClient.GetPlayInfoAsync(nextSong.Hash, _settings.MusicQuality);
                source = playData?.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }

            if (requestVersion != _playVersion || string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            if (!_player.PrepareNext(source))
            {
                return;
            }

            _preparedNextSong = nextSong;
            _preparedNextSource = source;
            _preparedNextTrack = new PreparedTrack
            {
                Id = BuildSongTransitionKey(nextSong),
                Source = source,
                IsLocal = isLocal,
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
                IsLocal = currentSong.IsLocalFile,
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

        if (_personalFm is { IsActive: true } && _personalFm.AdvanceForAutoTransition(_preparedNextSong))
        {
            _queue = _personalFm.GetQueueSongs().Select(CloneSong).ToList();
            _queueDefaultOrder = _queue.ToList();
            _isPersonalFmQueue = true;
            _currentIndex = _queue.Count > 0 ? 0 : -1;
        }
        else
        {
            var nextIndex = ResolveSongIndex(_queue, _preparedNextSong);
            if (nextIndex >= 0)
            {
                _currentIndex = nextIndex;
            }
        }

        if (CurrentSong != null)
        {
            CurrentSong.IsPlaying = true;
            _ = _lyricsService.LoadSongLyricsAsync(CurrentSong);
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
        if (_personalFm is { IsActive: true })
        {
            return _personalFm.UpcomingSong;
        }

        if (_queue.Count <= 1 || CurrentSong == null)
        {
            return null;
        }

        var nextIndex = (_currentIndex + 1) % _queue.Count;
        return nextIndex == _currentIndex ? null : _queue[nextIndex];
    }

    private bool HasCurrentSongReachedTail()
    {
        var duration = _player.GetDuration();
        if (duration <= TimeSpan.Zero && CurrentSong?.DurationSeconds > 0)
        {
            duration = TimeSpan.FromSeconds(CurrentSong.DurationSeconds);
        }

        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        return _player.GetPosition().TotalSeconds >= Math.Max(0, duration.TotalSeconds - 2);
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
        return $"{song.PlaybackIdentity}:{song.DurationSeconds:0.###}";
    }

    private void ApplySettings()
    {
        _player.SetVolume(_settings.Volume);
        _player.SetEQ(_settings.CustomEqGains);
        _player.SetSurround(_settings.EnableSurround);
    }

    private void ShuffleQueue(TerminalSongItem current)
    {
        var index = ResolveSongIndex(_queue, current);
        _queue = TerminalPlaylistPaging.ShuffleKeepingCurrent(_queue, index, _random);
        _currentIndex = 0;
    }

    private void HandlePlaybackEnded()
    {
        _ = Task.Run(() => PlayNextInternalAsync(trackFinished: true));
    }

    private static bool SameSong(TerminalSongItem a, TerminalSongItem b)
    {
        return a.IsSameTrack(b);
    }

    private static int ResolveSongIndex(IReadOnlyList<TerminalSongItem> songs, TerminalSongItem? song) =>
        TerminalPlaylistPaging.ResolveIndex(songs, song,
            static (left, right) => left.IsSameTrack(right),
            static (left, right) => ReferenceEquals(left.QueueIdentity, right.QueueIdentity));

    private static TerminalSongItem CloneSong(TerminalSongItem song)
    {
        return new TerminalSongItem
        {
            QueueIdentity = song.QueueIdentity,
            Name = song.Name,
            Singer = song.Singer,
            AlbumName = song.AlbumName,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId,
            AudioId = song.AudioId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            Source = song.Source,
            Privilege = song.Privilege,
            LocalFilePath = song.LocalFilePath,
            RemoteUrl = song.RemoteUrl,
            LocalSourceType = song.LocalSourceType,
            LocalTrackId = song.LocalTrackId,
            AuthorId = song.AuthorId,
            Singers = song.Singers,
            FileId = song.FileId,
            IsFavorite = song.IsFavorite
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
        {
            return;
        }

        Interlocked.Increment(ref _playVersion);
        _player.PlaybackEnded -= HandlePlaybackEnded;
        CancelTransitionWork();
        _player.Dispose();
    }
}
