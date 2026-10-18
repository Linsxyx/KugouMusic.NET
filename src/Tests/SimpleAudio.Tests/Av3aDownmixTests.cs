using SimpleAudio.Codecs.Av3a;
using Xunit;

namespace SimpleAudio.Tests;

public class Av3aDownmixTests
{
    [Fact]
    public void Stereo_PreservesSamplesAndFloatScale()
    {
        var output = new float[4];
        new Av3aStereoDownmix(2, 1).Convert([32767, -32768, 16384, -16384], output);
        Assert.Equal([32767 / 32768f, -1f, 0.5f, -0.5f], output);
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(6, 2)]
    [InlineData(8, 3)]
    [InlineData(8, 7)]
    [InlineData(10, 8)]
    [InlineData(10, 9)]
    [InlineData(12, 10)]
    public void CorrelatedFullScaleChannels_DoNotClip(int channels, uint config)
    {
        var output = new float[2];
        new Av3aStereoDownmix(channels, config).Convert(Enumerable.Repeat(short.MinValue, channels).ToArray(), output);
        Assert.All(output, v => Assert.InRange(v, -1.000001f, 1f));
    }

    [Fact]
    public void SurroundAndHeightPairs_ReachOnlyTheirOwnSide()
    {
        var downmix = new Av3aStereoDownmix(12, 10);
        for (var channel = 4; channel < 12; channel++)
        {
            var input = new short[12];
            var output = new float[2];
            input[channel] = 16384;
            downmix.Convert(input, output);
            Assert.True(output[channel % 2] > 0);
            Assert.Equal(0, output[1 - channel % 2]);
        }
    }

    [Fact]
    public void Mono_IsDuplicatedAndLfeIsOmitted()
    {
        var output = new float[2];
        new Av3aStereoDownmix(1, 0).Convert([16384], output);
        Assert.Equal([0.5f, 0.5f], output);
        var surround = new short[12];
        surround[3] = short.MaxValue;
        new Av3aStereoDownmix(12, 10).Convert(surround, output);
        Assert.Equal([0f, 0f], output);
    }
}
