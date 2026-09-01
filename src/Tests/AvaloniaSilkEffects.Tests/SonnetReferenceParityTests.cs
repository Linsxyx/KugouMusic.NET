using AvaloniaSilkEffects.Sonnet;
using System.Numerics;
using System.Text.Json;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetReferenceParityTests
{
    private const double Tolerance = 1e-4;
    private static readonly JsonDocument Reference = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sonnet-reference.json")));

    [Fact]
    public void Reference_IsPinnedToFrozenFoliaRevision()
    {
        var manifest = Reference.RootElement.GetProperty("manifest");
        Assert.Equal("0.7.2", manifest.GetProperty("foliaVersion").GetString());
        Assert.Equal("d5b8b24d5c873362f17bb372028afdbc30a4d2b2",
            manifest.GetProperty("commit").GetString());
        Assert.Equal("PingFang SC", manifest.GetProperty("fontFamily").GetString());
    }

    [Fact]
    public void RandomAndMotion_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("random").EnumerateArray())
            Assert.Equal(item.GetProperty("hash").GetUInt32(), SonnetRandom.Hash(item.GetProperty("value").GetString()!));

        foreach (var item in Reference.RootElement.GetProperty("motion").EnumerateArray())
        {
            var kind = ShotKind(item.GetProperty("kind").GetString()!);
            var progress = item.GetProperty("progress").GetDouble();
            Near(item.GetProperty("pathProgress").GetDouble(), SonnetMotion.ShotPathProgress(kind, progress));
            Frame(item.GetProperty("frame"), SonnetMotion.ShotFrame(kind, progress));
        }
        foreach (var item in Reference.RootElement.GetProperty("cameraBreath").EnumerateArray())
            Frame(item.GetProperty("frame"), SonnetMotion.CameraBreath(item.GetProperty("time").GetDouble(), 0.37));
    }

    [Fact]
    public void FocusTransitionsAndCredits_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("focusWeights").EnumerateArray())
        {
            var actual = SonnetMotion.FocusWeights([(1, 2), (4, 5)], item.GetProperty("time").GetDouble());
            var expected = item.GetProperty("weights").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            Assert.Equal(expected.Length, actual.Count);
            for (var index = 0; index < expected.Length; index++) Near(expected[index], actual[index]);
        }

        foreach (var item in Reference.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var kind = TransitionKind(item.GetProperty("kind").GetString()!);
            var frame = SonnetTransitions.Resolve(kind, item.GetProperty("entering").GetBoolean(),
                item.GetProperty("progress").GetDouble(), 0x12345678);
            TransitionFrame(item.GetProperty("frame"), frame);
        }

        foreach (var item in Reference.RootElement.GetProperty("credits").EnumerateArray())
        {
            var expected = item.GetProperty("frame");
            var actual = SonnetCredits.Resolve(10 + item.GetProperty("elapsed").GetDouble(), 10);
            Assert.Equal(expected.GetProperty("active").GetBoolean(), actual.Active);
            Near(expected.GetProperty("lyricAlpha").GetDouble(), actual.LyricAlpha);
            Near(expected.GetProperty("lyricBlur").GetDouble(), actual.LyricBlur);
            Near(expected.GetProperty("posterAlpha").GetDouble(), actual.PosterAlpha);
            Near(expected.GetProperty("posterOffsetY").GetDouble(), actual.PosterOffsetY);
            Near(expected.GetProperty("posterScale").GetDouble(), actual.PosterScale);
        }
    }

    [Fact]
    public void AllVariantSelectors_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("variants").EnumerateArray())
        {
            var seed = item.GetProperty("seed").GetInt32();
            Assert.Equal(item.GetProperty("geo").GetInt32(), SonnetVariantResolver.Geometry(seed));
            Assert.Equal(item.GetProperty("molecule").GetInt32(), SonnetVariantResolver.Molecule(seed));
            Assert.Equal(item.GetProperty("hudRotation").GetInt32(), SonnetVariantResolver.HudRotationQuarterTurns(seed));
            Assert.Equal(item.GetProperty("background").GetInt32(), SonnetVariantResolver.Background((uint)seed));
            Assert.Equal(item.GetProperty("decor").GetInt32(), SonnetVariantResolver.BackgroundDecor((uint)seed));
            Assert.Equal(item.GetProperty("fixed").GetInt32(), SonnetVariantResolver.FixedGeometry((uint)seed));
        }
    }

    [Fact]
    public void TypographyRoles_MatchFrozenReference()
    {
        var reference = Reference.RootElement.GetProperty("typographyRoles");
        foreach (var item in reference.GetProperty("weights").EnumerateArray())
        {
            var configured = item.GetProperty("weight").ValueKind == JsonValueKind.Null
                ? (int?)null
                : item.GetProperty("weight").GetInt32();
            Assert.Equal(
                item.GetProperty("resolved").GetInt32(),
                SonnetTypographyRoles.ResolveFontWeight(configured, SegmentRole(item.GetProperty("role").GetString()!)));
        }

        foreach (var item in reference.GetProperty("cases").EnumerateArray())
        {
            var segments = item.GetProperty("segments").EnumerateArray().Select(SemanticSegment).ToArray();
            var visibleLengths = item.GetProperty("visibleLengths").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var scores = item.GetProperty("scores").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            Assert.Equal(visibleLengths, segments.Select(SonnetTypographyRoles.VisibleLength));
            for (var index = 0; index < segments.Length; index++)
                Near(scores[index], SonnetTypographyRoles.HeroScore(segments[index]));

            var hero = SonnetTypographyRoles.FindHeroIndex(segments);
            Assert.Equal(item.GetProperty("hero").GetInt32(), hero);
            Assert.Equal(
                item.GetProperty("semiHeroes").EnumerateArray().Select(value => value.GetInt32()),
                SonnetTypographyRoles.FindSemiHeroIndices(segments, hero));
        }
    }

    [Fact]
    public void GlyphLayouts_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("glyphLayouts").EnumerateArray())
        {
            var segment = SemanticSegment(item.GetProperty("segment"));
            var placement = TypographyPlacement(item.GetProperty("placement"));
            var window = item.GetProperty("window");
            var startTime = window.GetProperty("startTime").GetDouble();
            var endTime = window.GetProperty("endTime").GetDouble();
            var fontSize = item.GetProperty("fontSize").GetSingle();
            var measures = item.GetProperty("measures").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetSingle());

            Near(item.GetProperty("duration").GetDouble(), SonnetGlyphLayout.ResolveMotionDuration(startTime, endTime));
            var actual = SonnetGlyphLayout.Build(
                segment, placement, fontSize,
                text => measures.TryGetValue(text, out var width) ? width : 20,
                startTime, endTime);
            var expected = item.GetProperty("glyphs").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, actual.Count);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index].GetProperty("char").GetString(), actual[index].Text);
                Vector(expected[index], "baseX", "baseY", actual[index].Position);
                Vector(expected[index], "enterX", "enterY", actual[index].Entrance);
                Near(expected[index].GetProperty("entryRotation").GetDouble(), actual[index].EntryRotation);
                Near(expected[index].GetProperty("startTime").GetDouble(), actual[index].StartTime);
                Near(expected[index].GetProperty("settleTime").GetDouble(), actual[index].SettleTime);
            }
        }
    }

    [Fact]
    public void NonPosterFlowLayouts_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("flowLayouts").EnumerateArray())
        {
            var gaps = item.GetProperty("gaps");
            var resolvedGaps = SonnetShotFlowLayouts.ResolveGaps(48);
            Near(gaps.GetProperty("flowGap").GetDouble(), resolvedGaps.FlowGap);
            Near(gaps.GetProperty("stackGap").GetDouble(), resolvedGaps.StackGap);

            var expected = item.GetProperty("boxes").EnumerateArray().ToArray();
            var boxes = item.GetProperty("inputBoxes").EnumerateArray().Select(FlowBox).ToArray();

            var context = new SonnetFlowLayoutContext(
                boxes, 3,
                item.GetProperty("width").GetDouble(),
                item.GetProperty("height").GetDouble(),
                resolvedGaps.FlowGap,
                resolvedGaps.StackGap);
            var variant = item.GetProperty("variant").GetInt32();
            var kind = item.GetProperty("kind").GetString();
            if (kind == "quiet")
                SonnetShotFlowLayouts.LayoutQuietTableau(context, variant);
            else if (kind == "ribbon")
                SonnetShotFlowLayouts.LayoutTrackingRibbon(context, variant);
            else if (kind == "cross")
                SonnetShotFlowLayouts.LayoutCrossStack(context);
            else if (kind == "editorial")
                SonnetShotFlowLayouts.LayoutEditorialColumn(
                    context, variant, item.GetProperty("secondaryHeroIndex").GetInt32());
            else
                SonnetShotFlowLayouts.LayoutFragmentCollage(context, variant);

            for (var index = 0; index < boxes.Length; index++)
                FlowBox(expected[index], boxes[index]);
        }
    }

    [Fact]
    public void PosterBlocksLayouts_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("posterLayouts").EnumerateArray())
        {
            var boxes = item.GetProperty("inputBoxes").EnumerateArray().Select(PosterBox).ToArray();
            var actual = SonnetPosterBlocksLayout.Layout(
                boxes,
                item.GetProperty("width").GetDouble(),
                item.GetProperty("height").GetDouble(),
                item.GetProperty("baseFontSize").GetDouble(),
                item.GetProperty("seed").GetUInt32());
            var expectedPlan = item.GetProperty("plan");
            Near(expectedPlan.GetProperty("width").GetDouble(), actual.Width);
            Near(expectedPlan.GetProperty("height").GetDouble(), actual.Height);
            Near(expectedPlan.GetProperty("gap").GetDouble(), actual.Gap);
            var expected = expectedPlan.GetProperty("placements").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, actual.Placements.Count);
            for (var index = 0; index < expected.Length; index++)
                PosterBox(expected[index], actual.Placements[index]);
        }
    }

    [Fact]
    public void SpatialPrismDrawLists_MatchFrozenReference()
    {
        foreach (var item in Reference.RootElement.GetProperty("drawLists").GetProperty("spatialPrisms").EnumerateArray())
        {
            var actual = item.GetProperty("name").GetString() switch
            {
                "solid-cuboid" => SonnetSpatialDrawLists.SolidCuboid(12, -18, 160, 96, 34, -22, 0x8fd3ff, 0.72),
                "triangular-prism" => SonnetSpatialDrawLists.TriangularPrism(-20, 14, 180, 120, 28, -19, 0xff6f91, 0.64),
                "hexagonal-prism" => SonnetSpatialDrawLists.HexagonalPrism(8, 4, 190, 130, -32, 24, 0xa8ff78, 0.58),
                "trapezoid-prism" => SonnetSpatialDrawLists.TrapezoidPrism(0, -6, 110, 210, 120, 30, 18, 0xf4d35e, 0.68),
                var name => throw new ArgumentOutOfRangeException(nameof(name), name, null),
            };
            DrawCommands(item.GetProperty("commands"), actual.Commands);
        }
    }

    [Fact]
    public void FrozenReference_ContainsAllAdditionalMgDrawLists()
    {
        var variants = Reference.RootElement.GetProperty("drawLists").GetProperty("additionalVariants")
            .EnumerateArray().ToArray();
        Assert.Equal(82, variants.Length);
        Assert.Equal(Enumerable.Range(18, 82), variants.Select(item => item.GetProperty("variant").GetInt32()));
        Assert.All(variants, item => Assert.NotEmpty(item.GetProperty("commands").EnumerateArray()));
    }

    private static void Frame(JsonElement expected, SonnetMotionFrame actual)
    {
        Near(expected.GetProperty("x").GetDouble(), actual.X);
        Near(expected.GetProperty("y").GetDouble(), actual.Y);
        Near(expected.GetProperty("scale").GetDouble(), actual.Scale);
        Near(expected.GetProperty("rotation").GetDouble(), actual.Rotation);
    }

    private static void TransitionFrame(JsonElement expected, SonnetTransitionFrame actual)
    {
        Near(expected.GetProperty("x").GetDouble(), actual.X);
        Near(expected.GetProperty("y").GetDouble(), actual.Y);
        Near(expected.GetProperty("scale").GetDouble(), actual.Scale);
        Near(expected.GetProperty("rotation").GetDouble(), actual.Rotation);
        Near(expected.GetProperty("alpha").GetDouble(), actual.Alpha);
        Near(expected.GetProperty("blur").GetDouble(), actual.Blur);
        Near(expected.GetProperty("glitch").GetDouble(), actual.Glitch);
        Near(expected.GetProperty("glitchSeed").GetDouble(), actual.GlitchSeed);
    }

    private static SonnetShotKind ShotKind(string value) => value switch
    {
        "editorial-column" => SonnetShotKind.EditorialColumn,
        "type-impact" => SonnetShotKind.TypeImpact,
        "fragment-collage" => SonnetShotKind.FragmentCollage,
        "tracking-ribbon" => SonnetShotKind.TrackingRibbon,
        "mask-reveal" => SonnetShotKind.MaskReveal,
        "poster-blocks" => SonnetShotKind.PosterBlocks,
        "quiet-tableau" => SonnetShotKind.QuietTableau,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static SonnetTransitionKind TransitionKind(string value) => value switch
    {
        "fast-blur" => SonnetTransitionKind.FastBlur,
        "mono-glitch" => SonnetTransitionKind.MonoGlitch,
        "camera-pull" => SonnetTransitionKind.CameraPull,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static SonnetSegmentRole SegmentRole(string value) => value switch
    {
        "hero" => SonnetSegmentRole.Hero,
        "semi-hero" => SonnetSegmentRole.SemiHero,
        "support" => SonnetSegmentRole.Support,
        "decoration" => SonnetSegmentRole.Decoration,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static SonnetSemanticSegment SemanticSegment(JsonElement value) => new(
        value.GetProperty("text").GetString()!,
        value.GetProperty("startOffset").GetInt32(),
        value.GetProperty("endOffset").GetInt32(),
        value.GetProperty("startTime").GetDouble(),
        value.GetProperty("endTime").GetDouble(),
        value.GetProperty("wordIndices").EnumerateArray().Select(item => item.GetInt32()).ToArray(),
        value.GetProperty("graphemes").EnumerateArray().Select(item => new SonnetGraphemeTiming(
            item.GetProperty("char").GetString()!,
            item.GetProperty("startTime").GetDouble(),
            item.GetProperty("endTime").GetDouble())).ToArray(),
        value.GetProperty("isWordLike").GetBoolean());

    private static SonnetTypographyPlacement TypographyPlacement(JsonElement value) => new(
        value.GetProperty("segmentIndex").GetInt32(),
        value.GetProperty("displayText").GetString()!,
        SegmentRole(value.GetProperty("role").GetString()!),
        value.GetProperty("fontScale").GetSingle(),
        value.GetProperty("measuredWidth").GetSingle(),
        value.GetProperty("measuredHeight").GetSingle(),
        value.GetProperty("x").GetSingle(),
        value.GetProperty("y").GetSingle(),
        value.GetProperty("rotation").GetSingle(),
        value.GetProperty("enterX").GetSingle(),
        value.GetProperty("enterY").GetSingle(),
        value.GetProperty("vertical").GetBoolean(),
        value.GetProperty("timingPhase").GetSingle());

    private static SonnetFlowLayoutBox FlowBox(JsonElement value) => new()
    {
        Index = value.GetProperty("index").GetInt32(),
        IsHero = value.GetProperty("isHero").GetBoolean(),
        IsSemiHero = value.GetProperty("isSemiHero").GetBoolean(),
        DisplayText = value.GetProperty("displayText").GetString()!,
        FontScale = value.GetProperty("fontScale").GetDouble(),
        MeasuredWidth = value.GetProperty("measuredWidth").GetDouble(),
        MeasuredHeight = value.GetProperty("measuredHeight").GetDouble(),
        Vertical = value.GetProperty("vertical").GetBoolean(),
        LayoutDirection = LayoutDirection(value.GetProperty("layoutDirection").GetString()!),
        Rotation = value.GetProperty("rotation").GetDouble(),
        X = value.GetProperty("x").GetDouble(),
        Y = value.GetProperty("y").GetDouble(),
        EnterX = value.GetProperty("enterX").GetDouble(),
        EnterY = value.GetProperty("enterY").GetDouble(),
    };

    private static SonnetPosterBlockBox PosterBox(JsonElement value) => new()
    {
        IsHero = value.GetProperty("isHero").GetBoolean(),
        IsSemiHero = value.GetProperty("isSemiHero").GetBoolean(),
        DisplayText = value.GetProperty("displayText").GetString()!,
        VerticalDisplayText = value.TryGetProperty("verticalDisplayText", out var verticalText)
            ? verticalText.GetString() : null,
        VerticalMeasuredWidth = value.TryGetProperty("verticalMeasuredWidth", out var verticalWidth)
            ? verticalWidth.GetDouble() : null,
        VerticalMeasuredHeight = value.TryGetProperty("verticalMeasuredHeight", out var verticalHeight)
            ? verticalHeight.GetDouble() : null,
        VerticalFontScale = value.TryGetProperty("verticalFontScale", out var verticalScale)
            ? verticalScale.GetDouble() : null,
        FontScale = value.GetProperty("fontScale").GetDouble(),
        MeasuredWidth = value.GetProperty("measuredWidth").GetDouble(),
        MeasuredHeight = value.GetProperty("measuredHeight").GetDouble(),
        X = value.GetProperty("x").GetDouble(),
        Y = value.GetProperty("y").GetDouble(),
        Rotation = value.GetProperty("rotation").GetDouble(),
        Vertical = value.GetProperty("vertical").GetBoolean(),
        LayoutDirection = LayoutDirection(value.GetProperty("layoutDirection").GetString()!),
        EnterX = value.GetProperty("enterX").GetDouble(),
        EnterY = value.GetProperty("enterY").GetDouble(),
    };

    private static void PosterBox(JsonElement expected, SonnetPosterBlockBox actual)
    {
        Assert.Equal(expected.GetProperty("displayText").GetString(), actual.DisplayText);
        Assert.Equal(expected.GetProperty("vertical").GetBoolean(), actual.Vertical);
        Assert.Equal(LayoutDirection(expected.GetProperty("layoutDirection").GetString()!), actual.LayoutDirection);
        Near(expected.GetProperty("fontScale").GetDouble(), actual.FontScale);
        Near(expected.GetProperty("measuredWidth").GetDouble(), actual.MeasuredWidth);
        Near(expected.GetProperty("measuredHeight").GetDouble(), actual.MeasuredHeight);
        Near(expected.GetProperty("x").GetDouble(), actual.X);
        Near(expected.GetProperty("y").GetDouble(), actual.Y);
        Near(expected.GetProperty("rotation").GetDouble(), actual.Rotation);
        Near(expected.GetProperty("enterX").GetDouble(), actual.EnterX);
        Near(expected.GetProperty("enterY").GetDouble(), actual.EnterY);
    }

    private static void DrawCommands(JsonElement expectedElement, IReadOnlyList<SonnetPaintCommand> actual)
    {
        var expected = expectedElement.EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(PaintKind(expected[index].GetProperty("kind").GetString()!), actual[index].Kind);
            Assert.Equal(expected[index].GetProperty("color").GetUInt32(), actual[index].Color);
            Near(expected[index].GetProperty("alpha").GetDouble(), actual[index].Alpha);
            Near(expected[index].GetProperty("width").GetDouble(), actual[index].Width);
            Near(expected[index].GetProperty("length").GetDouble(), actual[index].Length);
            Near(expected[index].GetProperty("staggerDelay").GetDouble(), actual[index].StaggerDelay);
            Near(expected[index].GetProperty("staggerSpan").GetDouble(), actual[index].StaggerSpan);
            var expectedPath = expected[index].GetProperty("path").EnumerateArray().ToArray();
            Assert.Equal(expectedPath.Length, actual[index].Path.Count);
            for (var pathIndex = 0; pathIndex < expectedPath.Length; pathIndex++)
            {
                var path = expectedPath[pathIndex];
                var command = actual[index].Path[pathIndex];
                Assert.Equal(PathVerb(path.GetProperty("verb").GetString()!), command.Verb);
                Near(Number(path, "a"), command.A); Near(Number(path, "b"), command.B);
                Near(Number(path, "c"), command.C); Near(Number(path, "d"), command.D);
                Near(Number(path, "e"), command.E); Near(Number(path, "f"), command.F);
                Near(Number(path, "length"), command.Length);
                Near(Number(path, "lastX"), command.LastX); Near(Number(path, "lastY"), command.LastY);
            }
        }
    }

    private static double Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? property.GetDouble() : 0;

    private static SonnetPaintKind PaintKind(string value) => value switch
    {
        "stroke" => SonnetPaintKind.Stroke,
        "fill" => SonnetPaintKind.Fill,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static SonnetPathVerb PathVerb(string value) => value switch
    {
        "moveTo" => SonnetPathVerb.MoveTo,
        "lineTo" => SonnetPathVerb.LineTo,
        "quadraticCurveTo" => SonnetPathVerb.QuadraticCurveTo,
        "bezierCurveTo" => SonnetPathVerb.BezierCurveTo,
        "arc" => SonnetPathVerb.Arc,
        "circle" => SonnetPathVerb.Circle,
        "rectangle" => SonnetPathVerb.Rectangle,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static void FlowBox(JsonElement expected, SonnetFlowLayoutBox actual)
    {
        Assert.Equal(expected.GetProperty("index").GetInt32(), actual.Index);
        Assert.Equal(LayoutDirection(expected.GetProperty("layoutDirection").GetString()!), actual.LayoutDirection);
        Near(expected.GetProperty("fontScale").GetDouble(), actual.FontScale);
        Near(expected.GetProperty("measuredWidth").GetDouble(), actual.MeasuredWidth);
        Near(expected.GetProperty("measuredHeight").GetDouble(), actual.MeasuredHeight);
        Near(expected.GetProperty("rotation").GetDouble(), actual.Rotation);
        Near(expected.GetProperty("x").GetDouble(), actual.X);
        Near(expected.GetProperty("y").GetDouble(), actual.Y);
        Near(expected.GetProperty("enterX").GetDouble(), actual.EnterX);
        Near(expected.GetProperty("enterY").GetDouble(), actual.EnterY);
    }

    private static SonnetLayoutDirection LayoutDirection(string value) => value switch
    {
        "horizontal" => SonnetLayoutDirection.Horizontal,
        "vertical" => SonnetLayoutDirection.Vertical,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static void Vector(JsonElement expected, string x, string y, Vector2 actual)
    {
        Near(expected.GetProperty(x).GetDouble(), actual.X);
        Near(expected.GetProperty(y).GetDouble(), actual.Y);
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, Tolerance);
}
