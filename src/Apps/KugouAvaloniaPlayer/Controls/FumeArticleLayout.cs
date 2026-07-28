using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaLyrics;
using SkiaSharp;

namespace KugouAvaloniaPlayer.Controls;

internal sealed class FumeArticleLayout
{
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double ViewportHeight { get; init; }
    public required FumePaperBounds PaperBounds { get; init; }
    public required IReadOnlyList<FumeArticleBlock> Blocks { get; init; }
    public required IReadOnlyList<FumeArticleBlock> ChronologicalBlocks { get; init; }
    public required IReadOnlyDictionary<int, FumeArticleBlock> BlocksBySourceIndex { get; init; }

    public double FirstStartSeconds =>
        ChronologicalBlocks.Count == 0
            ? double.PositiveInfinity
            : ChronologicalBlocks[0].Line.Start.TotalSeconds;

    public double LastEndSeconds =>
        ChronologicalBlocks.Count == 0
            ? double.NegativeInfinity
            : ChronologicalBlocks[^1].Line.Start.TotalSeconds +
              ChronologicalBlocks[^1].Line.Duration.TotalSeconds;
}

internal readonly record struct FumePaperBounds(
    double Left,
    double Top,
    double Right,
    double Bottom);

internal sealed class FumeArticleBlock
{
    public required int SourceLineIndex { get; init; }
    public required LyricLine Line { get; init; }
    public required bool IsHero { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double FontSize { get; init; }
    public required string TypefaceFamily { get; init; }
    public required double LineHeight { get; init; }
    public required IReadOnlyList<string> Graphemes { get; init; }
    public required IReadOnlyList<double> GlyphOffsets { get; init; }
    public required IReadOnlyList<int> WordRangeByGlyph { get; init; }
    public required IReadOnlyList<FumeWordRange> WordRanges { get; init; }
    public required IReadOnlyList<FumeRenderLine> RenderLines { get; init; }
}

internal readonly record struct FumeWordRange(
    int Start,
    int End,
    double StartSeconds,
    double EndSeconds);

internal sealed class FumeRenderLine
{
    public required int Start { get; init; }
    public required int End { get; init; }
    public required double Top { get; init; }
    public required double Width { get; init; }
}

internal static class FumeArticleLayoutEngine
{
    private const double TargetHeightRatio = 2.45;

    public static FumeArticleLayout? Build(
        IReadOnlyList<LyricLine> lines,
        double viewportWidth,
        double viewportHeight,
        string fontFamily,
        double lyricsFontScale,
        double heroScale)
    {
        if (lines.Count == 0 || viewportWidth <= 1 || viewportHeight <= 1)
            return null;

        var entries = lines
            .Select((line, index) => new SourceEntry(line, index))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Line.Text))
            .OrderBy(entry => StableUnit($"{fontFamily}:{entry.Index}:{entry.Line.Text}"))
            .ToArray();
        if (entries.Length == 0)
            return null;

        var paperWidth = Clamp(Math.Max(viewportWidth * 1.95, viewportWidth + 520), 920, 2400);
        var safeViewportHeight = Math.Max(viewportHeight, 240);
        var maxColumns = paperWidth >= 1120 ? 4 : paperWidth >= 760 ? 3 : paperWidth >= 500 ? 2 : 1;
        var targetHeight = safeViewportHeight * TargetHeightRatio;
        var forcedHeroIndex = ChooseFallbackHero(entries);

        using var requestedBodyTypeface = SKTypeface.FromFamilyName(
            fontFamily,
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var requestedHeroTypeface = SKTypeface.FromFamilyName(
            fontFamily,
            SKFontStyleWeight.SemiBold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        var bodyTypeface = requestedBodyTypeface ?? SKTypeface.Default;
        var heroTypeface = requestedHeroTypeface ?? bodyTypeface;
        using var paint = new SKPaint { IsAntialias = true };

        AttemptOptions? best = null;
        var bestScore = double.PositiveInfinity;
        for (var columns = maxColumns; columns >= 1; columns--)
        {
            var low = 0.82;
            var high = 1.42;
            var gap = Clamp(
                Math.Round(paperWidth * (columns >= 4 ? 0.0065 : columns == 3 ? 0.0085 : 0.0115)),
                6,
                14);

            for (var iteration = 0; iteration < 8; iteration++)
            {
                var density = (low + high) * 0.5;
                var options = new AttemptOptions(paperWidth, safeViewportHeight, columns, gap, density);
                var metrics = BuildAttempt(
                    entries,
                    viewportWidth,
                    viewportHeight,
                    fontFamily,
                    lyricsFontScale,
                    heroScale,
                    forcedHeroIndex,
                    bodyTypeface,
                    heroTypeface,
                    paint,
                    options,
                    false);
                if (metrics == null)
                    continue;

                var coveragePenalty = Math.Abs(metrics.Height - targetHeight);
                var overflowPenalty = metrics.Height < targetHeight ? 0 : (metrics.Height - targetHeight) * 0.14;
                var score = coveragePenalty + overflowPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = options;
                }

                if (metrics.Height < targetHeight)
                    low = density;
                else
                    high = density;
            }
        }

        return best == null
            ? null
            : BuildAttempt(
                entries,
                viewportWidth,
                viewportHeight,
                fontFamily,
                lyricsFontScale,
                heroScale,
                forcedHeroIndex,
                bodyTypeface,
                heroTypeface,
                paint,
                best.Value,
                true);
    }

    private static FumeArticleLayout? BuildAttempt(
        IReadOnlyList<SourceEntry> entries,
        double viewportWidth,
        double viewportHeight,
        string fontFamily,
        double lyricsFontScale,
        double heroScale,
        int forcedHeroIndex,
        SKTypeface bodyTypeface,
        SKTypeface heroTypeface,
        SKPaint paint,
        AttemptOptions options,
        bool includeDetails)
    {
        var horizontalMargin = Math.Max(viewportWidth * 0.86, 280);
        var verticalMargin = Math.Max(viewportHeight * 0.82, 220);
        var columnWidth = (options.PaperWidth - options.Gap * (options.Columns - 1)) / options.Columns;
        var columnHeights = Enumerable.Repeat(verticalMargin, options.Columns).ToArray();
        var blocks = includeDetails ? new List<FumeArticleBlock>(entries.Count) : null;
        var bodyTieCursor = 0;
        var heroTieCursor = 0;

        for (var blockIndex = 0; blockIndex < entries.Count; blockIndex++)
        {
            var entry = entries[blockIndex];
            var isHero = blockIndex == forcedHeroIndex || ChooseNaturalHero(entry.Line, blockIndex, entries.Count);
            var spanColumns = isHero ? Math.Min(options.Columns, options.Columns <= 1 ? 1 : 2) : 1;
            var spanWidth = columnWidth * spanColumns + options.Gap * (spanColumns - 1);
            var blockWidth = isHero
                ? spanColumns == 1
                    ? options.PaperWidth
                    : options.Columns == 2
                        ? columnWidth * 1.5 + options.Gap * 0.5
                        : spanWidth
                : columnWidth;
            var typeface = isHero ? heroTypeface : bodyTypeface;
            var prepared = PrepareText(
                entry.Line,
                blockWidth,
                isHero,
                lyricsFontScale,
                options.DensityScale,
                heroScale,
                typeface,
                paint);
            var lineHeight = prepared.FontSize * (isHero ? 1.02 : 1.06);
            var blockHeight = prepared.RenderLines.Count * lineHeight;
            var blockGap = isHero ? Math.Max(Math.Round(lineHeight * 0.2), 6) : Math.Max(Math.Round(lineHeight * 0.08), 2);

            double x;
            double y;
            if (isHero)
            {
                if (spanColumns == 1)
                {
                    y = columnHeights.Max();
                    x = horizontalMargin;
                    columnHeights[0] = y + blockHeight + blockGap;
                }
                else
                {
                    var bestHeight = double.PositiveInfinity;
                    var candidates = new List<int>();
                    for (var start = 0; start <= options.Columns - spanColumns; start++)
                    {
                        var coveredHeight = 0d;
                        for (var column = start; column < start + spanColumns; column++)
                            coveredHeight = Math.Max(coveredHeight, columnHeights[column]);

                        if (coveredHeight < bestHeight - 0.001)
                        {
                            bestHeight = coveredHeight;
                            candidates.Clear();
                            candidates.Add(start);
                        }
                        else if (Math.Abs(coveredHeight - bestHeight) < 0.001)
                        {
                            candidates.Add(start);
                        }
                    }

                    var targetStart = candidates.Count == 0
                        ? 0
                        : candidates[heroTieCursor++ % candidates.Count];
                    y = bestHeight;
                    x = horizontalMargin + targetStart * (columnWidth + options.Gap) +
                        Math.Max((spanWidth - blockWidth) * 0.5, 0);
                    for (var column = targetStart; column < targetStart + spanColumns; column++)
                        columnHeights[column] = y + blockHeight + blockGap;
                }
            }
            else
            {
                var minHeight = columnHeights.Min();
                var candidates = Enumerable.Range(0, columnHeights.Length)
                    .Where(index => Math.Abs(columnHeights[index] - minHeight) < 0.001)
                    .ToArray();
                var targetColumn = candidates[bodyTieCursor++ % candidates.Length];
                x = horizontalMargin + targetColumn * (columnWidth + options.Gap);
                y = columnHeights[targetColumn];
                columnHeights[targetColumn] = y + blockHeight + blockGap;
            }

            if (blocks != null)
            {
                blocks.Add(new FumeArticleBlock
                {
                    SourceLineIndex = entry.Index,
                    Line = entry.Line,
                    IsHero = isHero,
                    X = x,
                    Y = y,
                    Width = blockWidth,
                    Height = blockHeight,
                    FontSize = prepared.FontSize,
                    TypefaceFamily = prepared.TypefaceFamily,
                    LineHeight = lineHeight,
                    Graphemes = prepared.Graphemes,
                    GlyphOffsets = prepared.GlyphOffsets,
                    WordRangeByGlyph = prepared.WordRangeByGlyph,
                    WordRanges = prepared.WordRanges,
                    RenderLines = prepared.RenderLines
                });
            }
        }

        var articleHeight = columnHeights.Max() + verticalMargin;
        var paperBounds = new FumePaperBounds(
            horizontalMargin,
            verticalMargin,
            horizontalMargin + options.PaperWidth,
            Math.Max(articleHeight - verticalMargin, verticalMargin));
        if (blocks == null)
        {
            return new FumeArticleLayout
            {
                Width = options.PaperWidth + horizontalMargin * 2,
                Height = articleHeight,
                ViewportHeight = options.ViewportHeight,
                PaperBounds = paperBounds,
                Blocks = [],
                ChronologicalBlocks = [],
                BlocksBySourceIndex = new Dictionary<int, FumeArticleBlock>()
            };
        }

        var chronological = blocks.OrderBy(block => block.SourceLineIndex).ToArray();
        return new FumeArticleLayout
        {
            Width = options.PaperWidth + horizontalMargin * 2,
            Height = articleHeight,
            ViewportHeight = options.ViewportHeight,
            PaperBounds = paperBounds,
            Blocks = blocks,
            ChronologicalBlocks = chronological,
            BlocksBySourceIndex = chronological.ToDictionary(block => block.SourceLineIndex)
        };
    }

    private static PreparedBlock PrepareText(
        LyricLine line,
        double width,
        bool isHero,
        double lyricsFontScale,
        double densityScale,
        double heroScale,
        SKTypeface typeface,
        SKPaint paint)
    {
        var graphemes = SplitGraphemes(line.Text);
        SKTypeface? fallbackTypeface = null;
        foreach (var rune in line.Text.EnumerateRunes())
        {
            if (typeface.ContainsGlyph(rune.Value))
                continue;
            fallbackTypeface = SKFontManager.Default.MatchCharacter(rune.Value);
            break;
        }
        var effectiveTypeface = fallbackTypeface ?? typeface;
        var low = isHero ? 18d : 10d;
        var high = isHero ? 58d : 30d;
        var bestSize = low * lyricsFontScale * densityScale * (isHero ? heroScale : 1);

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var rawCandidate = (low + high) * 0.5;
            var candidate = rawCandidate * lyricsFontScale * densityScale * (isHero ? heroScale : 1);
            using var font = new SKFont(effectiveTypeface, (float)candidate);
            var measured = font.MeasureText(line.Text, paint);
            if (measured <= width)
            {
                bestSize = candidate;
                low = rawCandidate;
            }
            else
            {
                high = rawCandidate;
            }
        }

        using var finalFont = new SKFont(effectiveTypeface, (float)bestSize);
        var advances = graphemes.Select(value => (double)finalFont.MeasureText(value, paint)).ToArray();
        var glyphOffsets = new double[graphemes.Count + 1];
        for (var index = 0; index < advances.Length; index++)
            glyphOffsets[index + 1] = glyphOffsets[index] + advances[index];

        var renderLines = BuildRenderLines(glyphOffsets, width);
        var wordRanges = BuildWordRanges(line, graphemes);
        var rangeByGlyph = Enumerable.Repeat(-1, graphemes.Count).ToArray();
        for (var rangeIndex = 0; rangeIndex < wordRanges.Count; rangeIndex++)
        {
            var range = wordRanges[rangeIndex];
            for (var glyph = range.Start; glyph < range.End && glyph < rangeByGlyph.Length; glyph++)
                rangeByGlyph[glyph] = rangeIndex;
        }

        var prepared = new PreparedBlock(
            bestSize,
            effectiveTypeface.FamilyName,
            graphemes,
            glyphOffsets,
            renderLines,
            wordRanges,
            rangeByGlyph);
        fallbackTypeface?.Dispose();
        return prepared;
    }

    private static IReadOnlyList<FumeRenderLine> BuildRenderLines(
        IReadOnlyList<double> glyphOffsets,
        double maxWidth)
    {
        if (glyphOffsets.Count <= 1)
            return [new FumeRenderLine { Start = 0, End = 0, Top = 0, Width = 0 }];

        var result = new List<FumeRenderLine>();
        var start = 0;
        while (start < glyphOffsets.Count - 1)
        {
            var end = start + 1;
            while (end < glyphOffsets.Count &&
                   glyphOffsets[end] - glyphOffsets[start] <= maxWidth)
            {
                end++;
            }

            end = Math.Max(start + 1, end - 1);
            result.Add(new FumeRenderLine
            {
                Start = start,
                End = end,
                Top = result.Count,
                Width = glyphOffsets[end] - glyphOffsets[start]
            });
            start = end;
        }

        return result;
    }

    private static IReadOnlyList<FumeWordRange> BuildWordRanges(
        LyricLine line,
        IReadOnlyList<string> graphemes)
    {
        var result = new List<FumeWordRange>();
        var cursor = 0;
        foreach (var word in line.Words)
        {
            var count = SplitGraphemes(word.Text).Count;
            if (count == 0)
                continue;

            var start = Math.Clamp(cursor, 0, graphemes.Count);
            var end = Math.Clamp(start + count, start, graphemes.Count);
            while (end < graphemes.Count && string.IsNullOrWhiteSpace(graphemes[end]))
                end++;

            result.Add(new FumeWordRange(
                start,
                end,
                word.Start.TotalSeconds,
                word.Start.TotalSeconds + Math.Max(word.Duration.TotalSeconds, 0)));
            cursor = end;
        }

        return result;
    }

    internal static IReadOnlyList<string> SplitGraphemes(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            result.Add(enumerator.GetTextElement());
        return result;
    }

    private static bool ChooseNaturalHero(LyricLine line, int index, int total)
    {
        var count = SplitGraphemes(line.Text).Count(value => !string.IsNullOrWhiteSpace(value));
        if (count is < 4 or > 28)
            return false;

        var centered = Math.Abs(index - total / 2d) / Math.Max(total, 1);
        return centered < 0.72 &&
               ((index + 1) % 6 == 0 || StableUnit($"{line.Text}:{index}") > 0.965);
    }

    private static int ChooseFallbackHero(IReadOnlyList<SourceEntry> entries)
    {
        if (entries.Count == 0)
            return -1;
        for (var index = 0; index < entries.Count; index++)
        {
            if (ChooseNaturalHero(entries[index].Line, index, entries.Count))
                return -1;
        }

        var bestIndex = -1;
        var bestScore = double.NegativeInfinity;
        for (var index = 0; index < entries.Count; index++)
        {
            var count = SplitGraphemes(entries[index].Line.Text)
                .Count(value => !string.IsNullOrWhiteSpace(value));
            if (count == 0 || count > 36)
                continue;

            var centered = Math.Abs(index - entries.Count / 2d) / Math.Max(entries.Count, 1);
            var lengthScore = count is >= 6 and <= 22 ? 1 : count <= 28 ? 0.72 : 0.36;
            var score = (1 - centered) * 0.62 + lengthScore * 0.34 +
                        StableUnit($"{entries[index].Line.Text}:{index}:hero") * 0.04;
            if (score <= bestScore)
                continue;
            bestScore = score;
            bestIndex = index;
        }

        return bestIndex >= 0
            ? bestIndex
            : Enumerable.Range(0, entries.Count)
                .OrderBy(index => SplitGraphemes(entries[index].Line.Text).Count)
                .First();
    }

    private static double StableUnit(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash % 10000 / 10000d;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private readonly record struct SourceEntry(LyricLine Line, int Index);

    private readonly record struct AttemptOptions(
        double PaperWidth,
        double ViewportHeight,
        int Columns,
        double Gap,
        double DensityScale);

    private sealed record PreparedBlock(
        double FontSize,
        string TypefaceFamily,
        IReadOnlyList<string> Graphemes,
        IReadOnlyList<double> GlyphOffsets,
        IReadOnlyList<FumeRenderLine> RenderLines,
        IReadOnlyList<FumeWordRange> WordRanges,
        IReadOnlyList<int> WordRangeByGlyph);
}
