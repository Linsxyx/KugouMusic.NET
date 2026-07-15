using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
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
        SchedulePlaybackQueueCacheSave();
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

    private async Task ShowSongBatchActionDialogSafelyAsync(IReadOnlyList<SongItem> songs, bool allowAddToPlaylist)
    {
        try
        {
            await _favoriteService.ShowSongBatchActionDialogAsync(songs, allowAddToPlaylist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开批量操作对话框失败");
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

    public async Task ReplacePlaybackQueueAsync(IReadOnlyList<SongItem> songs, SongItem? startSong = null)
    {
        if (songs.Count == 0)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("没有可播放的歌曲")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }

        var contextList = songs as IList<SongItem> ?? songs.AsValueEnumerable().ToList();
        var normalizedStartSong = startSong is not null && contextList.Contains(startSong)
            ? startSong
            : contextList[0];
        var queueSnapshot = contextList as IReadOnlyList<SongItem> ?? contextList.AsValueEnumerable().ToList();

        if (IsPersonalFmSessionActive)
            ClearPersonalFmSession();

        _queueManager.RestoreQueue(queueSnapshot);
        await PlaySongAsync(normalizedStartSong, contextList);
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
            if (_player.IsStopped && CurrentPlayingSong != null)
            {
                _ = PlaySongAsync(CurrentPlayingSong);
                return;
            }

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
        OnPlaybackModeChanged(saveSettings: true);
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        _queueManager.ToggleShuffle(CurrentPlayingSong);
        OnPlaybackModeChanged(saveSettings: true);
    }

    public PlayMode CurrentPlayMode =>
        IsRepeatOneMode ? PlayMode.RepeatOne :
        IsShuffleMode ? PlayMode.Shuffle :
        PlayMode.Normal;

    public string CurrentPlayModeDisplayText => CurrentPlayMode switch
    {
        PlayMode.RepeatOne => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "列表循环"
    };

    [RelayCommand]
    private void CyclePlayMode()
    {
        var nextMode = CurrentPlayMode switch
        {
            PlayMode.Normal => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.RepeatOne,
            _ => PlayMode.Normal
        };

        ApplyPlayMode(nextMode, saveSettings: true);
    }

    [RelayCommand]
    private void SetPlayMode(PlayMode mode)
    {
        ApplyPlayMode(mode, saveSettings: true);
    }

    public void ApplySavedPlaybackModePreference()
    {
        ApplyPlayMode(SettingsManager.Settings.PlaybackMode, saveSettings: false);
    }

    private void ApplyPlayMode(PlayMode mode, bool saveSettings)
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

        OnPlaybackModeChanged(saveSettings);
    }

    private void OnPlaybackModeChanged(bool saveSettings)
    {
        ResetTransitionPipeline(true);
        OnPropertyChanged(nameof(IsRepeatOneMode));
        OnPropertyChanged(nameof(IsShuffleMode));
        OnPropertyChanged(nameof(CurrentPlayMode));
        OnPropertyChanged(nameof(CurrentPlayModeDisplayText));

        if (!saveSettings)
            return;

        SettingsManager.Settings.PlaybackMode = CurrentPlayMode;
        SettingsManager.Save();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        ClearPersonalFmSession();
        _queueManager.Clear();
        StopAndReset();
        SchedulePlaybackQueueCacheSave();
    }

    [RelayCommand]
    private async Task RemoveFromQueue(SongItem song) {
        if (song == CurrentPlayingSong) {
            if (_queueManager.PlaybackQueue.Count <= 1)
                StopAndReset();
            else
                await PlaySongAsync(_queueManager.GetNext(CurrentPlayingSong));
        }

        _queueManager.Remove(song);

        SchedulePlaybackQueueCacheSave();
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

    public async Task RestoreCachedPlaybackQueueAsync()
    {
        var snapshot = await Task.Run(_queueCacheService.Load);
        if (snapshot is not { Songs.Count: > 0 })
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isRestoringCachedQueue = true;
            try
            {
                _queueManager.RestoreQueue(snapshot.Songs);
                StopAndReset();

                var currentSong = ResolveCachedCurrentSong(snapshot);
                if (currentSong == null)
                    return;

                if (CurrentPlayingSong != null)
                    CurrentPlayingSong.IsPlaying = false;

                currentSong.IsPlaying = true;
                CurrentPlayingSong = currentSong;
                DisplayedPlayingSong = currentSong;
                IsLiked = _favoriteService.IsLiked(currentSong.Hash);
                TotalDurationSeconds = currentSong.DurationSeconds;
                CurrentPositionSeconds = 0;
                IsPlayingAudio = false;
                SyncDisplayPlaybackQueue();
            }
            finally
            {
                _isRestoringCachedQueue = false;
            }
        });
    }

    partial void OnCurrentPlayingSongChanged(SongItem? value)
    {
        SchedulePlaybackQueueCacheSave();
    }

    private SongItem? ResolveCachedCurrentSong(PlaybackQueueCacheSnapshot snapshot)
    {
        if (snapshot.Songs.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(snapshot.CurrentSongKey))
            return snapshot.Songs[0];

        return snapshot.Songs.AsValueEnumerable().FirstOrDefault(
                   song => string.Equals(
                       PlaybackQueueCacheService.BuildSongKey(song),
                       snapshot.CurrentSongKey,
                       StringComparison.Ordinal))
               ?? snapshot.Songs[0];
    }

    private void SchedulePlaybackQueueCacheSave()
    {
        if (_isRestoringCachedQueue || IsPersonalFmSessionActive)
            return;

        var previous = Interlocked.Exchange(
            ref _queueCacheSaveCancellation,
            new CancellationTokenSource());
        previous?.Cancel();

        var cancellation = _queueCacheSaveCancellation;
        if (cancellation == null)
            return;

        _ = SavePlaybackQueueCacheAfterDelayAsync(cancellation);
    }

    private async Task SavePlaybackQueueCacheAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token);
            var songs = _queueManager.PlaybackQueue.AsValueEnumerable().ToList();
            var currentSong = CurrentPlayingSong;
            await Task.Run(() => _queueCacheService.Save(songs, currentSong), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_queueCacheSaveCancellation, cancellation))
                Interlocked.CompareExchange(ref _queueCacheSaveCancellation, null, cancellation);

            cancellation.Dispose();
        }
    }

    private void CancelAndDisposeQueueCacheSaveCancellation()
    {
        var cancellation = Interlocked.Exchange(ref _queueCacheSaveCancellation, null);
        if (cancellation == null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
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

        Dispatcher.UIThread.Post(() => _ = PlayNext());
    }
}
