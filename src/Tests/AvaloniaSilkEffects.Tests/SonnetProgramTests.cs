using AvaloniaSilkEffects.Sonnet;
using System.Numerics;
using System.Text.Json;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetProgramTests
{
    private static SonnetLine Line(string text, double start, double end, int? block = null, bool chorus = false) =>
        new(text, start, end, [new(text, start, end)], BlockIndex: block, IsChorus: chorus);

    [Fact]
    public void Compiler_IsDeterministicAndRegistersSevenShotKinds()
    {
        var lines = Enumerable.Range(0, 8).Select(index => Line($"lyric {index}!", index * 2, index * 2 + 1.2, chorus: index == 3)).ToArray();

        var first = SonnetProgramCompiler.Compile(lines, "song-a");
        var second = SonnetProgramCompiler.Compile(lines, "song-a");

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(7, SonnetProgramCompiler.ShotKinds.Distinct().Count());
        Assert.Contains(first.Paragraphs, paragraph => paragraph.Kind == SonnetParagraphKind.Chorus);
        Assert.Equal(first.Paragraphs.Count - 1,
            SonnetProgramCompiler.FindParagraphIndexAtTime(first, first.Paragraphs[^1].StartTime));
    }

    [Fact]
    public void SemanticCompiler_PreservesCjkWhitespaceAndPunctuation()
    {
        var source = new SonnetLine("世界， 再见！", 1, 4,
        [
            new("世界", 1, 2),
            new("再见", 2.5, 3.7),
        ]);

        var segments = SonnetProgramCompiler.BuildSemanticSegments(source);

        Assert.Equal(source.FullText, string.Concat(segments.Select(segment => segment.Text)));
        Assert.Contains("，", segments[0].Text);
        Assert.Contains(0, segments.SelectMany(segment => segment.WordIndices));
        Assert.True(segments[^1].EndTime <= source.EndTime);
    }

    [Fact]
    public void Compiler_CapsParagraphsAndGroupsAtMostFourLinesPerShot()
    {
        var program = SonnetProgramCompiler.Compile(
            Enumerable.Range(0, 8).Select(index => Line($"line {index}", index * 1.1, index * 1.1 + 0.8)).ToArray(), "caps");

        Assert.True(program.Paragraphs.Count > 1);
        Assert.All(program.Paragraphs, paragraph => Assert.True(paragraph.Lines.Count <= 6));
        Assert.All(program.Paragraphs.SelectMany(paragraph => paragraph.Shots), shot => Assert.True(shot.LineIndices.Count <= 4));
    }

    [Fact]
    public void Layouts_KeepSegmentOrderButProduceSevenDistinctCompositions()
    {
        var segments = SonnetProgramCompiler.BuildSemanticSegments(new SonnetLine(
            "明かり に あなたへ", 0, 3,
            [new("明かり", 0, 0.8), new("に", 1, 1.4), new("あなたへ", 1.5, 3)]));
        var signatures = new HashSet<string>();
        foreach (var kind in SonnetProgramCompiler.ShotKinds)
        {
            var layout = SonnetTypographyLayout.Resolve([segments], kind, SonnetParagraphKind.Verse,
                1280, 720, 40, (text, size, _) => (text.Length * size * 0.58f, size * 1.2f));
            Assert.Equal(segments.Count, layout.Count);
            Assert.Equal(Enumerable.Range(0, segments.Count), layout.Select(item => item.SegmentIndex));
            signatures.Add(string.Join('|', layout.Select(item => $"{item.X:F1},{item.Y:F1},{item.Rotation:F2}")));
        }
        Assert.Equal(7, signatures.Count);
    }

    [Fact]
    public void Motion_UsesAbsoluteTimeAndStableFocusWeights()
    {
        var first = SonnetMotion.ShotFrame(SonnetShotKind.TypeImpact, 0.42);
        var second = SonnetMotion.ShotFrame(SonnetShotKind.TypeImpact, 0.42);
        var weights = SonnetMotion.FocusWeights([(1, 2), (4, 5)], 3);

        Assert.Equal(first, second);
        Assert.Equal(1, weights.Sum(), 10);
        Assert.All(weights, weight => Assert.InRange(weight, 0, 1));
    }

    [Fact]
    public void MotionGraphics_AreDeterministicAndSeekStable()
    {
        var first = BuildMg(0x1234abcd);
        var second = BuildMg(0x1234abcd);
        first.Update(8.25, 2, new(0.4f, 0.7f, 0.2f), new(24, -12), 1.08f);
        second.Update(3.1, 2, new(0, 0, 0), Vector2.Zero, 1);
        second.Update(8.25, 2, new(0.4f, 0.7f, 0.2f), new(24, -12), 1.08f);

        var firstSnapshot = first.Snapshot();
        var secondSnapshot = second.Snapshot();
        Assert.Equal(firstSnapshot, secondSnapshot);
    }

    [Fact]
    public void MotionGraphics_ShowFiniteCometTailsInsteadOfWholePaths()
    {
        var guide = BuildGuide(0x8910abcd);

        guide.Update(0.24);

        Assert.True(guide.VisibleTrailSegments > 0);
        Assert.True(guide.VisibleTrailSegments < guide.TotalTrailSegments);
    }

    [Fact]
    public void MotionGraphics_ClampAudioWithoutChangingBasePathsOrParticlePositions()
    {
        var view = BuildMg(0x10203040);
        view.Update(4.5, 0, new(-20, float.PositiveInfinity, float.NaN), new(8, 12), 1.04f);
        var clamped = view.Snapshot();
        view.Update(4.5, 0, new(0, 0, 0), new(8, 12), 1.04f);
        var zero = view.Snapshot();

        Assert.Equal(clamped.Select(particle => particle.Position),
            zero.Select(particle => particle.Position));
        Assert.All(clamped, particle =>
        {
            Assert.True(float.IsFinite(particle.Alpha));
            Assert.True(float.IsFinite(particle.Scale.X));
        });
    }

    private static SonnetMgView BuildMg(uint seed)
    {
        var shot = new SonnetShot("shot", SonnetShotKind.TypeImpact, 1, 12, [0], [], new(0, 0, 1, 0));
        var theme = new SonnetTheme(new(0.02f, 0.03f, 0.05f), new(0.95f, 0.95f, 0.92f),
            new(1, 0.25f, 0.42f), new(0.1f, 0.8f, 0.94f));
        return SonnetMgBuilder.BuildShot(shot, theme, 1280, 720, seed, new SonnetTuning());
    }

    private static SonnetGuideView BuildGuide(uint seed)
    {
        var segment = new SonnetSemanticSegment("世界", 0, 2, 2, 3, [0], [], true);
        var placement = new SonnetTypographyPlacement(0, "世界", SonnetSegmentRole.Hero,
            1, 160, 80, 0, 0, 0, -120, 40, false, 0.2f);
        var theme = new SonnetTheme(new(0.02f, 0.03f, 0.05f), EffectColor.White,
            new(1, 0.25f, 0.42f), new(0.1f, 0.8f, 0.94f));
        return SonnetMgBuilder.BuildGuide(segment, placement, 72, theme, seed);
    }
}
