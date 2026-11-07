using AvaloniaSilkEffects.Sonnet;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetSemanticAllocationTests
{
    // Lexical boundaries remain available after function words are grouped for display.
    // Display segments intentionally differ from the former Intl.Segmenter output.
    [Theory]
    [InlineData("沿着光的轨迹", "沿着|光的|轨迹", "沿着|光|的|轨迹")]
    [InlineData("世界， 再见！", "世界，| |再见！", "世界|再见")]
    [InlineData("我们一起走过漫长的夜晚", "我们|一起|走过|漫长的|夜晚", "我们|一起|走过|漫长|的|夜晚")]
    [InlineData("明かりにあなたへ", "明かりに|あなたへ", "明かり|に|あなた|へ")]
    [InlineData("don’t stop 3.14!", "don’t| |stop| |3.14!", "don’t|stop|3.14")]
    [InlineData("星光👨‍👩‍👧‍👦闪耀", "星光|👨‍👩‍👧‍👦|闪耀", "星光|闪耀")]
    public void DisplayGroupingPreservesLexicalBoundariesAndTiming(string text, string expected, string lexical)
    {
        var segments = SonnetProgramCompiler.BuildSemanticSegments(
            new SonnetLine(text, 1, 7, [new SonnetWordTiming(text, 1, 7)]));
        Assert.Equal(expected.Split('|'), segments.Select(segment => segment.Text));
        Assert.Equal(lexical.Split('|'), segments.SelectMany(segment => segment.LexicalWords).Select(word => word.Text));
        Assert.All(segments.SelectMany(segment => segment.LexicalWords), word =>
            Assert.Equal(text[word.StartOffset..word.EndOffset], word.Text));
        Assert.Equal(text, string.Concat(segments.Select(segment => segment.Text)));
        Assert.Equal(1, segments[0].StartTime);
        Assert.Equal(7, segments[^1].EndTime);
        Assert.All(segments, segment =>
        {
            Assert.Equal(text[segment.StartOffset..segment.EndOffset], segment.Text);
            Assert.Equal([0], segment.WordIndices);
        });
        for (var i = 1; i < segments.Count; i++)
            Assert.Equal(segments[i - 1].EndTime, segments[i].StartTime);
    }

    [Theory]
    [InlineData("沿着光的轨迹", "光的", "光", "的")]
    [InlineData("我们一起走过漫长的夜晚", "漫长的", "漫长", "的")]
    [InlineData("明かりにあなたへ", "明かりに", "明かり", "に")]
    [InlineData("明かりにあなたへ", "あなたへ", "あなた", "へ")]
    public void AttachedFunctionWordsDoNotBecomeTheEmphasisCore(string text, string display, string core, string function)
    {
        var segments = SonnetProgramCompiler.BuildSemanticSegments(new SonnetLine(text, 1, 7, [new(text, 1, 7)]));
        var segment = Assert.Single(segments, item => item.Text == display);

        Assert.Equal(core, text[segment.CoreStartOffset!.Value..segment.CoreEndOffset!.Value]);
        Assert.Equal(SonnetLexicalKind.Function, Assert.Single(segment.LexicalWords, word => word.Text == function).Kind);
        Assert.True(segment.IsWordLike);
    }

    [Theory]
    [InlineData(0.299, "光的|轨迹")]
    [InlineData(0.300, "光|的|轨迹")]
    [InlineData(0.301, "光|的|轨迹")]
    public void ReliablePausePreventsDisplayGroupingWithoutChangingLexicalWords(double pause, string expected)
    {
        const string text = "光的轨迹";
        var segments = SonnetProgramCompiler.BuildSemanticSegments(new SonnetLine(text, 1, 4,
            [new("光", 1, 2), new("的", 2 + pause, 3), new("轨迹", 3, 4)]));

        Assert.Equal(expected.Split('|'), segments.Select(segment => segment.Text));
        Assert.Equal(new[] { "光", "的", "轨迹" }, segments.SelectMany(segment => segment.LexicalWords).Select(word => word.Text));
        var graphemes = segments.SelectMany(segment => segment.Graphemes).ToArray();
        Assert.Equal(2, graphemes[0].EndTime);
        Assert.Equal(2 + pause, graphemes[1].StartTime);
        Assert.True(graphemes[0].HasReliableEnd);
        Assert.True(graphemes[1].HasReliableStart);
        Assert.Equal(new[] { 0, 1, 2 }, segments.SelectMany(segment => segment.WordIndices).Distinct());
    }

    [Theory]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇨🇳")]
    [InlineData("👍🏽")]
    [InlineData("1️⃣")]
    public void EmojiRemainsOneIndependentGraphemeWithItsOriginalTiming(string emoji)
    {
        var text = $"世界{emoji}再见";
        var segments = SonnetProgramCompiler.BuildSemanticSegments(new SonnetLine(text, 1, 4,
            [new("世界", 1, 2), new(emoji, 2, 3), new("再见", 3, 4)]));

        Assert.Equal(new[] { "世界", emoji, "再见" }, segments.Select(segment => segment.Text));
        var symbol = segments[1];
        Assert.Equal(emoji, Assert.Single(symbol.Graphemes).Text);
        Assert.Equal(emoji, text[symbol.StartOffset..symbol.EndOffset]);
        Assert.Equal(2, symbol.StartTime);
        Assert.Equal(3, symbol.EndTime);
        Assert.Equal([1], symbol.WordIndices);
        Assert.False(symbol.IsWordLike);
        Assert.Empty(symbol.LexicalWords);
        Assert.Null(symbol.CoreStartOffset);
        Assert.Null(symbol.CoreEndOffset);
    }

    [Fact]
    public void ChineseSentenceDoesNotForceQuietShotAndReachesExtendedGeometry()
    {
        var reached = new HashSet<SonnetShotKind>();
        var geometryVariants = new HashSet<uint>();
        for (var seed = 0; seed < 256; seed++)
        {
            var program = SonnetProgramCompiler.Compile([
                new SonnetLine("沿着光的轨迹", 0, 5, [new("沿着光的轨迹", 0, 5)]),
                new SonnetLine("再见", 10, 12, [new("再见", 10, 12)])
            ], seed.ToString());
            var paragraph = program.Paragraphs[0];
            Assert.Equal(SonnetParagraphKind.Verse, paragraph.Kind);
            var shot = Assert.Single(paragraph.Shots);
            reached.Add(shot.Kind);
            if (shot.Kind is SonnetShotKind.TypeImpact or SonnetShotKind.FragmentCollage)
                geometryVariants.Add(SonnetRandom.Hash($"{program.Seed}:{paragraph.Id}") % 100);
        }
        Assert.Equal(7, reached.Count);
        Assert.Contains(geometryVariants, variant => variant >= 48);
    }
}
