using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

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
            OnPropertyChanged(nameof(HasDisplayPlaybackQueue));
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

    private void AddLoadedSongsToQueue(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("没有可添加的歌曲")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }

        if (IsPersonalFmSessionActive)
            ClearPersonalFmSession();

        _queueManager.AddToEnd(songs);
        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已添加到播放列表")
            .WithContent($"已添加 {songs.Count} 首歌曲")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
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
    private void ToggleRepeatOneMode()
    {
        _queueManager.ToggleRepeatOne(CurrentPlayingSong);
        OnPlaybackModeChanged();
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        _queueManager.ToggleShuffle(CurrentPlayingSong);
        OnPlaybackModeChanged();
    }

    public PlayMode CurrentPlayMode =>
        IsRepeatOneMode ? PlayMode.RepeatOne :
        IsShuffleMode ? PlayMode.Shuffle :
        PlayMode.Normal;

    [RelayCommand]
    private void SetPlayMode(PlayMode mode)
    {
        if (mode == CurrentPlayMode)
            return;

        switch (mode)
        {
            case PlayMode.RepeatOne:
                _queueManager.ToggleRepeatOne(CurrentPlayingSong);
                break;
            case PlayMode.Shuffle:
                _queueManager.ToggleShuffle(CurrentPlayingSong);
                break;
            case PlayMode.Normal:
                if (IsRepeatOneMode)
                    _queueManager.ToggleRepeatOne(CurrentPlayingSong);
                if (IsShuffleMode)
                    _queueManager.ToggleShuffle(CurrentPlayingSong);
                break;
        }

        OnPlaybackModeChanged();
    }

    private void OnPlaybackModeChanged()
    {
        ResetTransitionPipeline(true);
        OnPropertyChanged(nameof(IsRepeatOneMode));
        OnPropertyChanged(nameof(IsShuffleMode));
        OnPropertyChanged(nameof(CurrentPlayMode));
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

        if (IsRepeatOneMode)
        {
            Dispatcher.UIThread.Post(async () => await PlaySongAsync(CurrentPlayingSong));
            return;
        }

        Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }
}
