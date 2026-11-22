using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using SimpleAudio.Codecs;
using Xunit;

namespace SimpleAudio.Tests;

// 覆盖声道布局决策、混音矩阵、WAVE_FORMAT_EXTENSIBLE 与普通多声道 WAV 的 BASS 实时自适配行为。
[CollectionDefinition("BASS", DisableParallelization = true)]
public class BassCollection;

public class ChannelMixMatrixTests
{
    [Fact]
    public void StereoIdentity_PreservesSamplesAndScale()
    {
        var matrix = ChannelMixMatrix.Build(AudioChannelLayout.Stereo, AudioChannelLayout.Stereo);
        Assert.True(matrix.IsIdentity);

        var output = new float[4];
        matrix.ConvertInt16([32767, -32768, 16384, -16384], output);
        Assert.Equal([32767 / 32768f, -1f, 0.5f, -0.5f], output);
    }

    [Fact]
    public void Surround51ToStereo_PreservesCenterVocalAndOmitsLfe()
    {
        var matrix = ChannelMixMatrix.Build(AudioChannelLayout.Surround51, AudioChannelLayout.Stereo);

        // 仅中置声道有信号（模拟 5.1 曲目人声）
        var centerOnly = new float[] { 0f, 0f, 1.0f, 0f, 0f, 0f };
        var stereo = new float[2];
        matrix.ConvertFloat(centerOnly, stereo);
        Assert.True(stereo[0] > 0.4f);
        Assert.Equal(stereo[0], stereo[1], precision: 5);

        // 仅 LFE 声道有信号，在立体声下混中应被省略
        var lfeOnly = new float[] { 0f, 0f, 0f, 1.0f, 0f, 0f };
        matrix.ConvertFloat(lfeOnly, stereo);
        Assert.Equal(0f, stereo[0]);
        Assert.Equal(0f, stereo[1]);
    }

    [Fact]
    public void Av3a714To71_PreservesBedAndFoldsOnlyHeights()
    {
        var layout714 = AudioChannelLayout.FromAv3aConfig(12, 10);
        var matrix = ChannelMixMatrix.Build(layout714, AudioChannelLayout.Surround71);

        // 中置、LFE、侧环绕在 7.1 拓扑中未受天空声道折叠影响，应保持 1:1 增益
        Assert.Equal(1f, matrix.GetWeight(2, 2), precision: 5); // FC -> FC
        Assert.Equal(1f, matrix.GetWeight(3, 3), precision: 5); // LFE -> LFE
        Assert.Equal(1f, matrix.GetWeight(6, 6), precision: 5); // SL -> SL
        Assert.Equal(1f, matrix.GetWeight(7, 7), precision: 5); // SR -> SR

        // 天空前左(8) -> 前左(0)，天空后右(11) -> 后右(5)
        Assert.True(matrix.GetWeight(0, 8) > 0f);
        Assert.Equal(0f, matrix.GetWeight(1, 8));
        Assert.True(matrix.GetWeight(5, 11) > 0f);
        Assert.Equal(0f, matrix.GetWeight(4, 11));
    }

    [Fact]
    public void Av3a714To51_PreservesCenterAndLfeAndMergesSurrounds()
    {
        var layout714 = AudioChannelLayout.FromAv3aConfig(12, 10);
        var matrix = ChannelMixMatrix.Build(layout714, AudioChannelLayout.Surround51);

        Assert.Equal(1f, matrix.GetWeight(2, 2), precision: 5); // FC
        Assert.Equal(1f, matrix.GetWeight(3, 3), precision: 5); // LFE

        // 侧环绕与后环绕均应归入同侧 5.1 环绕声道（4=BL, 5=BR），不串到对侧
        Assert.True(matrix.GetWeight(4, 4) > 0f);
        Assert.True(matrix.GetWeight(4, 6) > 0f);
        Assert.Equal(0f, matrix.GetWeight(5, 4));
        Assert.Equal(0f, matrix.GetWeight(5, 6));
    }

    [Fact]
    public void Av3a714ToStereo_KeepsLeftRightSeparationAndReasonableLoudness()
    {
        var layout714 = AudioChannelLayout.FromAv3aConfig(12, 10);
        var matrix = ChannelMixMatrix.Build(layout714, AudioChannelLayout.Stereo);

        // 左声道直达权重应显著高于旧的保守全相干缩放（~0.24），维持合理响度
        Assert.True(matrix.GetWeight(0, 0) > 0.5f, $"FL gain too quiet: {matrix.GetWeight(0, 0)}");

        for (var ch = 4; ch < 12; ch++)
        {
            var input = new float[12];
            var output = new float[2];
            input[ch] = 1f;
            matrix.ConvertFloat(input, output);
            Assert.True(output[ch % 2] > 0f);
            Assert.Equal(0f, output[1 - (ch % 2)]);
        }
    }

    [Theory]
    [InlineData(6, 2, AudioChannelMode.Auto, 2)]
    [InlineData(6, 6, AudioChannelMode.Auto, 6)]
    [InlineData(6, 8, AudioChannelMode.Auto, 6)]
    [InlineData(8, 6, AudioChannelMode.Auto, 6)]
    [InlineData(8, 8, AudioChannelMode.Auto, 8)]
    [InlineData(6, 8, AudioChannelMode.ForceStereo, 2)]
    public void ResolveTargetLayout_StandardFormatsAdaptToDeviceSpeakers(
        int sourceCh,
        int deviceSpeakers,
        AudioChannelMode mode,
        int expectedCh)
    {
        var source = AudioChannelLayout.FromStandardChannelCount(sourceCh);
        var target = AudioChannelLayout.ResolveTargetLayout(source, deviceSpeakers, mode);
        Assert.Equal(expectedCh, target.Channels);
    }

    [Fact]
    public void BinauralSpatialRenderer_SideLeftImpulseArrivesEarlierAndLouderInLeftEar()
    {
        var renderer = new BinauralSpatialRenderer(AudioChannelLayout.Surround71, sampleRate: 48000);
        const int frames = 256;
        var input = new float[frames * 8];
        // 在第 10 帧给 SideLeft（索引 6）注入单位脉冲
        input[10 * 8 + 6] = 1.0f;

        var output = new float[frames * 2];
        renderer.RenderFloat(input, output);

        var peakIdxL = 0;
        var peakValL = 0f;
        var peakIdxR = 0;
        var peakValR = 0f;

        for (var f = 0; f < frames; f++)
        {
            var l = MathF.Abs(output[f * 2]);
            var r = MathF.Abs(output[f * 2 + 1]);
            if (l > peakValL) { peakValL = l; peakIdxL = f; }
            if (r > peakValR) { peakValR = r; peakIdxR = f; }
        }

        // 左耳应比右耳更强（ILD），且早到达约 20~35 采样（~0.42~0.73ms ITD）
        Assert.True(peakValL > peakValR * 1.4f, $"Expected strong ILD: L={peakValL:F4}, R={peakValR:F4}");
        var itdSamples = peakIdxR - peakIdxL;
        Assert.InRange(itdSamples, 15, 40);
    }

    [Fact]
    public void DiscreteTdm8Layout_PreservesChannel3WithoutDroppingAsLfe()
    {
        var tdm8 = AudioChannelLayout.CreateDiscretePairs(8);
        Assert.True(tdm8.IsDiscrete);
        Assert.Equal("TDM 8ch", tdm8.Name);

        var matrix = ChannelMixMatrix.Build(tdm8, AudioChannelLayout.Stereo);
        var ch3Only = new float[] { 0f, 0f, 0f, 1.0f, 0f, 0f, 0f, 0f };
        var stereo = new float[2];
        matrix.ConvertFloat(ch3Only, stereo);
        Assert.Equal(0f, stereo[0]);
        Assert.True(stereo[1] > 0.4f, "TDM8 第 4 声道（c3）不应被误当作 LFE 丢弃");
    }
}

[Collection("BASS")]
public class BassChannelAdapterTests
{
    [Fact]
    public void Synthetic51Wav_AdaptsToStereoOn2ChDeviceAndPreservesCenterChannel()
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            const int sampleRate = 48000;
            const int frames = 48000; // 1.0 秒
            var wavBytes = BuildSyntheticWave(sampleRate, AudioChannelLayout.Surround51, frames, (frameIdx, ch) =>
            {
                // 仅在中置声道(2)写入 440Hz 正弦波，测试 2.0 下混后人声不会丢失
                return ch == 2 ? 0.8f * MathF.Sin(2f * MathF.PI * 440f * frameIdx / sampleRate) : 0f;
            });

            using var mem = new MemoryStream(wavBytes, writable: false);
            using var opened = BassStreamOpener.OpenStream(
                mem,
                wavBytes.Length,
                BassFlags.Decode | BassFlags.Float,
                owner: null,
                useUnbuffered: true,
                deviceSpeakers: 2,
                AudioChannelMode.Auto);

            Assert.True(opened.IsSuccess, opened.Error);
            Assert.Equal(6, opened.SourceChannels);
            Assert.Equal(2, opened.OutputChannels);
            Assert.Equal("5.1", opened.SourceLayoutName);
            Assert.Equal("2.0", opened.OutputLayoutName);

            Assert.True(Bass.ChannelGetInfo(opened.Handle, out var info));
            Assert.Equal(2, info.Channels);
            Assert.Equal(sampleRate, info.Frequency);

            var duration = Bass.ChannelBytes2Seconds(opened.Handle, Bass.ChannelGetLength(opened.Handle));
            Assert.InRange(duration, 0.999, 1.001);

            var pcm = new float[2048];
            var read = Bass.ChannelGetData(opened.Handle, pcm, pcm.Length * sizeof(float));
            Assert.Equal(pcm.Length * sizeof(float), read);
            Assert.Contains(pcm, static s => MathF.Abs(s) > 0.3f);

            // 验证中段 Seek 精准可用
            var midPos = Bass.ChannelSeconds2Bytes(opened.Handle, 0.5);
            Assert.True(Bass.ChannelSetPosition(opened.Handle, midPos));
            var midPcm = new float[2048];
            Assert.Equal(midPcm.Length * sizeof(float), Bass.ChannelGetData(opened.Handle, midPcm, midPcm.Length * sizeof(float)));
            Assert.Contains(midPcm, static s => MathF.Abs(s) > 0.3f);
        }
        finally
        {
            Bass.Free();
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void SyntheticMultiChannelWav_PassthroughAndBassFxTempoSupport(int channels)
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            const int sampleRate = 48000;
            const int frames = 24000;
            var layout = AudioChannelLayout.FromStandardChannelCount(channels);
            var wavBytes = BuildSyntheticWave(sampleRate, layout, frames, (frameIdx, ch) =>
                0.2f * MathF.Sin(2f * MathF.PI * (220f + ch * 55f) * frameIdx / sampleRate));

            using var mem = new MemoryStream(wavBytes, writable: false);
            using var opened = BassStreamOpener.OpenStream(
                mem,
                wavBytes.Length,
                BassFlags.Decode | BassFlags.Float,
                owner: null,
                useUnbuffered: true,
                deviceSpeakers: channels,
                AudioChannelMode.Auto);

            Assert.True(opened.IsSuccess, opened.Error);
            Assert.Equal(channels, opened.SourceChannels);
            Assert.Equal(channels, opened.OutputChannels);

            var tempo = BassFx.TempoCreate(opened.Handle, BassFlags.Decode | BassFlags.Float);
            Assert.True(tempo != 0, $"TempoCreate failed for {channels}ch: {Bass.LastError}");
            try
            {
                var buf = new float[1024 * channels];
                Assert.True(Bass.ChannelGetData(tempo, buf, buf.Length * sizeof(float)) > 0);
            }
            finally
            {
                Bass.StreamFree(tempo);
            }
        }
        finally
        {
            Bass.Free();
        }
    }

    [Fact]
    public void LocalMultiChannelWaveSamples_Tdm8And51And71AdaptAndRender3D()
    {
        const string tdm8Path = "/Users/xiong/Music/tdm8.wav";
        const string wav51Path = "/Users/xiong/Music/Nums_5dot1_24_48000.wav";
        const string wav71Path = "/Users/xiong/Music/Nums_7dot1_24_48000.wav";

        if (!File.Exists(tdm8Path) && !File.Exists(wav51Path) && !File.Exists(wav71Path))
            return;

        Assert.True(Bass.Init(0), Bass.LastError.ToString());
        try
        {
            if (File.Exists(tdm8Path))
            {
                using var openedLinear = BassStreamOpener.OpenPathOrUrl(
                    tdm8Path,
                    BassFlags.Decode | BassFlags.Float,
                    deviceSpeakers: 2,
                    AudioChannelMode.Auto);
                Assert.True(openedLinear.IsSuccess, openedLinear.Error);
                Assert.Equal(8, openedLinear.SourceChannels);
                Assert.Equal(2, openedLinear.OutputChannels);
                Assert.Equal("TDM 8ch", openedLinear.SourceLayoutName);
                Assert.Equal("2.0", openedLinear.OutputLayoutName);

                var dur = Bass.ChannelBytes2Seconds(openedLinear.Handle, Bass.ChannelGetLength(openedLinear.Handle));
                Assert.InRange(dur, 60.0, 61.0);

                var buf = new float[4096];
                Assert.True(Bass.ChannelSetPosition(openedLinear.Handle, Bass.ChannelSeconds2Bytes(openedLinear.Handle, 15.0)));
                Assert.Equal(buf.Length * 4, Bass.ChannelGetData(openedLinear.Handle, buf, buf.Length * 4));
                Assert.Contains(buf, static s => MathF.Abs(s) > 0.05f);

                using var opened3d = BassStreamOpener.OpenPathOrUrl(
                    tdm8Path,
                    BassFlags.Decode | BassFlags.Float,
                    deviceSpeakers: 2,
                    AudioChannelMode.HeadphoneSpatial);
                Assert.True(opened3d.IsSuccess, opened3d.Error);
                Assert.Equal("TDM 8ch", opened3d.SourceLayoutName);
                Assert.Equal("3D", opened3d.OutputLayoutName);
                Assert.True(Bass.ChannelSetPosition(opened3d.Handle, Bass.ChannelSeconds2Bytes(opened3d.Handle, 15.0)));
                Assert.Equal(buf.Length * 4, Bass.ChannelGetData(opened3d.Handle, buf, buf.Length * 4));
                Assert.Contains(buf, static s => MathF.Abs(s) > 0.05f);
            }

            foreach (var (path, expectedName) in new[] { (wav51Path, "5.1"), (wav71Path, "7.1") })
            {
                if (!File.Exists(path)) continue;

                using var opened3d = BassStreamOpener.OpenPathOrUrl(
                    path,
                    BassFlags.Decode | BassFlags.Float,
                    deviceSpeakers: 2,
                    AudioChannelMode.HeadphoneSpatial);
                Assert.True(opened3d.IsSuccess, opened3d.Error);
                Assert.Equal(expectedName, opened3d.SourceLayoutName);
                Assert.Equal("3D", opened3d.OutputLayoutName);

                // Nums_5dot1 / Nums_7dot1 在 t=1s(FL), 2s(FR), 3s(FC), 4s(LFE), 5s(BL), 6s(BR) 依次播报各声道
                var oneSec = new float[48000 * 2];
                for (var sec = 1; sec <= 6; sec++)
                {
                    Assert.True(Bass.ChannelSetPosition(opened3d.Handle, Bass.ChannelSeconds2Bytes(opened3d.Handle, sec)));
                    Assert.True(Bass.ChannelGetData(opened3d.Handle, oneSec, oneSec.Length * 4) > 0);
                    Assert.Contains(oneSec, static s => MathF.Abs(s) > 0.02f);
                }
            }
        }
        finally
        {
            Bass.Free();
        }
    }

    private static byte[] BuildSyntheticWave(
        int sampleRate,
        AudioChannelLayout layout,
        int frames,
        Func<int, int, float> sampleGenerator)
    {
        var samples = new float[frames * layout.Channels];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < layout.Channels; c++)
            {
                samples[f * layout.Channels + c] = sampleGenerator(f, c);
            }
        }

        var pcmBytes = MemoryMarshal.AsBytes(samples.AsSpan());
        var header = WaveHeaderBuilder.CreateFloatWaveHeader(sampleRate, layout, pcmBytes.Length);
        var wav = new byte[header.Length + pcmBytes.Length];
        header.CopyTo(wav, 0);
        pcmBytes.CopyTo(wav.AsSpan(header.Length));
        return wav;
    }
}
