using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed record PersonalFmPlaybackRequest(SongItem Song, IReadOnlyList<SongItem> ContextList);

public sealed record PersonalFmAdvanceResult(
    bool Success,
    PersonalFmPlaybackRequest? PlaybackRequest = null,
    string? UnavailableMessage = null);

public sealed class PersonalFmService(
    RecommendClient discoveryClient,
    ILogger<PersonalFmService> logger)
    : IDisposable
{
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly PersonalFmSessionState _session = new();

    public event Action? StateChanged;

    public bool IsActive => _session is { IsActive: true, CurrentSong: not null };
    public PersonalFmMode CurrentMode => _session.Mode;
    public PersonalFmSongPoolId CurrentSongPoolId => _session.SongPoolId;
    public SongItem? UpcomingSong => _session.UpcomingSongs.AsValueEnumerable().FirstOrDefault();

    public IReadOnlyList<SongItem> GetDisplaySongs(int limit = 5)
    {
        return _session.GetDisplaySongs(limit);
    }

    public IReadOnlyList<SongItem> GetQueueSongs()
    {
        return _session.GetDisplaySongs(Math.Max(1, _session.UpcomingSongs.Count + 1));
    }

    public async Task<List<SongItem>> FetchPreviewAsync(
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = await discoveryClient.GetPersonalRecommendFMAsync(
            mode: PersonalFmPresentation.GetModeApiValue(mode),
            songPoolId: (int)songPoolId);

        cancellationToken.ThrowIfCancellationRequested();

        if (response?.Songs == null || response.Songs.Count == 0)
            return [];

        var resolvedMode = string.IsNullOrWhiteSpace(response.Mode)
            ? mode
            : PersonalFmPresentation.ParseMode(response.Mode);

        return response.Songs
            .AsValueEnumerable().Select(MapPersonalFmSong)
            .GroupBy(BuildSongIdentityKey)
            .Select(group => group.AsValueEnumerable().First())
            .Take(5)
            .ToList();
    }

    public async Task<PersonalFmPlaybackRequest?> StartAsync(
        IReadOnlyList<SongItem> songs,
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        SongItem? startSong = null)
    {
        if (songs.Count == 0)
            return null;

        await _lock.WaitAsync();
        try
        {
            var preparedSongs = songs
                .AsValueEnumerable().Select(PreparePersonalFmSong)
                .ToList();

            var targetSong = startSong == null
                ? preparedSongs[0]
                : preparedSongs.AsValueEnumerable().FirstOrDefault(song => BuildSongIdentityKey(song) == BuildSongIdentityKey(startSong)) ??
                  preparedSongs[0];

            _session.Reset(mode, songPoolId, preparedSongs, targetSong);
            RaiseStateChanged();
            return BuildPlaybackRequest(targetSong);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PersonalFmAdvanceResult> DislikeCurrentAsync()
    {
        if (!IsActive || _session.CurrentSong == null)
            return new PersonalFmAdvanceResult(false);

        await _lock.WaitAsync();
        try
        {
            var current = _session.CurrentSong;
            var context = new PersonalFmActionContext
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

    public async Task<PersonalFmAdvanceResult> PlayNextAsync(int playtimeSeconds, bool isOverplay, bool trackFinished)
    {
        if (!IsActive || _session.CurrentSong == null)
            return new PersonalFmAdvanceResult(false);

        await _lock.WaitAsync();
        try
        {
            var current = _session.CurrentSong;
            var context = new PersonalFmActionContext
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

    public async Task<PersonalFmPlaybackRequest?> PlayPreviousAsync()
    {
        if (!IsActive || _session.HistorySongs.Count == 0 || _session.CurrentSong == null)
            return null;

        await _lock.WaitAsync();
        try
        {
            var oldCurrent = _session.CurrentSong;
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
        if (!_session.IsActive && _session.CurrentSong == null &&
            _session.UpcomingSongs.Count == 0 && _session.HistorySongs.Count == 0)
            return false;

        _session.Clear();
        RaiseStateChanged();
        return true;
    }

    public bool AddToNext(SongItem? song)
    {
        if (!IsActive || _session.CurrentSong == null || song == null)
            return false;

        var incomingSong = PreparePersonalFmSong(song);
        var incomingSongKey = BuildSongIdentityKey(incomingSong);
        if (BuildSongIdentityKey(_session.CurrentSong) == incomingSongKey)
            return false;

        _session.UpcomingSongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _session.HistorySongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _session.UpcomingSongs.Insert(0, incomingSong);
        RaiseStateChanged();
        return true;
    }

    public bool AdvanceForAutoTransition(SongItem? nextSong)
    {
        if (!IsActive || _session.CurrentSong == null || nextSong == null)
            return false;

        var nextSongKey = BuildSongIdentityKey(nextSong);
        var upcomingIndex = _session.UpcomingSongs.FindIndex(song => BuildSongIdentityKey(song) == nextSongKey);
        if (upcomingIndex < 0)
            return false;

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

    private PersonalFmAdvanceResult AdvanceCore(bool trackFinished)
    {
        if (_session.CurrentSong == null)
            return new PersonalFmAdvanceResult(false);

        if (_session.UpcomingSongs.Count == 0)
        {
            var message = trackFinished ? "私人 FM 暂时没有新的推荐歌曲" : "没有可切换的私人 FM 歌曲";
            return new PersonalFmAdvanceResult(false, UnavailableMessage: message);
        }

        var oldCurrent = _session.CurrentSong;
        oldCurrent.IsPlaying = false;
        _session.HistorySongs.Add(oldCurrent);

        var nextSong = _session.UpcomingSongs[0];
        _session.UpcomingSongs.RemoveAt(0);
        _session.CurrentSong = nextSong;

        RaiseStateChanged();
        return new PersonalFmAdvanceResult(true, BuildPlaybackRequest(nextSong));
    }

    private async Task ReportActionAsync(PersonalFmActionContext context)
    {
        if (context.Track == null)
            return;

        try
        {
            var response = await discoveryClient.GetPersonalRecommendFMAsync(
                hash: context.Track.Hash,
                songid: context.Track.AudioId > 0 ? context.Track.AudioId.ToString() : null,
                playtime: context.PlaytimeSeconds,
                action: context.Action,
                mode: PersonalFmPresentation.GetModeApiValue(_session.Mode),
                songPoolId: (int)_session.SongPoolId,
                isOverplay: context.IsOverplay ?? false,
                remainSongCount: Math.Max(0, context.RemainSongCount));

            if (response?.Songs == null || response.Songs.Count == 0)
                return;

            foreach (var song in response.Songs.AsValueEnumerable().Select(MapPersonalFmSong))
            {
                var songKey = BuildSongIdentityKey(song);
                if (_session.CurrentSong != null && BuildSongIdentityKey(_session.CurrentSong) == songKey)
                    continue;

                if (_session.HistorySongs.AsValueEnumerable().Any(item => BuildSongIdentityKey(item) == songKey))
                    continue;

                if (_session.UpcomingSongs.AsValueEnumerable().Any(item => BuildSongIdentityKey(item) == songKey))
                    continue;

                _session.UpcomingSongs.Add(song);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "私人 FM 行为上报失败");
        }
    }

    private PersonalFmPlaybackRequest BuildPlaybackRequest(SongItem song)
    {
        return new PersonalFmPlaybackRequest(song, BuildContextList());
    }

    private List<SongItem> BuildContextList()
    {
        var songs = new List<SongItem>();
        if (_session.CurrentSong != null)
            songs.Add(_session.CurrentSong);

        songs.AddRange(_session.UpcomingSongs);
        return songs;
    }

    private static SongItem PreparePersonalFmSong(SongItem song)
    {
        song.PlaybackSource = SongPlaybackSource.PersonalFm;
        return song;
    }

    private static SongItem MapPersonalFmSong(PersonalFmSong song)
    {
        return new SongItem
        {
            Name = song.Name,
            Singer = song.SingerName,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            AudioId = song.AudioId,
            Singers = song.Singers,
            Cover = ResolvePersonalFmCover(song),
            DurationSeconds = song.DurationSeconds,
            PlaybackSource = SongPlaybackSource.PersonalFm
        };
    }

    private static string ResolvePersonalFmCover(PersonalFmSong song)
    {
        var cover = song.TransParam?.UnionCover;
        return string.IsNullOrWhiteSpace(cover) ? DefaultSongCover : cover.Replace("{size}", "400");
    }

    private static string BuildSongIdentityKey(SongItem song)
    {
        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"hash:{song.Hash}";

        if (song.AudioId > 0)
            return $"audio:{song.AudioId}";

        return $"name:{song.Name}:{song.Singer}:{song.DurationSeconds:0.###}";
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke();
    }
}
