using AvaloniaLyrics;
using KugouAvaloniaPlayer.Controls;

namespace AvaloniaSilkEffects.Tests;

public sealed class FumeLayoutTests
{
    [Theory]
    [InlineData(40, 1280, 720, "Arial", 1, 1)]
    [InlineData(100, 800, 600, "Arial", 1, 1)]
    [InlineData(200, 1920, 1080, "Arial", 1, 1)]
    [InlineData(60, 390, 844, "PingFang SC", 0.8, 0.82)]
    [InlineData(80, 1440, 900, "Times New Roman", 1.4, 1.32)]
    [InlineData(1, 800, 600, "Arial", 1, 1)]
    public void LayoutPreservesTextAndGeometry(int count, int width, int height, string font, double scale, double hero)
    {
        string[] texts = ["夜空中最亮的星，能否听清", "Hello world, AVATAR office flowing", "我们一起走过春夏秋冬", "A very long lyric line that needs to fit within the article columns and preserve all its words", "风", "Cafe\u0301 与音乐 🎵"];
        var lines = Enumerable.Range(0, count).Select(i => new LyricLine
        {
            Text = texts[i % texts.Length], Start = TimeSpan.FromSeconds(i * 3), Duration = TimeSpan.FromSeconds(3)
        }).ToArray();
        var article = FumeArticleLayoutEngine.Build(lines, width, height, font, scale, hero)!;
        var repeated = FumeArticleLayoutEngine.Build(lines, width, height, font, scale, hero)!;
        Assert.Equal(count, article.Blocks.Count);
        foreach (var block in article.Blocks)
        {
            var oldBlock = repeated.BlocksBySourceIndex[block.SourceLineIndex];
            Assert.Equal(oldBlock.X, block.X, 6);
            Assert.Equal(oldBlock.Y, block.Y, 6);
            Assert.Equal(oldBlock.FontSize, block.FontSize, 6);
            Assert.Equal(oldBlock.RenderLines.Select(r => r.Text), block.RenderLines.Select(r => r.Text));
            Assert.Equal(block.Line.Text, string.Concat(block.RenderLines.Select(l => l.Text)));
            Assert.True(double.IsFinite(block.X) && double.IsFinite(block.Y));
            Assert.Equal(block.Graphemes.Count + 1, block.GlyphOffsets.Count);
            Assert.Equal(block.Graphemes.Count, block.WordRangeByGlyph.Count);
            Assert.True(block.Height > 0);
            foreach (var row in block.RenderLines)
                Assert.True(row.Width <= block.Width + 0.001 || row.End - row.Start == 1);
        }
    }

    [Fact]
    public void EmptyLyricsAndUnarrangedViewportDoNotBuildAnArticle()
    {
        Assert.Null(FumeArticleLayoutEngine.Build([], 800, 600, "Arial", 1, 1));
        Assert.Null(FumeArticleLayoutEngine.Build([new LyricLine { Text = "  " }], 800, 600, "Arial", 1, 1));
        Assert.Null(FumeArticleLayoutEngine.Build([new LyricLine { Text = "歌词" }], 0, 600, "Arial", 1, 1));
    }

    [Fact]
    public void SharedTextMeasurementsPreserveEachLinesTimingAndSourceIndex()
    {
        LyricLine Line(int seconds) => new()
        {
            Text = "你好 世界", Start = TimeSpan.FromSeconds(seconds), Duration = TimeSpan.FromSeconds(4),
            Words =
            [
                new LyricWord { Text = "你好", Start = TimeSpan.FromSeconds(seconds), Duration = TimeSpan.FromSeconds(2) },
                new LyricWord { Text = "世界", Start = TimeSpan.FromSeconds(seconds + 2), Duration = TimeSpan.FromSeconds(2) }
            ]
        };
        var lines = new[] { Line(10), new LyricLine { Text = " " }, Line(30) };
        var article = FumeArticleLayoutEngine.Build(lines, 800, 600, "Arial", 1, 1)!;
        Assert.Equal([0, 2], article.ChronologicalBlocks.Select(b => b.SourceLineIndex));
        foreach (var block in article.Blocks)
        {
            Assert.Same(lines[block.SourceLineIndex], block.Line);
            Assert.Equal([0, 0, 0, 1, 1], block.WordRangeByGlyph);
            Assert.Equal(block.Line.Start.TotalSeconds, block.WordRanges[0].StartSeconds);
            Assert.Equal(block.Line.Start.TotalSeconds + 4, block.WordRanges[1].EndSeconds);
        }
    }

}
