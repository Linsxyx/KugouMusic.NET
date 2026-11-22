using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using SimpleAudio;

namespace KaMusic.Cli.Tests;

// 验证终端版多声道适配模式的持久化、非法值回退以及设置项正向/反向循环切换。
public sealed class TerminalChannelModeTests
{
    [Fact]
    public void SettingsStore_PersistsAudioChannelModeAndNormalizesInvalidValues()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kamusic-cli-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TerminalSettingsStore(tempFile);
            store.Save(new TerminalAppSettings { AudioChannelMode = AudioChannelMode.HeadphoneSpatial });

            var loaded = store.Load();
            Assert.Equal(AudioChannelMode.HeadphoneSpatial, loaded.AudioChannelMode);

            File.WriteAllText(tempFile, """{"AudioChannelMode": 999}""");
            var fallback = store.Load();
            Assert.Equal(AudioChannelMode.Auto, fallback.AudioChannelMode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void PlaybackService_CyclesChannelModesForwardAndBackward()
    {
        using var test = new TerminalTestContext();
        var playback = test.Playback;

        Assert.Equal(AudioChannelMode.Auto, playback.AudioChannelMode);

        playback.CycleChannelMode(1);
        Assert.Equal(AudioChannelMode.HeadphoneSpatial, playback.AudioChannelMode);
        Assert.Contains("3D", playback.StatusMessage);

        playback.CycleChannelMode(1);
        Assert.Equal(AudioChannelMode.ForceStereo, playback.AudioChannelMode);

        playback.CycleChannelMode(1);
        Assert.Equal(AudioChannelMode.Passthrough, playback.AudioChannelMode);

        playback.CycleChannelMode(1);
        Assert.Equal(AudioChannelMode.Auto, playback.AudioChannelMode);

        playback.CycleChannelMode(-1);
        Assert.Equal(AudioChannelMode.Passthrough, playback.AudioChannelMode);
        Assert.Equal(AudioChannelMode.Passthrough, playback.GetSnapshot().AudioChannelMode);
    }
}
