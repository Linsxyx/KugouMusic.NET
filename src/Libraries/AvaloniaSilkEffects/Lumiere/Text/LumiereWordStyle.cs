namespace AvaloniaSilkEffects.Lumiere.Text;

/// <summary>
/// Folia text/wordStyle.ts: per-word size differences. Function words and symbols go a size down, the line's key word
/// (the longest content word) a size up, the rest float a little by seed. Lines are laid out word by word.
/// </summary>
public static class LumiereWordStyle
{
    /// <summary>Largest word scale: glyph textures are drawn at this scale, so enlarged words only ever shrink.</summary>
    public const double MaxWordScale = 1.5;

    private static readonly HashSet<string> FunctionWords =
        [.. "的了在把是我你他她它们和与也就都着过吗呢吧啊呀哦被让给从向到这那之而又很".Select(c => c.ToString())];

    private static readonly HashSet<string> MinorLatin =
        ["a", "an", "the", "of", "to", "in", "on", "at", "me", "my", "with", "and", "or", "is", "i", "you", "it", "by", "for"];

    /// <summary>Each word's size factor (one per word), seeded.</summary>
    public static double[] Scales(IReadOnlyList<LumiereWordSpan> words, string seed)
    {
        var rng = new LumiereRng($"{seed}:words");
        static int LengthOf(LumiereWordSpan word) => word.End - word.Start;
        bool Minor(LumiereWordSpan word) => word.Blank
            || LengthOf(word) == 1 && FunctionWords.Contains(word.Text)
            || MinorLatin.Contains(word.Text.ToLowerInvariant());
        // Key word: the longest content word; ties go to the later one (line ends read as landing points).
        var key = -1;
        for (var index = 0; index < words.Count; index++)
        {
            if (Minor(words[index])) continue;
            if (key < 0 || LengthOf(words[index]) >= LengthOf(words[key])) key = index;
        }
        var scales = new double[words.Count];
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var jitter = rng.Next();
            // Whitespace keeps its width (shrinking would crowd Latin words); only punctuation and symbols shrink.
            if (word.Blank && string.IsNullOrWhiteSpace(word.Text)) scales[index] = 1;
            else if (index == key && words.Count > 1) scales[index] = 1.45;
            else if (Minor(word)) scales[index] = 0.62;
            else scales[index] = 0.85 + jitter * 0.35;
        }
        return scales;
    }

    /// <summary>Per-word offset across the line (font sizes): the key word stays, small words jump more.</summary>
    public static double[] Jags(IReadOnlyList<LumiereWordSpan> words, IReadOnlyList<double> scales, string seed)
    {
        var rng = new LumiereRng($"{seed}:jags");
        var keyScale = scales.Count > 0 ? scales.Max() : double.NegativeInfinity;
        var jags = new double[words.Count];
        for (var index = 0; index < words.Count; index++)
        {
            var offset = (rng.Next() - 0.5) * 2;
            var scale = index < scales.Count ? scales[index] : 1;
            jags[index] = scale == keyScale && words.Count > 1 ? 0 : offset * (scale < 0.7 ? 0.32 : 0.2);
        }
        return jags;
    }
}
