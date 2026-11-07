using AvaloniaSilkEffects.Sonnet;
using Xunit;

namespace AvaloniaSilkEffects.Tests;

public class SonnetCoverPaletteTests
{
    [Fact]
    public void RepresentativePaletteKeepsPopulationOrderAndIgnoresTransparentPixels()
    {
        byte[] pixels = [20, 30, 40, 255, 20, 30, 40, 255, 20, 30, 40, 255,
            240, 10, 20, 255, 0, 255, 0, 127];
        var colors = SonnetCoverPalette.Extract(pixels);
        Assert.Equal(2, colors.Count);
        Assert.Equal(new EffectColor(20 / 255f, 30 / 255f, 40 / 255f), colors[0]);
        Assert.Equal(new EffectColor(240 / 255f, 10 / 255f, 20 / 255f), colors[1]);
    }

    [Fact]
    public void QuantizedBucketUsesJavaScriptHalfUpRounding()
    {
        var colors = SonnetCoverPalette.Extract([16, 16, 16, 128, 17, 17, 17, 255]);
        Assert.Single(colors);
        Assert.Equal(new EffectColor(17 / 255f, 17 / 255f, 17 / 255f), colors[0]);
        Assert.Empty(SonnetCoverPalette.Extract([255, 0, 0, 0]));
    }
}
