using System.Numerics;
using AvaloniaSilkEffects.Sonnet;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetDecorationTests
{
    internal static SonnetTypographyPlacement Placement(float rotation = 0) =>
        new(0, "世界", SonnetSegmentRole.Hero, 1, 240, 80, 0, 0, rotation, -120, 40, false, 0.2f);

    [Fact]
    public void VerticalFramesRestoreLocalDimensions()
    {
        Assert.Equal(new Vector2(80, 240), SonnetFrameDecorView.LocalDimensions(Placement(MathF.PI / 2)));
        Assert.Equal(new Vector2(80, 240), SonnetFrameDecorView.LocalDimensions(Placement(-MathF.PI / 2)));
        Assert.Equal(new Vector2(240, 80), SonnetFrameDecorView.LocalDimensions(Placement(0.2f)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FramesGrowAfterFirstGlyphAndSeekBackDeterministically(int variant)
    {
        var theme = new SonnetTheme(EffectColor.Transparent, EffectColor.White, EffectColor.White, EffectColor.White);
        var frame = new SonnetFrameDecorView(Placement(), 64, theme, variant, 2, 0, 8);
        frame.Update(1.9);
        Assert.Equal(0, frame.Root.Alpha);
        var nodes = frame.Root.Children.ToArray();
        var time = frame.StartTime + (frame.EndTime - frame.StartTime) * 0.15;
        frame.Update(time);
        var sizes = nodes.OfType<ShapeNode>().Select(n => n.Size).ToArray();
        frame.Update(10);
        Assert.Contains(nodes.OfType<ShapeNode>(), n => n.Size.Length() > 50 || variant == 3);
        frame.Update(time);
        Assert.Equal(sizes, nodes.OfType<ShapeNode>().Select(n => n.Size).ToArray());
        Assert.Equal(nodes, frame.Root.Children);
    }

    [Fact]
    public void GuideHeadInterpolatesBetweenSamples()
    {
        var line = new PolylineNode { Points = [new(0, 0), new(100, 0), new(100, 100)] };
        Assert.Equal(new Vector2(25, 0), SonnetGuideView.Point(line, 0.125f));
        Assert.Equal(new Vector2(100, 50), SonnetGuideView.Point(line, 0.75f));
    }

    [Fact]
    public void StrokesTraceByLengthAndRestoreOnSeek()
    {
        var line = new PolylineNode { Points = [new(0, 0), new(10, 0), new(10, 100)] };
        var reveal = new SonnetStrokeReveal(new EffectContainer().Add(line));
        reveal.Update(0);
        Assert.False(line.IsVisible);
        reveal.Update(0.16);
        var tip = line.EndPositionOverride;
        Assert.Equal(2, line.EndPointIndex);
        Assert.Equal(new Vector2(10, 86.25f), tip);
        reveal.Update(1);
        Assert.Equal(new Vector2(10, 100), line.EndPositionOverride);
        reveal.Update(0.16);
        Assert.Equal(tip, line.EndPositionOverride);
    }
}
