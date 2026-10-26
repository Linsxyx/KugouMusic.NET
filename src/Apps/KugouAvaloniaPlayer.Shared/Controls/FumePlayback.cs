using System;

namespace KugouAvaloniaPlayer.Controls;

// Shared absolute-time rules for CPU camera data and GPU text slices.
internal static class FumePlayback
{
    public static double ResolvePrintedProgress(FumeArticleBlock block, double seconds)
    {
        var start = block.Line.Start.TotalSeconds;
        var end = start + Math.Max(block.Line.Duration.TotalSeconds, 0.12);
        if (seconds <= start)
            return 0;
        if (seconds >= end)
            return block.Graphemes.Count;
        if (!HasTimedWordRanges(block))
            return Math.Clamp((seconds - start) / (end - start), 0, 1) *
                   block.Graphemes.Count;

        var printed = 0d;
        foreach (var range in block.WordRanges)
        {
            if (range.End <= range.Start || range.EndSeconds <= range.StartSeconds)
                continue;
            if (seconds < range.StartSeconds)
                return printed;
            var progress = Math.Clamp(
                (seconds - range.StartSeconds) /
                (range.EndSeconds - range.StartSeconds),
                0,
                1);
            printed = range.Start + (range.End - range.Start) * progress;
            if (progress < 1)
                return printed;
        }
        return printed;
    }

    public static bool HasTimedWordRanges(FumeArticleBlock block)
    {
        foreach (var range in block.WordRanges)
        {
            if (range.End > range.Start && range.EndSeconds > range.StartSeconds)
                return true;
        }

        return false;
    }

    public static double ResolvePlayedFraction(
        FumeArticleBlock block,
        int glyphIndex,
        int rangeIndex,
        double printedProgress,
        double seconds)
    {
        if (rangeIndex < 0 ||
            rangeIndex >= block.WordRanges.Count ||
            block.WordRanges[rangeIndex].EndSeconds <= block.WordRanges[rangeIndex].StartSeconds)
        {
            return Math.Clamp(printedProgress - glyphIndex, 0, 1);
        }

        var range = block.WordRanges[rangeIndex];
        var rangeStart = block.GlyphOffsets[range.Start];
        var rangeEnd = block.GlyphOffsets[range.End];
        var rangeWidth = Math.Max(rangeEnd - rangeStart, 0.001);
        var rangeProgress = Math.Clamp(
            (seconds - range.StartSeconds) /
            (range.EndSeconds - range.StartSeconds),
            0,
            1);
        var playedOffset = rangeStart + rangeWidth * rangeProgress;
        var glyphStart = block.GlyphOffsets[glyphIndex];
        var glyphEnd = block.GlyphOffsets[glyphIndex + 1];
        return Math.Clamp(
            (playedOffset - glyphStart) / Math.Max(glyphEnd - glyphStart, 0.001),
            0,
            1);
    }

    public static void ResolveGlyphTiming(
        FumeArticleBlock block,
        int glyphIndex,
        int rangeIndex,
        out double start,
        out double end)
    {
        if (rangeIndex < 0 || rangeIndex >= block.WordRanges.Count)
        {
            var lineStart = block.Line.Start.TotalSeconds;
            var lineDuration = Math.Max(block.Line.Duration.TotalSeconds, 0.12);
            var count = Math.Max(block.Graphemes.Count, 1);
            start = lineStart + glyphIndex / (double)count * lineDuration;
            end = lineStart + (glyphIndex + 1d) / count * lineDuration;
            return;
        }

        var range = block.WordRanges[rangeIndex];
        var duration = Math.Max(range.EndSeconds - range.StartSeconds, 0.08);
        var rangeStart = block.GlyphOffsets[range.Start];
        var rangeWidth = Math.Max(block.GlyphOffsets[range.End] - rangeStart, 0.001);
        var glyphStart = block.GlyphOffsets[glyphIndex];
        var glyphEnd = block.GlyphOffsets[glyphIndex + 1];
        start = range.StartSeconds + (glyphStart - rangeStart) / rangeWidth * duration;
        end = range.StartSeconds + (glyphEnd - rangeStart) / rangeWidth * duration;
    }
}
