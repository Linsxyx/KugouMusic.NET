using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private void OnPlaybackQueueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsPersonalFmSessionActive)
            return;

        SyncDisplayPlaybackQueue();
    }

    private void SyncDisplayPlaybackQueue()
    {
        void Update()
        {
            DisplayPlaybackQueue.Clear();
            if (IsPersonalFmSessionActive)
                DisplayPlaybackQueue.AddRange(GetPersonalFmQueueSongs());
            else
                DisplayPlaybackQueue.AddRange(_queueManager.PlaybackQueue);

            OnPropertyChanged(nameof(DisplayPlaybackQueueCount));
        }

        if (Dispatcher.UIThread.CheckAccess())
            Update();
        else
            Dispatcher.UIThread.Post(Update);
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
        if (IsPersonalFmSessionActive)
        {
            await PlayNextPersonalFmAsync();
            return;
        }

        await PlaySongAsync(_queueManager.GetNext(CurrentPlayingSong));
    }

    [RelayCommand]
    private async Task PlayPrevious()
    {
        if (IsPersonalFmSessionActive)
        {
            await PlayPreviousPersonalFmAsync();
            return;
        }

        await PlaySongAsync(_queueManager.GetPrevious(CurrentPlayingSong));
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        _queueManager.ToggleShuffle(CurrentPlayingSong);
        OnPropertyChanged(nameof(IsShuffleMode));
    }

    [RelayCommand]
    private void ClearQueue()
    {
        ClearPersonalFmSession();
        _queueManager.Clear();
        StopAndReset();
    }

    [RelayCommand]
    private void RemoveFromQueue(SongItem song)
    {
        _queueManager.Remove(song);
        if (_queueManager.PlaybackQueue.Count == 0)
            StopAndReset();
    }

    [RelayCommand]
    private async Task ToggleLike()
    {
        if (CurrentPlayingSong == null)
            return;

        IsLiked = await _favoriteService.ToggleLikeAsync(CurrentPlayingSong, IsLiked);
    }

    private void StopAndReset()
    {
        CancelAndDisposeDelayedVisualSwitchCancellation();
        ResetTransitionPipeline(true);
        _playbackTimer.Stop();
        _playbackCoordinator.InvalidatePendingLoads();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentLyricLine = null;
        CurrentLyricIndex = -1;
        NextLyricLine = null;
        CurrentPositionSeconds = 0;
        _lyricsService.Clear();
        Interlocked.Increment(ref _lyricsLoadVersion);
        CompleteNowPlayingLyricsTransition();
        ResetTailTelemetry();
        ResetVisualizerBars();
    }

    private void OnPlaybackEnded()
    {
        if (IsPersonalFmSessionActive)
        {
            Dispatcher.UIThread.Post(async () => await PlayNextPersonalFmAsync(true));
            return;
        }

        Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }
}
