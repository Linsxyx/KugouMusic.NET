using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private async Task<(string? Source, bool IsLocal)> ResolvePlaybackSourceAsync(
        SongItem song,
        CancellationToken cancellationToken)
    {
        var localFilePath = song.LocalFilePath;
        var isLocalSong = !string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath);
        if (isLocalSong)
            return (localFilePath, true);

        cancellationToken.ThrowIfCancellationRequested();
        var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
        if (playData == null || playData.Status != 1)
            return (null, false);

        var url = playData.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return (url, false);
    }

    private void StartLyricsLoad(SongItem song, bool isLocal)
    {
        var loadVersion = Interlocked.Increment(ref _lyricsLoadVersion);
        BeginNowPlayingLyricsTransition();
        _ = LoadLyricsForCurrentSongAsync(song, isLocal, loadVersion);
    }

    private async Task LoadLyricsForCurrentSongAsync(SongItem song, bool isLocal, int loadVersion)
    {
        try
        {
            if (isLocal && !string.IsNullOrWhiteSpace(song.LocalFilePath))
                await _lyricsService.LoadLocalLyricsAsync(song.LocalFilePath);
            else
                await _lyricsService.LoadOnlineLyricsAsync(song.Hash, song.Name);

            if (loadVersion != _lyricsLoadVersion || CurrentPlayingSong != song)
                return;

            var activeLine = _lyricsService.SyncLyrics(CurrentPositionSeconds * 1000);
            CurrentLyricLine = activeLine;
            CurrentLyricText = activeLine?.Content ?? "暂无歌词";
            CurrentLyricTrans = activeLine?.Translation ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "歌词加载失败");
            if (loadVersion != _lyricsLoadVersion || CurrentPlayingSong != song)
                return;

            CurrentLyricLine = null;
            CurrentLyricText = "暂无歌词";
            CurrentLyricTrans = "";
        }
        finally
        {
            if (loadVersion == _lyricsLoadVersion && CurrentPlayingSong == song)
                CompleteNowPlayingLyricsTransition();
        }
    }

    private void BeginDelayedVisualSwitch(SongItem song, bool isLocal)
    {
        CancelAndDisposeDelayedVisualSwitchCancellation();
        _isDelayingVisualSwitch = true;

        var cts = new CancellationTokenSource();
        _delayedVisualSwitchCancellation = cts;
        _ = CompleteDelayedVisualSwitchAsync(song, isLocal, cts);
    }

    private async Task CompleteDelayedVisualSwitchAsync(SongItem song, bool isLocal, CancellationTokenSource cts)
    {
        try
        {
            var cancellationToken = cts.Token;
            await Task.Delay(SeamlessVisualSwitchDelay, cancellationToken);
            if (cancellationToken.IsCancellationRequested || CurrentPlayingSong != song)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || CurrentPlayingSong != song)
                    return;

                DisplayedPlayingSong = song;
            });

            _isDelayingVisualSwitch = false;
            StartLyricsLoad(song, isLocal);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_delayedVisualSwitchCancellation, cts))
            {
                _delayedVisualSwitchCancellation.Dispose();
                _delayedVisualSwitchCancellation = null;
            }
        }
    }

    private void BeginNowPlayingSongTransition()
    {
        NowPlayingArtworkOpacity = 0.55;
        NowPlayingArtworkTranslateY = 16;
        Dispatcher.UIThread.Post(() =>
        {
            NowPlayingArtworkOpacity = 1;
            NowPlayingArtworkTranslateY = 0;
        }, DispatcherPriority.Render);
    }

    private void BeginNowPlayingLyricsTransition()
    {
        NowPlayingLyricsOpacity = 0;
        NowPlayingLyricsTranslateY = 28;
    }

    private void CompleteNowPlayingLyricsTransition()
    {
        Dispatcher.UIThread.Post(() =>
        {
            NowPlayingLyricsOpacity = 1;
            NowPlayingLyricsTranslateY = 0;
        }, DispatcherPriority.Render);
    }
}
