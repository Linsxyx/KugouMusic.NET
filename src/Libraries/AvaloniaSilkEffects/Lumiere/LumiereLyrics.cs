using System.Globalization;

namespace AvaloniaSilkEffects.Lumiere;

public readonly record struct LumiereWord(string Text, double StartTime, double EndTime);

public sealed record LumiereLine(string Text, double StartTime, double EndTime, IReadOnlyList<LumiereWord> Words)
{
    /// <summary>Paragraph metadata from the lyric source (cut paragraphs where they change).</summary>
    public string? SongPart { get; init; }
    public int? BlockIndex { get; init; }
    public bool IsChorus { get; init; }

    /// <summary>
    /// Folia utils/lyrics/renderHints.ts: the latest point a visualizer may keep the line for pass / exit polish after the
    /// reveal (≥ EndTime). Micro lines (&lt; 0.1 s) get a floor, short ones (&lt; 0.18 s) a fast exit.
    /// </summary>
    public double RenderEndTime
    {
        get
        {
            var raw = Math.Max(EndTime - StartTime, 0);
            if (raw < 0.10) return Math.Max(EndTime, StartTime + 0.067);
            var lastWordEnd = Words.Count > 0 ? Words[^1].EndTime : EndTime;
            if (raw < 0.18)
            {
                var enter = Math.Clamp(raw * 0.45, 0.045, 0.06);
                var exitFast = Math.Clamp(raw * 0.22, 0.03, 0.04);
                var passFast = Math.Max(lastWordEnd, StartTime) + 0.03;
                return Math.Max(EndTime, Math.Max(StartTime + enter + 0.01, Math.Max(passFast, EndTime - exitFast)) + exitFast);
            }
            var exit = Math.Min(0.32, Math.Max(0.18, Math.Max(raw, 0.12) * 0.18));
            var pass = Math.Max(lastWordEnd, StartTime) + 0.06;
            return Math.Max(EndTime, Math.Max(pass, EndTime - exit) + exit);
        }
    }
}

/// <summary>Start and end of one grapheme being sung.</summary>
public readonly record struct LumiereGlyphTiming(double Start, double End);

/// <summary>
/// Per-grapheme timing and the reveal envelopes (Folia text/reveal.ts). Graphemes inside a word split the word's
/// time evenly; a line without words splits its own time.
/// </summary>
public static class LumiereReveal
{
    public static IReadOnlyList<string> Graphemes(string text)
    {
        var output = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) output.Add((string)enumerator.Current);
        return output;
    }

    private static void Even(List<LumiereGlyphTiming> output, int count, double start, double end)
    {
        var unit = Math.Max(end - start, 0) / count;
        for (var index = 0; index < count; index++)
            output.Add(new LumiereGlyphTiming(start + unit * index, index == count - 1 ? end : start + unit * (index + 1)));
    }

    private static int FindSequence(IReadOnlyList<string> source, IReadOnlyList<string> target, int from)
    {
        if (target.Count == 0) return from;
        for (var index = from; index <= source.Count - target.Count; index++)
        {
            var matched = true;
            for (var t = 0; t < target.Count && matched; t++) matched = source[index + t] == target[t];
            if (matched) return index;
        }
        return -1;
    }

    /// <summary>
    /// Folia utils/lyrics/graphemeTiming.ts buildLineGraphemeTimeline: word timings mapped back onto the displayed
    /// line, including spaces and punctuation the parser words lack; gaps take the next word's start, the tail the
    /// last resolved time.
    /// </summary>
    public static (IReadOnlyList<string> Graphemes, IReadOnlyList<LumiereGlyphTiming> Timings) Timeline(LumiereLine line)
    {
        var graphemes = Graphemes(line.Text);
        if (graphemes.Count == 0) return (graphemes, []);
        var even = new List<LumiereGlyphTiming>(graphemes.Count);
        if (line.Words.Count == 0)
        {
            Even(even, graphemes.Count, line.StartTime, line.EndTime);
            return (graphemes, even);
        }
        var timeline = new LumiereGlyphTiming?[graphemes.Count];
        var cursor = 0;
        var lastResolved = line.StartTime;
        var wordTimings = new List<LumiereGlyphTiming>();
        foreach (var word in line.Words)
        {
            var wordGraphemes = Graphemes(word.Text);
            if (wordGraphemes.Count == 0) continue;
            var matched = FindSequence(graphemes, wordGraphemes, cursor);
            var start = matched >= 0 ? matched : cursor;
            var end = Math.Min(start + wordGraphemes.Count, graphemes.Count);
            for (var gap = cursor; gap < start; gap++) timeline[gap] = new LumiereGlyphTiming(word.StartTime, word.StartTime);
            wordTimings.Clear();
            Even(wordTimings, wordGraphemes.Count, word.StartTime, word.EndTime);
            for (var local = 0; local < end - start; local++)
            {
                timeline[start + local] = wordTimings[local];
                lastResolved = Math.Max(lastResolved, wordTimings[local].End);
            }
            cursor = Math.Max(cursor, end);
        }
        return (graphemes, [.. timeline.Select(timing => timing ?? new LumiereGlyphTiming(lastResolved, lastResolved))]);
    }

    /// <summary>0 before the glyph is sung, 1 once done; short glyphs get at least 80 ms.</summary>
    public static double Progress(LumiereGlyphTiming timing, double time) =>
        Math.Clamp((time - timing.Start) / Math.Max(timing.End - timing.Start, 0.08), 0, 1);

    /// <summary>The flash at the moment of lighting: smooth rise over attack, then exponential decay.</summary>
    public static double Flash(LumiereGlyphTiming timing, double time, double decay = 0.5, double attack = 0.15)
    {
        var age = time - timing.Start;
        if (age <= 0) return 0;
        if (age < attack)
        {
            var t = age / attack;
            return t * t * (3 - 2 * t);
        }
        return Math.Exp(-(age - attack) / decay);
    }
}
