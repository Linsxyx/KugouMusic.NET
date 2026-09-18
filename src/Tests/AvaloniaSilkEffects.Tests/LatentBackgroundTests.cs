using AvaloniaSilkEffects.Backgrounds;
using SkiaSharp;

namespace AvaloniaSilkEffects.Tests;

public sealed class LatentBackgroundTests
{
    [Fact]
    public void CoverColorsKeepMeshOrderAndThemeFallback()
    {
        var theme = LatentPalette.Midnight;
        Assert.Equal(new[] { theme.Secondary,theme.Primary,theme.Secondary,theme.Primary,theme.Background,theme.Accent },
            theme.MeshColors());
        var a = new EffectColor(1,0,0);
        Assert.Equal(new[] { a,theme.Primary,a,theme.Primary,theme.Background,theme.Accent },
            (theme with { Cover = new[]{a} }).MeshColors());
    }
    [Fact]
    public void CoverExtractionUsesStraightRgbaAndSkipsTransparentPixels()
    {
        using var bitmap = new SKBitmap(50,50);
        bitmap.Erase(new SKColor(220,40,20,200));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png,100);
        var colors = LatentCoverPalette.ExtractEncoded(data.ToArray());
        Assert.Single(colors);
        Assert.InRange(colors[0].R,.85f,.87f);
        bitmap.Erase(SKColors.Transparent);
        using var transparent = bitmap.Encode(SKEncodedImageFormat.Png,100);
        Assert.Empty(LatentCoverPalette.ExtractEncoded(transparent.ToArray()));
    }

    [Fact]
    public void CoverOnlyUsesAllSixCoverColorsAndCoverColoredDithering()
    {
        EffectColor[] cover = [new(.1f,0,0),new(.2f,0,0),new(.3f,0,0),
            new(.4f,0,0),new(.5f,0,0),new(.6f,0,0)];
        var palette = LatentPalette.Midnight with { Cover = cover, UseCoverColorsOnly = true };
        Assert.Equal(cover, palette.MeshColors());
        Assert.Equal(cover[2], palette.DitheringBackground);
        Assert.Equal(LatentPalette.Midnight.Background, palette.Background);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SparseCoverOnlyPaletteRepeatsCoverColorsInsteadOfAddingThemeAccent(int count)
    {
        EffectColor[] cover = [new(0,0,1),new(0,.3f,.8f),new(.1f,.2f,.7f)];
        var palette = LatentPalette.Midnight with
        {
            Cover = cover.Take(count).ToArray(), UseCoverColorsOnly = true,
            Accent = new(214/255f,169/255f,31/255f)
        };
        var a = count > 0 ? cover[0] : palette.Secondary;
        var b = count > 1 ? cover[1] : a;
        var c = count > 2 ? cover[2] : b;
        Assert.Equal(new[] { a,b,c,a,b,c }, palette.MeshColors());
        Assert.Equal(c, palette.DitheringBackground);
        Assert.DoesNotContain(palette.Accent, palette.MeshColors());
    }
    [Fact]
    public void PausedBackgroundRunsAtOriginalReducedSpeed()
    {
        var state = new LatentModulation();
        state.Step(new LatentAudio(),.1);
        Assert.Equal(.0036,state.MeshTime,8);
        Assert.Equal(.0012,state.DitherTime,8);
        Assert.Equal(0,state.Bass);
    }
    [Fact]
    public void BeatResponseIsDeterministicAndBounded()
    {
        var a = new LatentModulation();
        var b = new LatentModulation();
        var audio = new LatentAudio(1,1,1,1,1,1,false);
        for (var i=0;i<120;i++) { a.Step(audio,1d/60); b.Step(audio,1d/60); }
        Assert.Equal(a.MeshTime,b.MeshTime);
        Assert.InRange(a.Beat,0,1);
        Assert.InRange(a.MeshTime,.6,4);
    }
}
