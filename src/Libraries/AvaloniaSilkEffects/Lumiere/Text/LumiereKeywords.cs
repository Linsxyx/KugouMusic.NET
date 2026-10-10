using System.Numerics;
using System.Text.RegularExpressions;

namespace AvaloniaSilkEffects.Lumiere.Text;

/// <summary>A theme keyword and its color (Folia theme.wordColors).</summary>
public sealed record LumiereWordColor(string Word, EffectColor Color);

/// <summary>A prepared keyword: CJK phrases match by containment, English by whole words.</summary>
internal sealed record LumiereKeywordMatcher(Vector3 Color, string[] CjkPhrases, string[] EnglishWords, int Priority);

/// <summary>A keyword's colors under one light color: glyph body, halo and sparkle.</summary>
internal sealed record LumiereKeywordTints(Vector3 Glyph, Vector3 Halo, Vector3 Star);

/// <summary>
/// Folia text/keywordColors.ts + visualizer/wordColoring.ts: keyword coloring. Keywords match by character ranges (CJK phrases by
/// containment, English by whole word), so 「花火」 never stains the 「火」 of 「火车」; overlapping ranges keep the higher
/// priority (longer) keyword. Each line matches once at build time into per-grapheme colors; per frame only the mix with
/// the light color runs. The keyword hue is lifted to full brightness and mixed with the light, then scaled back to the
/// light's brightness: the glyph still reads as lit, just tinted. Unsung glyphs stay cool and are not colored.
/// </summary>
internal static partial class LumiereKeywords
{
    // Keyword share (the rest is light): glyph body, halo, sparkle, cross burst, background fragment.
    public const float GlyphMix = 0.6f;
    public const float HaloMix = 0.75f;
    public const float StarMix = 0.55f;
    public const float BurstMix = 0.7f;
    public const float EchoMix = 0.3f;
    /// <summary>White mixed into a keyword sparkle (plain glyphs use 0.5): tinted, but still a bright star.</summary>
    public const float StarWhite = 0.35f;
    /// <summary>Keyword halos glow a little more so the color shows.</summary>
    public const float HaloGain = 1.25f;

    [GeneratedRegex(@"[一-龥぀-ヿ가-힯]")]
    private static partial Regex Cjk();

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[A-Za-z0-9_]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Normalize(string text) => NonWord().Replace(text.ToLowerInvariant(), "");

    public static IReadOnlyList<LumiereKeywordMatcher> Prepare(IReadOnlyList<LumiereWordColor>? wordColors, bool enabled)
    {
        if (!enabled || wordColors is null || wordColors.Count == 0) return [];
        var matchers = new List<LumiereKeywordMatcher>();
        foreach (var entry in wordColors)
        {
            var target = entry.Word?.Trim() ?? "";
            if (target.Length == 0) continue;
            var color = new Vector3(entry.Color.R, entry.Color.G, entry.Color.B);
            if (Cjk().IsMatch(target))
            {
                matchers.Add(new LumiereKeywordMatcher(color, [target], [], target.Length));
                continue;
            }
            var words = Whitespace().Split(target).Select(Normalize).Where(word => word.Length > 0).ToArray();
            if (words.Length == 0) continue;
            matchers.Add(new LumiereKeywordMatcher(color, [], words, words.Max(word => word.Length)));
        }
        return matchers;
    }

    private readonly record struct Range(int Start, int End, Vector3 Color, int Priority);

    private static bool Overlap(int aStart, int aEnd, int bStart, int bEnd) => aStart < bEnd && bStart < aEnd;

    private static List<Range> Ranges(string text, IReadOnlyList<LumiereKeywordMatcher> matchers)
    {
        var ranges = new List<Range>();
        var english = new Dictionary<string, (Vector3 Color, int Priority)>();
        foreach (var matcher in matchers)
        {
            foreach (var target in matcher.CjkPhrases)
            {
                var cursor = 0;
                while (cursor < text.Length)
                {
                    var start = text.IndexOf(target, cursor, StringComparison.Ordinal);
                    if (start < 0) break;
                    ranges.Add(new Range(start, start + target.Length, matcher.Color, matcher.Priority));
                    cursor = start + Math.Max(target.Length, 1);
                }
            }
            foreach (var word in matcher.EnglishWords)
                if (!english.TryGetValue(word, out var current) || matcher.Priority > current.Priority)
                    english[word] = (matcher.Color, matcher.Priority);
        }
        if (english.Count > 0)
            foreach (Match match in Word().Matches(text))
                if (english.TryGetValue(Normalize(match.Value), out var color))
                    ranges.Add(new Range(match.Index, match.Index + match.Length, color.Color, color.Priority));

        // Higher priority first, then position; keep only non-overlapping ranges.
        var selected = new List<Range>();
        foreach (var range in ranges.OrderByDescending(r => r.Priority).ThenBy(r => r.Start).ThenBy(r => r.End))
            if (!selected.Any(current => Overlap(current.Start, current.End, range.Start, range.End)))
                selected.Add(range);
        return [.. selected.OrderBy(r => r.Start).ThenBy(r => r.End)];
    }

    /// <summary>Each grapheme's keyword color (null when not a keyword); graphemes split like the glyph line.</summary>
    public static Vector3?[] GlyphColors(string text, IReadOnlyList<LumiereKeywordMatcher> matchers)
    {
        var graphemes = LumiereReveal.Graphemes(text);
        var colors = new Vector3?[graphemes.Count];
        if (matchers.Count == 0 || graphemes.Count == 0) return colors;
        var ranges = Ranges(text, matchers);
        if (ranges.Count == 0) return colors;
        var offset = 0;
        var rangeIndex = 0;
        for (var index = 0; index < graphemes.Count; index++)
        {
            var start = offset;
            var end = offset + graphemes[index].Length;
            offset = end;
            if (string.IsNullOrWhiteSpace(graphemes[index])) continue;
            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= start) rangeIndex++;
            if (rangeIndex < ranges.Count && Overlap(ranges[rangeIndex].Start, ranges[rangeIndex].End, start, end))
                colors[index] = ranges[rangeIndex].Color;
        }
        return colors;
    }

    /// <summary>Keyword hue (lifted to full brightness) mixed with the light, scaled back to the light's brightness.</summary>
    public static Vector3 Light(Vector3 light, Vector3 keyword, float amount)
    {
        var keywordPeak = MathF.Max(keyword.X, MathF.Max(keyword.Y, keyword.Z));
        var hue = keywordPeak > 1e-4f ? keyword / keywordPeak : Vector3.One;
        var mixed = LumiereColor.Mix(light, hue, amount);
        var peak = MathF.Max(mixed.X, MathF.Max(mixed.Y, mixed.Z));
        var target = MathF.Max(light.X, MathF.Max(light.Y, light.Z));
        return peak > 1e-4f ? mixed * (target / peak) : light;
    }

    public static LumiereKeywordTints Tints(Vector3 light, Vector3 keyword) => new(
        Light(light, keyword, GlyphMix),
        Light(light, keyword, HaloMix),
        LumiereColor.Mix(Light(light, keyword, StarMix), Vector3.One, StarWhite));

    /// <summary>A cross burst landing on a keyword: mostly the keyword light, keeping some of the rig's tint.</summary>
    public static Vector3 BurstColor(Vector3 burstColor, Vector3 light, Vector3 keyword) =>
        LumiereColor.Mix(burstColor, Light(light, keyword, BurstMix), 0.8f);

    /// <summary>Keywords in background fragments: only a touch of color.</summary>
    public static Vector3 EchoColor(Vector3 light, Vector3 keyword) => Light(light, keyword, EchoMix);
}
