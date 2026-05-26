using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

public sealed class PlaybackAudioEffectsService(
    IPlaybackCoordinator playbackCoordinator,
    ILogger<PlaybackAudioEffectsService> logger)
{
    private readonly DualTrackAudioPlayer _player = playbackCoordinator.Player;
    private bool _isVolumeNormalizationEnabled;

    public void Initialize(bool volumeNormalizationEnabled)
    {
        _isVolumeNormalizationEnabled = volumeNormalizationEnabled;
        _player.SetVolumeNormalizationEnabled(volumeNormalizationEnabled);
    }

    public void ApplyCustomEQ(float[] gains)
    {
        _player.SetEQ(gains);
    }

    public void UpdateAudioEffects(string preset, bool surround)
    {
        if (preset == "自定义")
            _player.SetEQ(SettingsManager.Settings.CustomEqGains);
        else
            _player.SetEQ(GetEqPreset(preset));

        _player.SetSurround(surround);
    }

    public async Task SetVolumeNormalizationEnabledAsync(SongItem? currentSong)
    {
        _isVolumeNormalizationEnabled = true;
        _player.SetVolumeNormalizationEnabled(true);
        await RefreshCurrentTrackNormalizationAsync(currentSong);
    }

    public void DisableVolumeNormalization()
    {
        _isVolumeNormalizationEnabled = false;
        _player.SetVolumeNormalizationEnabled(false);
        _player.SetActiveNormalizationGain(1.0f);
    }

    public async Task RefreshCurrentTrackNormalizationAsync(SongItem? currentSong)
    {
        var source = _player.ActiveSource;
        if (!_isVolumeNormalizationEnabled || currentSong == null || string.IsNullOrWhiteSpace(source))
            return;

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
            logger.LogDebug(ex, "刷新当前歌曲音量平衡失败");
        }
    }

    public async Task<float> ResolveNormalizationGainAsync(
        string source,
        bool isLocal,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (!_isVolumeNormalizationEnabled || string.IsNullOrWhiteSpace(source))
            return 1.0f;

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
            logger.LogDebug(ex, "估算歌曲音量补偿失败");
            return 1.0f;
        }
    }

    private static float[] GetEqPreset(string preset)
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
}
