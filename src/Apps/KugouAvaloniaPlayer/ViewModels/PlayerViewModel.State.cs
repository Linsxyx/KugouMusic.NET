using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using SimpleAudio;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped || IsDraggingProgress)
            return;

        IsBuffering = _player.IsStalled;

        var pos = _player.GetPosition();
        CurrentPositionSeconds = pos.TotalSeconds;
        var analysisSnapshot = _player.GetActiveAnalysisSnapshot();
        CaptureTailTelemetry(analysisSnapshot);
        UpdateNowPlayingVisualizer(analysisSnapshot);

        if (!_isDelayingVisualSwitch)
        {
            SyncCurrentLyricFromPosition(pos.TotalMilliseconds);
        }

        if (_autoTransitionStarted &&
            !_player.IsCrossfading &&
            _pendingTransitionProfile == null &&
            _pendingTransitionSong == null &&
            _preparedNextTrack == null &&
            _preparedNextSong == null)
        {
            ResetTransitionPipeline(false);
        }

        if (!IsSeamlessTransitionEnabled || _player.IsCrossfading || IsSwitchingQuality)
            return;

        var remainingSec = Math.Max(0, TotalDurationSeconds - pos.TotalSeconds);
        var requestVersion = _playRequestVersion;
        var currentLoadCts = _loadCancellation;
        if (currentLoadCts == null || currentLoadCts.IsCancellationRequested)
            return;

        if (remainingSec <= PreloadWindowSec)
            _ = EnsurePreparedNextTrackAsync(requestVersion, currentLoadCts.Token);

        if (remainingSec <= AnalysisWindowSec)
            _ = EnsureTransitionAnalysisAsync(requestVersion, currentLoadCts.Token);

        TryStartAutoCrossfade(remainingSec);
    }

    partial void OnCurrentPositionSecondsChanged(double value)
    {
        _systemMediaSessionService.UpdateTimeline(value, TotalDurationSeconds);

        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5)
            return;

        if (_player.IsCrossfading)
        {
            _player.AbortCrossfade();
            ResetTransitionPipeline(false);
        }

        _player.SetPosition(TimeSpan.FromSeconds(value));
        SyncCurrentLyricFromPosition(value * 1000);
    }

    partial void OnMusicVolumeChanged(float value)
    {
        _player.SetVolume(value);
        SettingsManager.Settings.MusicVolume = value;
        SettingsManager.Save();
    }

    partial void OnDisplayedPlayingSongChanged(SongItem? value)
    {
        BeginNowPlayingSongTransition();
        _ = _systemMediaSessionService.UpdateSongAsync(value);
        _systemMediaSessionService.UpdateTimeline(CurrentPositionSeconds, TotalDurationSeconds);
    }

    partial void OnIsPlayingAudioChanged(bool value)
    {
        _systemMediaSessionService.UpdatePlaybackState(value);
    }

    partial void OnTotalDurationSecondsChanged(double value)
    {
        _systemMediaSessionService.UpdateTimeline(CurrentPositionSeconds, value);
    }

    partial void OnMusicQualityChanged(string value)
    {
        SettingsManager.Settings.MusicQuality = value;
        SettingsManager.Save();

        if (!string.Equals(QualitySelection, value, StringComparison.OrdinalIgnoreCase))
            SetQualitySelectionSilently(value);
    }

    partial void OnQualitySelectionChanged(string value)
    {
        if (_isSyncingQualitySelection)
            return;

        if (string.IsNullOrWhiteSpace(value))
            return;

        if (string.Equals(value, MusicQuality, StringComparison.OrdinalIgnoreCase))
            return;

        _ = SwitchQualityAsync(value);
    }

    private void SetQualitySelectionSilently(string value)
    {
        if (string.Equals(QualitySelection, value, StringComparison.OrdinalIgnoreCase))
            return;

        _isSyncingQualitySelection = true;
        try
        {
            QualitySelection = value;
        }
        finally
        {
            _isSyncingQualitySelection = false;
        }
    }

    private void RevertQualitySelectionToCurrentQuality()
    {
        SetQualitySelectionSilently(MusicQuality);
    }

    public async Task LoadLikeListAsync()
    {
        await _favoriteService.LoadLikeListAsync();
    }

    public void ApplyCustomEQ(float[] gains)
    {
        _player.SetEQ(gains);
        OnPropertyChanged(nameof(MusicQuality));
    }

    public void UpdateAudioEffects(string preset, bool surround)
    {
        if (preset == "自定义")
            _player.SetEQ(SettingsManager.Settings.CustomEqGains);
        else
            _player.SetEQ(GetEqPreset(preset));

        _player.SetSurround(surround);
    }

    public void SetSeamlessTransitionEnabled(bool enabled)
    {
        if (IsSeamlessTransitionEnabled == enabled)
            return;

        IsSeamlessTransitionEnabled = enabled;
        if (!enabled)
        {
            _player.AbortCrossfade();
            ResetTransitionPipeline(true);
        }
    }

    public void SetNowPlayingVisualizerEnabled(bool enabled)
    {
        if (IsNowPlayingVisualizerEnabled == enabled)
            return;

        IsNowPlayingVisualizerEnabled = enabled;
        if (!enabled)
            ResetVisualizerBars();
    }

    public void SetVolumeNormalizationEnabled(bool enabled)
    {
        _isVolumeNormalizationEnabled = enabled;
        _player.SetVolumeNormalizationEnabled(enabled);

        if (!enabled)
        {
            _player.SetActiveNormalizationGain(1.0f);
            return;
        }

        _ = RefreshCurrentTrackNormalizationAsync();
    }

    public async Task<bool> SwitchQualityAsync(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || !QualityOptions.Contains(quality, StringComparer.OrdinalIgnoreCase))
        {
            RevertQualitySelectionToCurrentQuality();
            return false;
        }

        if (string.Equals(MusicQuality, quality, StringComparison.OrdinalIgnoreCase))
            return true;

        var currentSong = CurrentPlayingSong;
        if (currentSong == null)
        {
            MusicQuality = quality;
            return true;
        }

        await _playSongLock.WaitAsync();
        IsSwitchingQuality = true;
        try
        {
            currentSong = CurrentPlayingSong;
            if (currentSong == null)
            {
                MusicQuality = quality;
                return true;
            }

            var currentLoadCts = new CancellationTokenSource();
            var sourceInfo = await _playbackSourceResolver.ResolveAsync(currentSong, quality, currentLoadCts.Token);
            if (sourceInfo.IsLocal)
            {
                currentLoadCts.Dispose();
                MusicQuality = quality;
                return true;
            }

            if (!sourceInfo.Success || string.IsNullOrWhiteSpace(sourceInfo.Source))
            {
                currentLoadCts.Dispose();
                RevertQualitySelectionToCurrentQuality();
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("切换音质失败")
                    .WithContent(GetQualitySwitchFailureMessage(sourceInfo.FailureReason))
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return false;
            }

            var wasPlaying = _player.IsPlaying;
            var resumePosition = CurrentPositionSeconds;

            CancelAndDisposeLoadCancellation();
            ResetTransitionPipeline(true);
            _loadCancellation = currentLoadCts;

            _playbackTimer.Stop();
            _player.Stop();

            var normalizationGain = await ResolveNormalizationGainAsync(
                sourceInfo.Source,
                sourceInfo.IsLocal,
                currentSong.DurationSeconds,
                currentLoadCts.Token);
            var loadSuccess = await _playbackCoordinator.LoadAsync(sourceInfo.Source, currentSong.Name,
                normalizationGain, AudioLoadTimeout, currentLoadCts.Token);
            if (!loadSuccess)
            {
                RevertQualitySelectionToCurrentQuality();
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("切换音质失败")
                    .WithContent("新的音频流加载失败。")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return false;
            }

            MusicQuality = quality;
            _player.SetVolume(MusicVolume);
            TotalDurationSeconds =
                currentSong.DurationSeconds > 0 ? currentSong.DurationSeconds : _player.GetDuration().TotalSeconds;

            var safePosition = Math.Clamp(resumePosition, 0, Math.Max(TotalDurationSeconds - 0.25, 0));
            _player.SetPosition(TimeSpan.FromSeconds(safePosition));
            CurrentPositionSeconds = safePosition;

            SyncCurrentLyricFromPosition(safePosition * 1000, preserveExistingText: true);

            if (wasPlaying)
            {
                _player.Play();
                _playbackTimer.Start();
                IsPlayingAudio = true;
            }
            else
            {
                IsPlayingAudio = false;
            }

            return true;
        }
        catch (Exception ex)
        {
            RevertQualitySelectionToCurrentQuality();
            _logger.LogError(ex, "切换音质失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("切换音质失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return false;
        }
        finally
        {
            IsSwitchingQuality = false;
            _playSongLock.Release();
        }
    }

    private void SyncCurrentLyricFromPosition(double currentMs, bool preserveExistingText = false)
    {
        var activeLine = _lyricsService.SyncLyrics(currentMs);
        var activeIndex = activeLine == null ? -1 : _lyricsService.CurrentLyricIndex;
        var nextLine = _lyricsService.GetLineAt(activeIndex + 1);

        if (activeLine == CurrentLyricLine &&
            activeIndex == CurrentLyricIndex &&
            nextLine == NextLyricLine)
            return;

        CurrentLyricLine = activeLine;
        CurrentLyricIndex = activeIndex;
        NextLyricLine = nextLine;
    }

    private static string GetQualitySwitchFailureMessage(PlaybackSourceFailureReason reason)
    {
        return reason switch
        {
            PlaybackSourceFailureReason.LoginRequired => "登录后才能切换在线播放音质。",
            PlaybackSourceFailureReason.EmptyUrl => "没有获取到新的播放地址。",
            _ => "当前音质暂不可用，已保持原音质。"
        };
    }

    private float[] GetEqPreset(string preset)
    {
        return preset switch
        {
            "流行" => [-2f, 0f, -5.0f, -1.0f, 0f, 0.0f, 0f, -3.0f, 0f, 0f],
            "摇滚" => [4.0f, 1.0f, -2.0f, 0f, 0f, -2.0f, 0f, -2.0f, 1.0f, 4.0f],
            "爵士" => [0f, 0f, 0f, -1.0f, -1.0f, -3.0f, 0f, 0f, 0f, 0f],
            "古典" => [0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 3.0f, 1.0f, 6.0f, 2.0f, 6.0f],
            "嘻哈" => [3.0f, 0f, -3.0f, 0f, 0f, -3.0f, 0f, 0.0f, 0f, 2.0f],
            "布鲁斯" => [2.0f, 2.0f, -6.0f, -2.0f, 3.0f, 1.0f, 0f, 1.0f, 0.0f, 2.0f],
            "电子音乐" => [3.0f, 1.0f, -1.0f, 0f, 0f, -3.0f, 0f, 0f, 0f, 0f],
            "金属" => [2.0f, 0f, 0f, -1.0f, -1.0f, -4.0f, 0f, 0f, 0f, 0f],
            _ => [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]
        };
    }

    private static AvaloniaList<AudioVisualizerBarViewModel> CreateVisualizerBars()
    {
        var bars = new AvaloniaList<AudioVisualizerBarViewModel>();
        for (var i = 0; i < VisualizerBarCount; i++)
            bars.Add(new AudioVisualizerBarViewModel());

        return bars;
    }

    private async Task RefreshCurrentTrackNormalizationAsync()
    {
        var currentSong = CurrentPlayingSong;
        var source = _player.ActiveSource;
        if (!_isVolumeNormalizationEnabled || currentSong == null || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        try
        {
            var gain = await ResolveNormalizationGainAsync(
                source,
                !string.IsNullOrWhiteSpace(currentSong.LocalFilePath) && File.Exists(currentSong.LocalFilePath),
                currentSong.DurationSeconds,
                CancellationToken.None);
            _player.SetActiveNormalizationGain(gain);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "刷新当前歌曲音量平衡失败");
        }
    }

    private async Task<float> ResolveNormalizationGainAsync(
        string source,
        bool isLocal,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (!_isVolumeNormalizationEnabled || string.IsNullOrWhiteSpace(source))
        {
            return 1.0f;
        }

        try
        {
            return await TrackVolumeNormalizer.EstimateGainAsync(source, isLocal, durationSeconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "估算歌曲音量补偿失败");
            return 1.0f;
        }
    }

    private void ResetVisualizerBars()
    {
        for (var i = 0; i < NowPlayingVisualizerBars.Count; i++)
        {
            NowPlayingVisualizerBars[i].Height = VisualizerMinHeight;
            NowPlayingVisualizerBars[i].Opacity = 0.1;
        }
    }

    private void UpdateNowPlayingVisualizer(AudioAnalysisSnapshot snapshot)
    {
        var spectrumBands = snapshot.SpectrumBands;
        if (spectrumBands == null || spectrumBands.Count == 0)
        {
            ResetVisualizerBars();
            return;
        }

        var energyBoost = Math.Clamp(snapshot.Rms * 8.5, 0d, 1d);
        var brightnessBoost = Math.Clamp(snapshot.Brightness * 1.25, 0d, 1d);
        var barCount = NowPlayingVisualizerBars.Count;

        for (var i = 0; i < barCount; i++)
        {
            var phase = barCount <= 1 ? 0d : i / (barCount - 1d);
            var band = SampleSpectrumBand(spectrumBands, phase);
            var shapedBand = Math.Pow(Math.Clamp(band, 0d, 1d), 0.72d);
            var centerLift = 0.82d + Math.Sin(phase * Math.PI) * 0.12d;
            var ripple = 1d + Math.Sin(snapshot.PositionSeconds * 4.8d + i * 0.18d) * energyBoost * 0.035d;
            var target = Math.Clamp(
                (shapedBand * 0.58d + energyBoost * 0.14d + brightnessBoost * 0.04d) * centerLift * ripple,
                0d,
                1d);
            var targetHeight = VisualizerMinHeight + target * VisualizerHeightRange;
            var bar = NowPlayingVisualizerBars[i];

            var smoothing = targetHeight >= bar.Height ? 0.46d : 0.16d;
            bar.Height += (targetHeight - bar.Height) * smoothing;
            bar.Opacity = Math.Clamp(0.1d + Math.Pow(target, 0.9d) * 0.5d, 0.1d, 0.6d);
        }
    }

    private static double SampleSpectrumBand(IReadOnlyList<float> spectrumBands, double phase)
    {
        if (spectrumBands.Count == 1)
            return spectrumBands[0];

        var position = Math.Clamp(phase, 0d, 1d) * (spectrumBands.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = Math.Min(lowerIndex + 1, spectrumBands.Count - 1);
        var mix = position - lowerIndex;
        var lower = Math.Clamp(spectrumBands[lowerIndex], 0f, 1f);
        var upper = Math.Clamp(spectrumBands[upperIndex], 0f, 1f);

        return lower + (upper - lower) * mix;
    }
}
