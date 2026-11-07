using AvaloniaLyrics;
using KugouAvaloniaPlayer.Controls;

namespace AvaloniaSilkEffects.Tests;

public sealed class FumePlaybackTests
{
    private static FumeArticleBlock Block(bool timed = true) => new()
    {
        SourceLineIndex = 0, Line = new LyricLine { Text = "你好", Start = TimeSpan.FromSeconds(10), Duration = TimeSpan.FromSeconds(2) },
        IsHero = false, X = 0, Y = 0, Width = 40, Height = 30, FontSize = 20,
        TypefaceFamily = "PingFang SC", LineHeight = 30,
        Graphemes = ["你", "好"], GlyphOffsets = [0, 10, 40],
        WordRangeByGlyph = [0, 0], WordRanges = timed ? [new FumeWordRange(0, 2, 10, 12)] : [],
        RenderLines = [new FumeRenderLine { Start = 0, End = 2, Text = "你好", Top = 0, Width = 40 }]
    };

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(20, 2)]
    public void PrintedProgressUsesAbsoluteTime(double seconds, double expected) =>
        Assert.Equal(expected, FumePlayback.ResolvePrintedProgress(Block(), seconds), 6);

    [Fact]
    public void TimedWordWipeUsesMeasuredWidthRatherThanCharacterCount()
    {
        var block = Block();
        Assert.Equal(1, FumePlayback.ResolvePlayedFraction(block, 0, 0, 1, 11), 6);
        Assert.Equal(1d/3, FumePlayback.ResolvePlayedFraction(block, 1, 0, 1, 11), 6);
        FumePlayback.ResolveGlyphTiming(block, 1, 0, out var start, out var end);
        Assert.Equal(10.5, start, 6);
        Assert.Equal(12, end, 6);
    }

    [Fact]
    public void SeekAndRepeatedPausedSamplesDoNotAccumulateState()
    {
        var block = Block(false);
        var first = FumePlayback.ResolvePrintedProgress(block, 10.5);
        _ = FumePlayback.ResolvePrintedProgress(block, 30);
        for (var i=0; i<100; i++)
            Assert.Equal(first, FumePlayback.ResolvePrintedProgress(block, 10.5));
        Assert.Equal(0.5, FumePlayback.ResolvePlayedFraction(block, 0, -1, first, 10.5), 6);
    }
}
