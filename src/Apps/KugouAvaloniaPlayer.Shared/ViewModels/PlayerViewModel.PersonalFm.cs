using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    public event Action? PersonalFmStateChanged;

    public bool IsPersonalFmSessionActive => _personalFmService.IsActive;
    public PersonalFmMode CurrentPersonalFmMode => _personalFmService.CurrentMode;
    public PersonalFmSongPoolId CurrentPersonalFmSongPoolId => _personalFmService.CurrentSongPoolId;

    public IReadOnlyList<SongItem> GetPersonalFmDisplaySongs(int limit = 5)
    {
        return _personalFmService.GetDisplaySongs(limit);
    }

    public IReadOnlyList<SongItem> GetPersonalFmQueueSongs()
    {
        return _personalFmService.GetQueueSongs();
    }

    public Task<List<SongItem>> FetchPersonalFmPreviewAsync(
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        CancellationToken cancellationToken = default)
    {
        return _personalFmService.FetchPreviewAsync(mode, songPoolId, cancellationToken);
    }

    public async Task StartPersonalFmAsync(
        IReadOnlyList<SongItem> songs,
        PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId,
        SongItem? startSong = null)
    {
        var request = await _personalFmService.StartAsync(songs, mode, songPoolId, startSong);
        if (request == null)
            return;

        await PlayPersonalFmRequestAsync(request);
    }

    public Task RefreshPersonalFmAsync(IReadOnlyList<SongItem> songs, PersonalFmMode mode,
        PersonalFmSongPoolId songPoolId)
    {
        return StartPersonalFmAsync(songs, mode, songPoolId);
    }

    public async Task<bool> DislikeCurrentPersonalFmAsync()
    {
        var result = await _personalFmService.DislikeCurrentAsync();
        return await HandlePersonalFmAdvanceResultAsync(result);
    }

    public async Task PlayNextPersonalFmAsync(bool trackFinished = false)
    {
        var result = await _personalFmService.PlayNextAsync(
            Math.Max(0, (int)Math.Floor(CurrentPositionSeconds)),
            trackFinished || HasCurrentSongReachedTail(),
            trackFinished);

        await HandlePersonalFmAdvanceResultAsync(result);
    }

    public async Task PlayPreviousPersonalFmAsync()
    {
        var request = await _personalFmService.PlayPreviousAsync();
        if (request != null)
            await PlayPersonalFmRequestAsync(request);
    }

    public void ClearPersonalFmSession()
    {
        _personalFmService.Clear();
    }

    public bool AddSongToPersonalFmNext(SongItem? song)
    {
        if (!_personalFmService.AddToNext(song))
            return false;

        ResetTransitionPipeline(true);

        if (_loadCancellation is { IsCancellationRequested: false } cts)
            _ = EnsurePreparedNextTrackAsync(_playRequestVersion, cts.Token);

        return true;
    }

    private SongItem? GetUpcomingPersonalFmSong()
    {
        return _personalFmService.UpcomingSong;
    }

    private bool HasCurrentSongReachedTail()
    {
        if (TotalDurationSeconds <= 0)
            return false;

        return CurrentPositionSeconds >= Math.Max(0, TotalDurationSeconds - 2);
    }

    private void AdvancePersonalFmSessionForAutoTransition(SongItem? nextSong)
    {
        _personalFmService.AdvanceForAutoTransition(nextSong);
    }

    private async Task<bool> HandlePersonalFmAdvanceResultAsync(PersonalFmAdvanceResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.UnavailableMessage))
            ShowPersonalFmUnavailableToast(result.UnavailableMessage);

        if (!result.Success || result.PlaybackRequest == null)
            return false;

        await PlayPersonalFmRequestAsync(result.PlaybackRequest);
        return true;
    }

    private Task PlayPersonalFmRequestAsync(PersonalFmPlaybackRequest request)
    {
        return PlaySongAsync(request.Song, request.ContextList as IList<SongItem> ?? request.ContextList.AsValueEnumerable().ToList(), true);
    }

    private void OnPersonalFmServiceStateChanged()
    {
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
