using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SimpleAudio;
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

    [RelayCommand]
    private void SeekToLyricLine(TimeSpan position)
    {
        var targetSeconds = position.TotalSeconds;
        var maxSeconds = TotalDurationSeconds > 0
            ? TotalDurationSeconds
            : Math.Max(0, targetSeconds);

        CurrentPositionSeconds = Math.Clamp(targetSeconds, 0, maxSeconds);
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
        _audioEffectsService.ApplyCustomEQ(gains);
        OnPropertyChanged(nameof(MusicQuality));
    }

    public void UpdateAudioEffects(string preset, bool surround)
    {
        _audioEffectsService.UpdateAudioEffects(preset, surround);
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
        if (enabled)
            _ = _audioEffectsService.SetVolumeNormalizationEnabledAsync(CurrentPlayingSong);
        else
            _audioEffectsService.DisableVolumeNormalization();
    }

    public bool SetOutputDevice(int deviceId)
    {
        return _player.SetOutputDevice(deviceId);
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

            var normalizationGain = await _audioEffectsService.ResolveNormalizationGainAsync(
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

    private void ResetVisualizerBars()
    {
        _visualizerService.Reset();
    }

    private void UpdateNowPlayingVisualizer(AudioAnalysisSnapshot snapshot)
    {
        _visualizerService.Update(snapshot);
    }
}
