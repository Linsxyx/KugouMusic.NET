namespace AvaloniaSilkEffects.Tests;

public sealed class FrameAndColorTests
{
    [Fact]
    public void Delta_IsClampedAtBothEdges()
    {
        Assert.Equal(TimeSpan.Zero, EffectFrameClock.ClampDelta(TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromMilliseconds(100), EffectFrameClock.ClampDelta(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromMilliseconds(16), EffectFrameClock.ClampDelta(TimeSpan.FromMilliseconds(16)));
    }

    [Fact]
    public void PremultipliedColor_MultipliesRgbButKeepsAlpha()
    {
        var result = new EffectColor(0.8f, 0.4f, 0.2f, 0.5f).Premultiplied();

        Assert.Equal(new EffectColor(0.4f, 0.2f, 0.1f, 0.5f), result);
    }

    [Fact]
    public void TextTextureKey_UsesEveryRasterInput()
    {
        var first = new TextTextureKey("歌词", "Inter", 48, 600, EffectColor.White, 2);
        var same = new TextTextureKey("歌词", "Inter", 48, 600, EffectColor.White, 2);
        var differentScale = first with { RasterScale = 3 };

        Assert.Equal(first, same);
        Assert.NotEqual(first, differentScale);
    }

    [Fact]
    public void FramePacer_DefaultLetsCompositorOwnEveryCallback()
    {
        var pacer = new EffectFramePacer();

        Assert.True(pacer.ShouldPresent(TimeSpan.Zero, 0));
        Assert.True(pacer.ShouldPresent(TimeSpan.FromMilliseconds(8.2), 0));
        Assert.True(pacer.ShouldPresent(TimeSpan.FromMilliseconds(16.4), 0));
    }

    [Fact]
    public void FramePacer_CappedCadenceDoesNotAccumulateDrift()
    {
        var pacer = new EffectFramePacer();

        Assert.True(pacer.ShouldPresent(TimeSpan.Zero, 60));
        Assert.False(pacer.ShouldPresent(TimeSpan.FromMilliseconds(16.4), 60));
        Assert.True(pacer.ShouldPresent(TimeSpan.FromMilliseconds(16.8), 60));
        Assert.True(pacer.ShouldPresent(TimeSpan.FromMilliseconds(33.4), 60));
        Assert.False(pacer.ShouldPresent(TimeSpan.FromMilliseconds(41.7), 60));
        Assert.True(pacer.ShouldPresent(TimeSpan.FromMilliseconds(50.1), 60));
    }
}
