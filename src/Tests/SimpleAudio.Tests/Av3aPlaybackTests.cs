using System.Diagnostics;
using ManagedBass;
using ManagedBass.Fx;
using SimpleAudio.Codecs;
using SimpleAudio.Codecs.Av3a;
using Xunit;
using Xunit.Abstractions;

namespace SimpleAudio.Tests;

// AV3A 真实样本集成测试：验证按需解码零落盘、Preroll Seek 收敛精度、7.1.4 多声道自适配与分析管线兼容性。
internal sealed class Av3aSampleFactAttribute : FactAttribute
{
    public const string DefaultLocalSample = "/Users/xiong/Music/林俊杰 - 黑夜问白天.m4a";

    public Av3aSampleFactAttribute()
    {
        if (ResolveSamplePath() == null)
        {
            Skip = "Set SIMPLEAUDIO_AV3A_SAMPLE to a local AV3A M4A file to run native integration checks.";
        }
    }

    public static string? ResolveSamplePath()
    {
        var env = Environment.GetEnvironmentVariable("SIMPLEAUDIO_AV3A_SAMPLE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        return File.Exists(DefaultLocalSample) ? DefaultLocalSample : null;
    }
}

[Collection("BASS")]
public class Av3aPlaybackTests(ITestOutputHelper output)
{
    private static string SamplePath => Av3aSampleFactAttribute.ResolveSamplePath()!;

    [Av3aSampleFact]
    public void PrerollSeek_ConvergesToSequentialReferenceDecode()
    {
        using var input = File.OpenRead(SamplePath);
        var track = Av3aMp4Track.TryRead(input);
        Assert.NotNull(track);

        // 先顺序解码前 520 帧，记录第 500 帧（约 11.6 秒处）的精确参考波形。
        const int targetFrame = 500;
        var packet = new byte[track.Samples.Max(static s => s.Size)];
        var firstSize = track.ReadPacket(input, 0, packet);
        using var refDecoder = new Av3aNativeDecoder(packet.AsSpan(0, firstSize));
        short[] referencePcm = [];

        for (var i = 0; i <= targetFrame; i++)
        {
            var size = track.ReadPacket(input, i, packet);
            var decoded = refDecoder.Decode(packet.AsSpan(0, size));
            if (i == targetFrame)
            {
                referencePcm = decoded.ToArray();
            }
        }

        // 测试不同 Preroll 帧数下，重置解码器并 Seek 到 targetFrame 的误差收敛情况。
        foreach (var preroll in new[] { 0, 1, 2, 4, 8 })
        {
            var start = Math.Max(0, targetFrame - preroll);
            var seedSize = track.ReadPacket(input, start, packet);
            using var seekDecoder = new Av3aNativeDecoder(packet.AsSpan(0, seedSize));
            short[] seekPcm = [];
            for (var i = start; i <= targetFrame; i++)
            {
                var size = track.ReadPacket(input, i, packet);
                var decoded = seekDecoder.Decode(packet.AsSpan(0, size));
                if (i == targetFrame)
                {
                    seekPcm = decoded.ToArray();
                }
            }

            double sumSqErr = 0;
            double sumSqRef = 0;
            var maxAbsDiff = 0;
            for (var s = 0; s < referencePcm.Length; s++)
            {
                var diff = Math.Abs(referencePcm[s] - seekPcm[s]);
                if (diff > maxAbsDiff) maxAbsDiff = diff;
                sumSqErr += (double)diff * diff;
                sumSqRef += (double)referencePcm[s] * referencePcm[s];
            }

            var snrDb = sumSqErr == 0 ? 120.0 : 10.0 * Math.Log10(sumSqRef / sumSqErr);
            output.WriteLine($"Preroll={preroll}: maxAbsDiff={maxAbsDiff}, SNR={snrDb:F2} dB");

            if (preroll == Av3aPcmWaveStream.DefaultPrerollFrames)
            {
                Assert.True(snrDb > 35.0, $"Expected high SNR at DefaultPrerollFrames={preroll}, got {snrDb:F2} dB");
            }
        }
    }

    [Av3aSampleFact]
    public void M4a_ZeroTempFileFastStartupSeekAndMultiChannelAdaptation()
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            var tempBefore = Directory.GetFiles(Path.GetTempPath(), "simpleaudio-av3a-*.pcm");

            // 1. 2.0 耳机设备：7.1.4 自动下混为 2.0，且起播与尾段 Seek 均在毫秒级完成
            var sw = Stopwatch.StartNew();
            using (var opened2 = BassStreamOpener.OpenPathOrUrl(
                       SamplePath,
                       BassFlags.Decode | BassFlags.Float,
                       deviceSpeakers: 2,
                       AudioChannelMode.Auto))
            {
                var startupMs = sw.Elapsed.TotalMilliseconds;
                Assert.True(opened2.IsSuccess, opened2.Error);
                Assert.Equal(12, opened2.SourceChannels);
                Assert.Equal(2, opened2.OutputChannels);
                Assert.Equal("7.1.4", opened2.SourceLayoutName);
                Assert.Equal("2.0", opened2.OutputLayoutName);
                Assert.True(startupMs < 500, $"On-demand AV3A startup took {startupMs:F1}ms");

                var duration = Bass.ChannelBytes2Seconds(opened2.Handle, Bass.ChannelGetLength(opened2.Handle));
                Assert.InRange(duration, 290.0, 295.0);

                var head = new float[4096];
                Assert.Equal(head.Length * 4, Bass.ChannelGetData(opened2.Handle, head, head.Length * 4));
                Assert.Contains(head, static s => MathF.Abs(s) > 0.0001f);

                // 跳到末尾前 5 秒，按需解码应瞬间完成（无需等待解完整首歌）
                var seekSw = Stopwatch.StartNew();
                Assert.True(Bass.ChannelSetPosition(opened2.Handle, Bass.ChannelSeconds2Bytes(opened2.Handle, duration - 5)));
                var tail = new float[4096];
                Assert.Equal(tail.Length * 4, Bass.ChannelGetData(opened2.Handle, tail, tail.Length * 4));
                var seekMs = seekSw.Elapsed.TotalMilliseconds;
                output.WriteLine($"AV3A 7.1.4->2.0 startup={startupMs:F1}ms, near-end seek={seekMs:F1}ms, duration={duration:F2}s");
                Assert.True(seekMs < 500, $"Near-end seek took {seekMs:F1}ms");

                // 经过 BassFx.TempoCreate 变速管线验证
                Assert.True(Bass.ChannelSetPosition(opened2.Handle, 0));
                var tempo = BassFx.TempoCreate(opened2.Handle, BassFlags.Decode | BassFlags.Float);
                Assert.True(tempo != 0, Bass.LastError.ToString());
                try
                {
                    Assert.True(Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, 15));
                    Assert.True(Bass.ChannelGetData(tempo, head, head.Length * 4) > 0);
                }
                finally
                {
                    Bass.StreamFree(tempo);
                }
            }

            // 确认整个过程零临时 PCM 文件落盘
            var tempAfter = Directory.GetFiles(Path.GetTempPath(), "simpleaudio-av3a-*.pcm");
            Assert.Equal(tempBefore.Length, tempAfter.Length);

            // 2. 5.1 / 7.1 硬件设备自适配验证（含 BassFx.TempoCreate）
            foreach (var (speakers, expectedCh, expectedName) in new[] { (6, 6, "5.1"), (8, 8, "7.1") })
            {
                using var opened = BassStreamOpener.OpenPathOrUrl(
                    SamplePath,
                    BassFlags.Decode | BassFlags.Float,
                    deviceSpeakers: speakers,
                    AudioChannelMode.Auto);

                Assert.True(opened.IsSuccess, opened.Error);
                Assert.Equal(12, opened.SourceChannels);
                Assert.Equal(expectedCh, opened.OutputChannels);
                Assert.Equal(expectedName, opened.OutputLayoutName);

                Assert.True(Bass.ChannelGetInfo(opened.Handle, out var info));
                Assert.Equal(expectedCh, info.Channels);

                // 跳过歌曲开头前几十毫秒静音，定位到 15 秒处验证多声道实际音频信号与 BassFx.TempoCreate。
                Assert.True(Bass.ChannelSetPosition(opened.Handle, Bass.ChannelSeconds2Bytes(opened.Handle, 15.0)));
                var pcm = new float[2048 * expectedCh];
                Assert.Equal(pcm.Length * 4, Bass.ChannelGetData(opened.Handle, pcm, pcm.Length * 4));
                Assert.Contains(pcm, static s => MathF.Abs(s) > 0.001f);

                Assert.True(Bass.ChannelSetPosition(opened.Handle, 0));
                var tempo = BassFx.TempoCreate(opened.Handle, BassFlags.Decode | BassFlags.Float);
                Assert.True(tempo != 0, $"TempoCreate failed for AV3A {expectedName}: {Bass.LastError}");
                Bass.StreamFree(tempo);
            }
        }
        finally
        {
            Bass.Free();
        }
    }

    [Av3aSampleFact]
    public async Task AnalysisPipelines_LoudnessAndTransitionSupportAv3a()
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            var transitionService = new ManagedBassTransitionAnalysisService();
            var prepared = new PreparedTrack
            {
                Id = "av3a-test",
                Source = SamplePath,
                IsLocal = true,
                DurationSeconds = 292
            };
            var sw = Stopwatch.StartNew();
            var profile = await transitionService.AnalyzeAsync(prepared, prepared);
            output.WriteLine($"Transition analysis finished in {sw.Elapsed.TotalMilliseconds:F1}ms: entry={profile.MixEntrySec:F2}s, overlap={profile.OverlapSec:F2}s, confidence={profile.Confidence:F2}");

            Assert.True(profile.OverlapSec > 0);
            Assert.True(profile.Confidence > 0.3);

            var gain = await TrackVolumeNormalizer.EstimateGainAsync(SamplePath, isLocal: true, durationSeconds: 292);
            output.WriteLine($"Loudness normalization gain={gain:F4}");
            Assert.InRange(gain, 0.1f, 3.0f);
        }
        finally
        {
            Bass.Free();
        }
    }

    [Av3aSampleFact]
    public void AbortableStream_DisposesOwnerOnceAndStopsCleanly()
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            var owner = new CountingOwner();
            var input = new AbortTrackingMemoryStream(File.ReadAllBytes(SamplePath));

            using (var opened = BassStreamOpener.OpenStream(
                       input,
                       input.Length,
                       BassFlags.Decode | BassFlags.Float,
                       owner,
                       useUnbuffered: true,
                       deviceSpeakers: 2,
                       AudioChannelMode.Auto))
            {
                Assert.True(opened.IsSuccess, opened.Error);
                var pcm = new float[2048];
                Assert.True(Bass.ChannelGetData(opened.Handle, pcm, pcm.Length * 4) > 0);
            }

            Assert.True(input.Aborted);
            Assert.False(input.CanRead);
            Assert.Equal(1, owner.Disposals);
        }
        finally
        {
            Bass.Free();
        }
    }

    [Av3aSampleFact]
    public void CorruptedCrc_IsRejectedBeforePlayback()
    {
        using var input = File.OpenRead(SamplePath);
        var track = Av3aMp4Track.TryRead(input)!;
        var packet = new byte[track.Samples[0].Size];
        track.ReadPacket(input, 0, packet);
        using var decoder = new Av3aNativeDecoder(packet);
        packet[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => decoder.Decode(packet));
    }

    private sealed class CountingOwner : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class AbortTrackingMemoryStream(byte[] data) : MemoryStream(data), IAbortableStreamSource
    {
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
