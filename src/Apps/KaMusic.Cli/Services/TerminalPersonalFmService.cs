using KuGou.Net.Abstractions.Models;
using KaMusic.Cli.Models;

namespace KaMusic.Cli.Services;

// 终端私人 FM 会话服务：维护历史、当前与待播队列，上报播放/不喜欢行为并动态补货（对齐桌面端 PersonalFmService）。

internal sealed class TerminalPersonalFmService : IDisposable
{
    private readonly TerminalKugouClients _clients;
    private readonly Func<string?, bool> _isLiked;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TerminalPersonalFmSessionState _session = new();

    public TerminalPersonalFmService(TerminalKugouClients clients, Func<string?, bool> isLiked)
    {
        _clients = clients;
        _isLiked = isLiked;
    }

    public event Action? StateChanged;

    public bool IsActive => _session is { IsActive: true, CurrentSong: not null };
    public TerminalPersonalFmMode CurrentMode => _session.Mode;
    public TerminalPersonalFmSongPoolId CurrentSongPoolId => _session.SongPoolId;
    public TerminalSongItem? CurrentSong => _session.CurrentSong;
    public TerminalSongItem? UpcomingSong => _session.UpcomingSongs.FirstOrDefault();
    public int HistoryCount => _session.HistorySongs.Count;
    public int UpcomingCount => _session.UpcomingSongs.Count;

    public IReadOnlyList<TerminalSongItem> GetDisplaySongs(int limit = 5) => _session.GetDisplaySongs(limit);

    public IReadOnlyList<TerminalSongItem> GetQueueSongs() =>
        _session.GetDisplaySongs(Math.Max(1, _session.UpcomingSongs.Count + 1));

    public async Task<List<TerminalSongItem>> FetchPreviewAsync(
        TerminalPersonalFmMode mode,
        TerminalPersonalFmSongPoolId songPoolId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = await _clients.Recommend.GetPersonalRecommendFMAsync(
            mode: TerminalPersonalFmPresentation.GetModeApiValue(mode),
            songPoolId: (int)songPoolId);

        cancellationToken.ThrowIfCancellationRequested();

        if (response?.Songs == null || response.Songs.Count == 0)
        {
            return [];
        }

        return response.Songs
            .Select(MapPersonalFmSong)
            .GroupBy(BuildSongIdentityKey)
            .Select(group => group.First())
            .Take(5)
            .ToList();
    }

    public async Task<TerminalPersonalFmPlaybackRequest?> StartAsync(
        IReadOnlyList<TerminalSongItem> songs,
        TerminalPersonalFmMode mode,
        TerminalPersonalFmSongPoolId songPoolId,
        TerminalSongItem? startSong = null)
    {
        if (songs.Count == 0)
        {
            return null;
        }

        await _lock.WaitAsync();
        try
        {
            var preparedSongs = songs.Select(PreparePersonalFmSong).ToList();
            var targetSong = startSong == null
                ? preparedSongs[0]
                : preparedSongs.FirstOrDefault(song => BuildSongIdentityKey(song) == BuildSongIdentityKey(startSong))
                  ?? preparedSongs[0];

            _session.Reset(mode, songPoolId, preparedSongs, targetSong);
            RaiseStateChanged();
            return BuildPlaybackRequest(targetSong);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TerminalPersonalFmAdvanceResult> DislikeCurrentAsync()
    {
        if (!IsActive || _session.CurrentSong == null)
        {
            return new TerminalPersonalFmAdvanceResult(false);
        }

        await _lock.WaitAsync();
        try
        {
            var current = _session.CurrentSong;
            var context = new TerminalPersonalFmActionContext
            {
                Track = current,
                Action = "garbage",
                RemainSongCount = _session.UpcomingSongs.Count
            };

            await ReportActionAsync(context);
            return AdvanceCore(trackFinished: false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TerminalPersonalFmAdvanceResult> PlayNextAsync(
        int playtimeSeconds,
        bool isOverplay,
        bool trackFinished)
    {
        if (!IsActive || _session.CurrentSong == null)
        {
            return new TerminalPersonalFmAdvanceResult(false);
        }

        await _lock.WaitAsync();
        try
        {
            var current = _session.CurrentSong;
            var context = new TerminalPersonalFmActionContext
            {
                Track = current,
                Action = "play",
                RemainSongCount = _session.UpcomingSongs.Count,
                PlaytimeSeconds = playtimeSeconds,
                IsOverplay = isOverplay
            };

            await ReportActionAsync(context);
            return AdvanceCore(trackFinished);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TerminalPersonalFmPlaybackRequest?> PlayPreviousAsync()
    {
        if (!IsActive || _session.HistorySongs.Count == 0 || _session.CurrentSong == null)
        {
            return null;
        }

        await _lock.WaitAsync();
        try
        {
            var oldCurrent = _session.CurrentSong;
            oldCurrent.IsPlaying = false;
            var previous = _session.HistorySongs[^1];
            _session.HistorySongs.RemoveAt(_session.HistorySongs.Count - 1);
            _session.UpcomingSongs.Insert(0, oldCurrent);
            _session.CurrentSong = previous;
            RaiseStateChanged();
            return BuildPlaybackRequest(previous);
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool Clear()
    {
        if (!_session.IsActive &&
            _session.CurrentSong == null &&
            _session.UpcomingSongs.Count == 0 &&
            _session.HistorySongs.Count == 0)
        {
            return false;
        }

        _session.Clear();
        RaiseStateChanged();
        return true;
    }

    public bool AddToNext(TerminalSongItem? song)
    {
        if (!IsActive || _session.CurrentSong == null || song == null)
        {
            return false;
        }

        var incomingSong = PreparePersonalFmSong(song);
        var incomingSongKey = BuildSongIdentityKey(incomingSong);
        if (BuildSongIdentityKey(_session.CurrentSong) == incomingSongKey)
        {
            return false;
        }

        _session.UpcomingSongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _session.HistorySongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _session.UpcomingSongs.Insert(0, incomingSong);
        RaiseStateChanged();
        return true;
    }

    public bool AdvanceForAutoTransition(TerminalSongItem? nextSong)
    {
        if (!IsActive || _session.CurrentSong == null || nextSong == null)
        {
            return false;
        }

        var nextSongKey = BuildSongIdentityKey(nextSong);
        var upcomingIndex = _session.UpcomingSongs.FindIndex(song => BuildSongIdentityKey(song) == nextSongKey);
        if (upcomingIndex < 0)
        {
            return false;
        }

        var oldCurrent = _session.CurrentSong;
        oldCurrent.IsPlaying = false;
        _session.HistorySongs.Add(oldCurrent);
        _session.CurrentSong = _session.UpcomingSongs[upcomingIndex];
        _session.UpcomingSongs.RemoveAt(upcomingIndex);
        RaiseStateChanged();
        return true;
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private TerminalPersonalFmAdvanceResult AdvanceCore(bool trackFinished)
    {
        if (_session.CurrentSong == null)
        {
            return new TerminalPersonalFmAdvanceResult(false);
        }

        if (_session.UpcomingSongs.Count == 0)
        {
            var message = trackFinished ? "私人 FM 暂时没有新的推荐歌曲" : "没有可切换的私人 FM 歌曲";
            return new TerminalPersonalFmAdvanceResult(false, UnavailableMessage: message);
        }

        var oldCurrent = _session.CurrentSong;
        oldCurrent.IsPlaying = false;
        _session.HistorySongs.Add(oldCurrent);

        var nextSong = _session.UpcomingSongs[0];
        _session.UpcomingSongs.RemoveAt(0);
        _session.CurrentSong = nextSong;

        RaiseStateChanged();
        return new TerminalPersonalFmAdvanceResult(true, BuildPlaybackRequest(nextSong));
    }

    private async Task ReportActionAsync(TerminalPersonalFmActionContext context)
    {
        if (context.Track == null || string.IsNullOrWhiteSpace(context.Track.Hash))
        {
            return;
        }

        try
        {
            var response = await _clients.Recommend.GetPersonalRecommendFMAsync(
                hash: context.Track.Hash,
                songid: context.Track.AudioId > 0 ? context.Track.AudioId.ToString() : null,
                playtime: context.PlaytimeSeconds,
                action: context.Action,
                mode: TerminalPersonalFmPresentation.GetModeApiValue(_session.Mode),
                songPoolId: (int)_session.SongPoolId,
                isOverplay: context.IsOverplay ?? false,
                remainSongCount: Math.Max(0, context.RemainSongCount));

            if (response?.Songs == null || response.Songs.Count == 0)
            {
                return;
            }

            foreach (var song in response.Songs.Select(MapPersonalFmSong))
            {
                var songKey = BuildSongIdentityKey(song);
                if (_session.CurrentSong != null && BuildSongIdentityKey(_session.CurrentSong) == songKey)
                {
                    continue;
                }

                if (_session.HistorySongs.Any(item => BuildSongIdentityKey(item) == songKey))
                {
                    continue;
                }

                if (_session.UpcomingSongs.Any(item => BuildSongIdentityKey(item) == songKey))
                {
                    continue;
                }

                _session.UpcomingSongs.Add(song);
            }
        }
        catch
        {
            // FM 上报失败不应中断正常播放流程。
        }
    }

    private TerminalPersonalFmPlaybackRequest BuildPlaybackRequest(TerminalSongItem song) =>
        new(song, BuildContextList());

    private List<TerminalSongItem> BuildContextList()
    {
        var songs = new List<TerminalSongItem>();
        if (_session.CurrentSong != null)
        {
            songs.Add(_session.CurrentSong);
        }

        songs.AddRange(_session.UpcomingSongs);
        return songs;
    }

    private static TerminalSongItem PreparePersonalFmSong(TerminalSongItem song)
    {
        song.Source = "私人 FM";
        return song;
    }

    private TerminalSongItem MapPersonalFmSong(PersonalFmSong song)
    {
        return new TerminalSongItem
        {
            Name = song.Name,
            Singer = song.SingerName,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            AudioId = song.AudioId,
            Singers = song.Singers,
            Cover = ResolvePersonalFmCover(song),
            DurationSeconds = song.DurationSeconds,
            Source = "私人 FM",
            IsFavorite = _isLiked(song.Hash)
        };
    }

    private static string? ResolvePersonalFmCover(PersonalFmSong song)
    {
        var cover = song.TransParam?.UnionCover;
        return string.IsNullOrWhiteSpace(cover) ? null : cover.Replace("{size}", "400");
    }

    private static string BuildSongIdentityKey(TerminalSongItem song)
    {
        if (!string.IsNullOrWhiteSpace(song.Hash))
        {
            return $"hash:{song.Hash}";
        }

        if (song.AudioId > 0)
        {
            return $"audio:{song.AudioId}";
        }

        return $"name:{song.Name}:{song.Singer}:{song.DurationSeconds:0.###}";
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();
}
