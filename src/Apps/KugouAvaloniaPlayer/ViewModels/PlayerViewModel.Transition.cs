using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimpleAudio;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private void ResetTransitionPipeline(bool cancelPreparedTrack)
    {
        CancelAndDisposeTransitionCancellation();
        _autoTransitionStarted = false;
        _isAnalyzingTransition = false;
        _isPreparingNextTrack = false;
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
        _preparedNextIsLocal = false;
        PreparedNextCover = null;
        _activeTransitionProfile = null;
        _analysisFailureSongKey = null;
        _prepareFailureSongKey = null;
        if (cancelPreparedTrack)
        {
            _player.AbortCrossfade();
            _player.CancelPrepared();
        }
    }

    private void ResetTailTelemetry()
    {
        _tailTelemetry.Clear();
    }

    private void CaptureTailTelemetry(AudioAnalysisSnapshot snapshot)
    {
        if (snapshot.DurationSeconds <= 0 || snapshot.PositionSeconds < 0)
            return;

        if (_tailTelemetry.Count > 0 && snapshot.PositionSeconds + 0.15 < _tailTelemetry[^1].PositionSeconds)
            _tailTelemetry.Clear();

        _tailTelemetry.Add(new PlaybackTelemetryPoint(snapshot.PositionSeconds, snapshot.DurationSeconds, snapshot.Rms,
            snapshot.Brightness));

        while (_tailTelemetry.Count > TailTelemetryCapacity)
            _tailTelemetry.RemoveAt(0);

        var minPosition = Math.Max(0, snapshot.PositionSeconds - 15.0);
        _tailTelemetry.RemoveAll(x => x.PositionSeconds < minPosition);
    }

    private TailPlaybackMetrics? BuildTailPlaybackMetrics()
    {
        if (_tailTelemetry.Count == 0)
            return null;

        var points = _tailTelemetry.Where(x => x.PositionSeconds >= _tailTelemetry[^1].PositionSeconds - 8.0).ToList();
        if (points.Count == 0)
            return null;

        var peakRms = points.Max(x => x.Rms);
        var silenceThreshold = Math.Max(peakRms * 0.18, 0.0025);
        var lastActive = points.LastOrDefault(x => x.Rms >= silenceThreshold);
        var lastPoint = points[^1];
        var tailSilenceSec = lastActive.PositionSeconds <= 0
            ? 0
            : Math.Max(0, lastPoint.PositionSeconds - lastActive.PositionSeconds);

        return new TailPlaybackMetrics
        {
            TailRms = points.Average(x => x.Rms),
            TailBrightness = points.Average(x => x.Brightness),
            TailSilenceSec = tailSilenceSec
        };
    }

    private SongItem? GetUpcomingSong()
    {
        if (IsPersonalFmSessionActive)
            return GetUpcomingPersonalFmSong();

        if (CurrentPlayingSong == null)
            return null;

        if (IsRepeatOneMode)
            return CurrentPlayingSong;

        if (_queueManager.PlaybackQueue.Count <= 1)
            return null;

        var nextSong = _queueManager.GetNext(CurrentPlayingSong);
        return nextSong == CurrentPlayingSong ? null : nextSong;
    }

    private async Task EnsurePreparedNextTrackAsync(int requestVersion, CancellationToken cancellationToken)
    {
        if (_autoTransitionStarted || _isPreparingNextTrack || _player.IsCrossfading)
            return;

        var nextSong = GetUpcomingSong();
        if (nextSong == null)
            return;

        var nextSongKey = BuildSongTransitionKey(nextSong);
        if (_preparedNextSong == nextSong && _player.HasPreparedTrack &&
            !string.IsNullOrWhiteSpace(_preparedNextSource))
            return;

        if (string.Equals(_prepareFailureSongKey, nextSongKey, StringComparison.Ordinal))
            return;

        _isPreparingNextTrack = true;
        try
        {
            var transitionCts = EnsureTransitionCancellation();
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transitionCts.Token);
            var sourceInfo = await ResolvePlaybackSourceAsync(nextSong, linkedCts.Token);
            if (requestVersion != _playRequestVersion || linkedCts.IsCancellationRequested ||
                !sourceInfo.Success || string.IsNullOrWhiteSpace(sourceInfo.Source))
                return;

            if (!_player.PrepareNext(sourceInfo.Source))
            {
                _prepareFailureSongKey = nextSongKey;
                return;
            }

            _prepareFailureSongKey = null;
            _preparedNextSong = nextSong;
            _preparedNextSource = sourceInfo.Source;
            _preparedNextIsLocal = sourceInfo.IsLocal;
            PreparedNextCover = nextSong.Cover;
            _preparedNextTrack = new PreparedTrack
            {
                Id = nextSongKey,
                Source = sourceInfo.Source,
                IsLocal = sourceInfo.IsLocal,
                DurationSeconds = nextSong.DurationSeconds
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预加载下一首失败");
            _prepareFailureSongKey = nextSongKey;
        }
        finally
        {
            _isPreparingNextTrack = false;
        }
    }

    private async Task EnsureTransitionAnalysisAsync(int requestVersion, CancellationToken cancellationToken)
    {
        var preparedNextTrack = _preparedNextTrack;
        var preparedNextSong = _preparedNextSong;
        if (_autoTransitionStarted || _isAnalyzingTransition || _player.IsCrossfading || preparedNextTrack == null ||
            preparedNextSong == null)
            return;

        var nextSongKey = BuildSongTransitionKey(preparedNextSong);
        if (_pendingTransitionSong == preparedNextSong && _pendingTransitionProfile != null)
            return;

        if (string.Equals(_analysisFailureSongKey, nextSongKey, StringComparison.Ordinal))
            return;

        var currentSong = CurrentPlayingSong;
        var currentSource = _player.ActiveSource;
        if (currentSong == null || string.IsNullOrWhiteSpace(currentSource))
            return;

        _isAnalyzingTransition = true;
        try
        {
            var transitionCts = EnsureTransitionCancellation();
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transitionCts.Token);
            var currentTrack = new PreparedTrack
            {
                Id = BuildSongTransitionKey(currentSong),
                Source = currentSource,
                IsLocal = !string.IsNullOrWhiteSpace(currentSong.LocalFilePath) &&
                          File.Exists(currentSong.LocalFilePath),
                DurationSeconds = TotalDurationSeconds,
                TailMetrics = BuildTailPlaybackMetrics()
            };

            var profile =
                await _transitionAnalysisService.AnalyzeAsync(currentTrack, preparedNextTrack, linkedCts.Token);
            if (requestVersion != _playRequestVersion || linkedCts.IsCancellationRequested ||
                _autoTransitionStarted || preparedNextSong != _preparedNextSong || preparedNextTrack != _preparedNextTrack)
                return;

            _analysisFailureSongKey = null;
            _pendingTransitionSong = preparedNextSong;
            _pendingTransitionProfile = profile;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_autoTransitionStarted || preparedNextSong != _preparedNextSong || preparedNextTrack != _preparedNextTrack)
                return;

            _logger.LogDebug(ex, "过渡分析失败，回退默认参数");
            _analysisFailureSongKey = nextSongKey;
            _pendingTransitionSong = preparedNextSong;
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
            return;

        if (_pendingTransitionProfile == null || _pendingTransitionSong != _preparedNextSong)
        {
            if (remainingSec > FallbackMixEntrySec)
                return;

            _pendingTransitionSong = _preparedNextSong;
            _pendingTransitionProfile = BuildFallbackTransitionProfile(remainingSec);
        }

        if (remainingSec > _pendingTransitionProfile.MixEntrySec)
            return;

        if (!_player.StartCrossfade(_pendingTransitionProfile, remainingSec))
            return;

        _autoTransitionStarted = true;
        _activeTransitionProfile = _pendingTransitionProfile;
        AdvancePersonalFmSessionForAutoTransition(_preparedNextSong);

        var oldSong = CurrentPlayingSong;
        if (oldSong != null)
            oldSong.IsPlaying = false;

        CurrentPlayingSong = _preparedNextSong;
        CurrentPlayingSong.IsPlaying = true;
        IsLiked = _favoriteService.IsLiked(CurrentPlayingSong.Hash);
        CurrentPositionSeconds = 0;
        TotalDurationSeconds = CurrentPlayingSong.DurationSeconds > 0
            ? CurrentPlayingSong.DurationSeconds
            : _player.GetDuration().TotalSeconds;
        _ = _historyService.RecordPlayedAsync(CurrentPlayingSong);
        BeginDelayedVisualSwitch(CurrentPlayingSong, _preparedNextIsLocal);
        ResetTailTelemetry();
        _pendingTransitionProfile = null;
        _pendingTransitionSong = null;
        _preparedNextTrack = null;
        _preparedNextSong = null;
        _preparedNextSource = null;
        _preparedNextIsLocal = false;
        PreparedNextCover = null;
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

    private CancellationTokenSource EnsureTransitionCancellation()
    {
        if (_transitionWorkCancellation == null || _transitionWorkCancellation.IsCancellationRequested)
        {
            CancelAndDisposeTransitionCancellation();
            _transitionWorkCancellation = new CancellationTokenSource();
        }

        return _transitionWorkCancellation;
    }

    private static string BuildSongTransitionKey(SongItem song)
    {
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{song.LocalFilePath}";

        return $"remote:{song.Hash}:{song.DurationSeconds:0.###}";
    }

    private readonly record struct PlaybackTelemetryPoint(
        double PositionSeconds,
        double DurationSeconds,
        double Rms,
        double Brightness);
}
