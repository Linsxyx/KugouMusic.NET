using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly SemaphoreSlim _personalFmLock = new(1, 1);
    private readonly PersonalFmSessionState _personalFmSession = new();

    public event Action? PersonalFmStateChanged;

    public bool IsPersonalFmSessionActive => _personalFmSession is { IsActive: true, CurrentSong: not null };
    public PersonalFmMode CurrentPersonalFmMode => _personalFmSession.Mode;
    public PersonalFmSongPoolId CurrentPersonalFmSongPoolId => _personalFmSession.SongPoolId;

    public IReadOnlyList<SongItem> GetPersonalFmDisplaySongs(int limit = 5)
    {
        return _personalFmSession.GetDisplaySongs(limit);
    }

    public IReadOnlyList<SongItem> GetPersonalFmQueueSongs()
    {
        return _personalFmSession.GetDisplaySongs(Math.Max(1, _personalFmSession.UpcomingSongs.Count + 1));
    }

    public async Task<List<SongItem>> FetchPersonalFmPreviewAsync(
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = await _discoveryClient.GetPersonalRecommendFMAsync(
            mode: PersonalFmPresentation.GetModeApiValue(mode),
            songPoolId: (int)songPoolId);

        cancellationToken.ThrowIfCancellationRequested();

        if (response?.Songs == null || response.Songs.Count == 0)
            return [];

        var resolvedMode = string.IsNullOrWhiteSpace(response.Mode)
            ? mode
            : PersonalFmPresentation.ParseMode(response.Mode);

        return response.Songs
            .Select(song => MapPersonalFmSong(song, resolvedMode, songPoolId))
            .GroupBy(BuildSongIdentityKey)
            .Select(group => group.First())
            .Take(5)
            .ToList();
    }

    public async Task StartPersonalFmAsync(
        IReadOnlyList<SongItem> songs,
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        SongItem? startSong = null)
    {
        if (songs.Count == 0)
            return;

        await _personalFmLock.WaitAsync();
        try
        {
            var preparedSongs = songs
                .Select(song => PreparePersonalFmSong(song, mode, songPoolId))
                .ToList();

            var targetSong = startSong == null
                ? preparedSongs[0]
                : preparedSongs.FirstOrDefault(song => BuildSongIdentityKey(song) == BuildSongIdentityKey(startSong)) ??
                  preparedSongs[0];

            _personalFmSession.Reset(mode, songPoolId, preparedSongs, targetSong);
            OnPropertyChanged(nameof(IsPersonalFmSessionActive));
            OnPropertyChanged(nameof(CurrentPersonalFmMode));
            OnPropertyChanged(nameof(CurrentPersonalFmSongPoolId));
            RaisePersonalFmStateChanged();

            await PlaySongAsync(targetSong, BuildPersonalFmContextList(), true);
        }
        finally
        {
            _personalFmLock.Release();
        }
    }

    public async Task RefreshPersonalFmAsync(IReadOnlyList<SongItem> songs, PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId)
    {
        await StartPersonalFmAsync(songs, mode, songPoolId);
    }

    public async Task<bool> DislikeCurrentPersonalFmAsync()
    {
        if (!IsPersonalFmSessionActive || _personalFmSession.CurrentSong == null)
            return false;

        await _personalFmLock.WaitAsync();
        try
        {
            var current = _personalFmSession.CurrentSong;
            var context = new PersonalFmActionContext
            {
                Track = current,
                Action = "garbage",
                RemainSongCount = _personalFmSession.UpcomingSongs.Count
            };

            await ReportPersonalFmActionAsync(context);
            return await AdvancePersonalFmCoreAsync(trackFinished: false, preservePlaybackTiming: false);
        }
        finally
        {
            _personalFmLock.Release();
        }
    }

    public async Task PlayNextPersonalFmAsync(bool trackFinished = false)
    {
        if (!IsPersonalFmSessionActive || _personalFmSession.CurrentSong == null)
            return;

        await _personalFmLock.WaitAsync();
        try
        {
            var current = _personalFmSession.CurrentSong;
            var context = new PersonalFmActionContext
            {
                Track = current,
                Action = "play",
                RemainSongCount = _personalFmSession.UpcomingSongs.Count,
                PlaytimeSeconds = Math.Max(0, (int)Math.Floor(CurrentPositionSeconds)),
                IsOverplay = trackFinished || HasCurrentSongReachedTail()
            };

            await ReportPersonalFmActionAsync(context);
            await AdvancePersonalFmCoreAsync(trackFinished, true);
        }
        finally
        {
            _personalFmLock.Release();
        }
    }

    public async Task PlayPreviousPersonalFmAsync()
    {
        if (!IsPersonalFmSessionActive || _personalFmSession.HistorySongs.Count == 0 || _personalFmSession.CurrentSong == null)
            return;

        await _personalFmLock.WaitAsync();
        try
        {
            var oldCurrent = _personalFmSession.CurrentSong;
            var previous = _personalFmSession.HistorySongs[^1];
            _personalFmSession.HistorySongs.RemoveAt(_personalFmSession.HistorySongs.Count - 1);
            _personalFmSession.UpcomingSongs.Insert(0, oldCurrent);
            _personalFmSession.CurrentSong = previous;
            RaisePersonalFmStateChanged();

            await PlaySongAsync(previous, BuildPersonalFmContextList(), true);
        }
        finally
        {
            _personalFmLock.Release();
        }
    }

    public void ClearPersonalFmSession()
    {
        if (!_personalFmSession.IsActive && _personalFmSession.CurrentSong == null &&
            _personalFmSession.UpcomingSongs.Count == 0 && _personalFmSession.HistorySongs.Count == 0)
            return;

        _personalFmSession.Clear();
        OnPropertyChanged(nameof(IsPersonalFmSessionActive));
        RaisePersonalFmStateChanged();
    }

    public bool AddSongToPersonalFmNext(SongItem? song)
    {
        if (!IsPersonalFmSessionActive || _personalFmSession.CurrentSong == null || song == null)
            return false;

        var incomingSong = PreparePersonalFmSong(song, _personalFmSession.Mode, _personalFmSession.SongPoolId);
        var incomingSongKey = BuildSongIdentityKey(incomingSong);
        if (BuildSongIdentityKey(_personalFmSession.CurrentSong) == incomingSongKey)
            return false;

        _personalFmSession.UpcomingSongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _personalFmSession.HistorySongs.RemoveAll(item => BuildSongIdentityKey(item) == incomingSongKey);
        _personalFmSession.UpcomingSongs.Insert(0, incomingSong);
        RaisePersonalFmStateChanged();
        ResetTransitionPipeline(true);

        if (_loadCancellation is { IsCancellationRequested: false } cts)
            _ = EnsurePreparedNextTrackAsync(_playRequestVersion, cts.Token);

        return true;
    }

    private async Task<bool> AdvancePersonalFmCoreAsync(bool trackFinished, bool preservePlaybackTiming)
    {
        if (_personalFmSession.CurrentSong == null)
            return false;

        if (_personalFmSession.UpcomingSongs.Count == 0)
        {
            ShowPersonalFmUnavailableToast(trackFinished ? "私人 FM 暂时没有新的推荐歌曲" : "没有可切换的私人 FM 歌曲");
            return false;
        }

        var oldCurrent = _personalFmSession.CurrentSong;
        oldCurrent.IsPlaying = false;
        _personalFmSession.HistorySongs.Add(oldCurrent);

        var nextSong = _personalFmSession.UpcomingSongs[0];
        _personalFmSession.UpcomingSongs.RemoveAt(0);
        _personalFmSession.CurrentSong = nextSong;

        RaisePersonalFmStateChanged();
        await PlaySongAsync(nextSong, BuildPersonalFmContextList(), true);
        return true;
    }

    private async Task ReportPersonalFmActionAsync(PersonalFmActionContext context)
    {
        if (context.Track == null)
            return;

        try
        {
            var response = await _discoveryClient.GetPersonalRecommendFMAsync(
                hash: context.Track.Hash,
                songid: context.Track.AudioId > 0 ? context.Track.AudioId.ToString() : null,
                playtime: context.PlaytimeSeconds,
                action: context.Action,
                mode: PersonalFmPresentation.GetModeApiValue(_personalFmSession.Mode),
                songPoolId: (int)_personalFmSession.SongPoolId,
                isOverplay: context.IsOverplay ?? false,
                remainSongCount: Math.Max(0, context.RemainSongCount));

            if (response?.Songs == null || response.Songs.Count == 0)
                return;

            foreach (var song in response.Songs.Select(item => MapPersonalFmSong(item, _personalFmSession.Mode,
                         _personalFmSession.SongPoolId)))
            {
                var songKey = BuildSongIdentityKey(song);
                if (_personalFmSession.CurrentSong != null &&
                    BuildSongIdentityKey(_personalFmSession.CurrentSong) == songKey)
                    continue;

                if (_personalFmSession.HistorySongs.Any(item => BuildSongIdentityKey(item) == songKey))
                    continue;

                if (_personalFmSession.UpcomingSongs.Any(item => BuildSongIdentityKey(item) == songKey))
                    continue;

                _personalFmSession.UpcomingSongs.Add(song);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "私人 FM 行为上报失败");
        }
    }

    private SongItem? GetUpcomingPersonalFmSong()
    {
        return _personalFmSession.UpcomingSongs.FirstOrDefault();
    }

    private List<SongItem> BuildPersonalFmContextList()
    {
        var songs = new List<SongItem>();
        if (_personalFmSession.CurrentSong != null)
            songs.Add(_personalFmSession.CurrentSong);

        songs.AddRange(_personalFmSession.UpcomingSongs);
        return songs;
    }

    private bool HasCurrentSongReachedTail()
    {
        if (TotalDurationSeconds <= 0)
            return false;

        return CurrentPositionSeconds >= Math.Max(0, TotalDurationSeconds - 2);
    }

    private static SongItem PreparePersonalFmSong(SongItem song, PersonalFmMode mode, PersonalFmSongPoolId songPoolId)
    {
        song.PlaybackSource = SongPlaybackSource.PersonalFm;
        return song;
    }

    private static SongItem MapPersonalFmSong(PersonalFmSong song, PersonalFmMode mode, PersonalFmSongPoolId songPoolId)
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

    private void AdvancePersonalFmSessionForAutoTransition(SongItem? nextSong)
    {
        if (!IsPersonalFmSessionActive || _personalFmSession.CurrentSong == null || nextSong == null)
            return;

        var nextSongKey = BuildSongIdentityKey(nextSong);
        var upcomingIndex = _personalFmSession.UpcomingSongs.FindIndex(song => BuildSongIdentityKey(song) == nextSongKey);
        if (upcomingIndex < 0)
            return;

        var oldCurrent = _personalFmSession.CurrentSong;
        oldCurrent.IsPlaying = false;
        _personalFmSession.HistorySongs.Add(oldCurrent);
        _personalFmSession.CurrentSong = _personalFmSession.UpcomingSongs[upcomingIndex];
        _personalFmSession.UpcomingSongs.RemoveAt(upcomingIndex);
        RaisePersonalFmStateChanged();
    }

    private void RaisePersonalFmStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsPersonalFmSessionActive));
            OnPropertyChanged(nameof(CurrentPersonalFmMode));
            OnPropertyChanged(nameof(CurrentPersonalFmSongPoolId));
            PersonalFmStateChanged?.Invoke();
        });
    }

    private void ShowPersonalFmUnavailableToast(string content)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("私人 FM")
                .WithContent(content)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        });
    }
}
