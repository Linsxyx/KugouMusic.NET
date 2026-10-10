using AvaloniaSilkEffects.Sonnet;

namespace AvaloniaSilkEffects.Lumiere.Text;

/// <summary>A word of a line: grapheme range [Start, End); whitespace, punctuation and symbols are blank.</summary>
public readonly record struct LumiereWordSpan(string Text, int Start, int End, bool Blank);

/// <summary>
/// Folia text/wordStyle.ts segmentWords. Folia goes through Intl.Segmenter (ICU dictionary words, isWordLike);
/// here Sonnet's tokenizer does the same job: dictionary words for Chinese, a model for Japanese, scanner runs
/// for Latin, with whitespace, punctuation and symbols marked as not word-like.
/// </summary>
public static class LumiereWords
{
    public static IReadOnlyList<LumiereWordSpan> Segment(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var line = new SonnetLine(text, 0, 0, []);
        var elements = SonnetTokenizer.Elements(text);
        var tokens = SonnetTokenizer.Tokenize(line, elements, SonnetTokenizer.SongLanguage([line]));
        return [.. tokens.Select(token => new LumiereWordSpan(token.Word.Text, token.Start, token.End, !token.IsWord))];
    }
}
