using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Folia's buildSonnetCreditsPoster: the end-credits card shown after the final lyric.</summary>
internal static class SonnetCreditsPoster
{
    // Matches the fixed transparent margin EffectTextureCache rasterizes around plain text,
    // so a top-left anchor lands where Pixi's text origin would.
    private const float RasterPadding = 12;

    internal static EffectContainer Build(SonnetTheme theme, SonnetSongMetadata metadata,
        float width, float height, float lyricsFontScale, float rasterScale)
    {
        var root = new EffectContainer();
        var title = metadata.Title?.Trim() ?? "";
        var artist = metadata.Artist?.Trim() ?? "";
        var album = metadata.Album?.Trim() ?? "";
        var left = Math.Max(38, width * 0.105f);
        var right = Math.Max(38, width * 0.09f);
        var contentWidth = Math.Max(220, width - left - right);
        var titleSize = Math.Clamp(width * 0.088f * lyricsFontScale, 42, 118);
        var detailSize = Math.Clamp(width * 0.018f * lyricsFontScale, 13, 25);
        var titleWeight = theme.FontWeight ?? 700;
        var detailWeight = theme.FontWeight ?? 500;

        root.Add(new ShapeNode { Position = new Vector2(left, height * 0.155f),
            Size = new Vector2(Math.Max(42, width * 0.075f), 5), Color = theme.Accent with { A = 0.95f } });
        root.Add(new ShapeNode { Position = new Vector2(left, height * 0.155f),
            Size = new Vector2(2, height * 0.57f), Color = theme.Primary with { A = 0.22f } });
        root.Add(new ShapeNode { Position = new Vector2(width - right - 8, height * 0.225f),
            Size = new Vector2(8, height * 0.34f), Color = theme.Secondary with { A = 0.32f } });
        root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = new Vector2(left, height * 0.79f),
            Size = new Vector2(width - right - left, 0), StrokeWidth = 1, Color = theme.Primary with { A = 0.36f } });

        if (artist.Length > 0)
            AddLines(root, artist.ToUpperInvariant(), theme, detailSize, detailWeight, theme.Accent,
                new Vector2(left + 20, height * 0.205f), contentWidth * 0.72f, detailSize * 1.25f, rasterScale);
        if (title.Length > 0)
            AddLines(root, title, theme, titleSize, titleWeight, theme.Primary,
                new Vector2(left + 16, height * 0.285f), contentWidth * 0.88f,
                titleSize * 1.2f - Math.Max(2, titleSize * 0.08f), rasterScale);
        if (album.Length > 0)
            AddLines(root, $"— {album}", theme, detailSize * 0.92f, detailWeight, theme.Secondary,
                new Vector2(left + 18, height * 0.825f), contentWidth * 0.72f, detailSize * 1.15f, rasterScale);
        return root;
    }

    private static void AddLines(EffectContainer root, string text, SonnetTheme theme, float size, int weight,
        EffectColor color, Vector2 origin, float wrapWidth, float lineHeight, float rasterScale)
    {
        var lines = Wrap(text, wrapWidth, value => EffectTextureCache.MeasureText(value, theme.FontFamily, size, weight).X);
        var padding = RasterPadding / Math.Clamp(rasterScale, 1, 4);
        for (var index = 0; index < lines.Count; index++)
        {
            root.Add(new TextNode
            {
                Text = lines[index], FontFamily = theme.FontFamily, FontSize = size, FontWeight = weight,
                Color = color, RasterScale = rasterScale,
                Position = origin + new Vector2(-padding, index * lineHeight - padding),
            });
        }
    }

    /// <summary>Pixi-style word wrap with breakWords: whole words when they fit, graphemes otherwise.</summary>
    internal static IReadOnlyList<string> Wrap(string text, float maxWidth, Func<string, float> measure)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var token in Tokenize(text))
        {
            var candidate = current + token;
            if (current.Length == 0 || measure(candidate.TrimEnd()) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            lines.Add(current.TrimEnd());
            current = token.TrimStart();
        }
        if (current.Trim().Length > 0) lines.Add(current.TrimEnd());
        return lines;
    }

    // Latin words (with their trailing space) stay together; every other grapheme breaks on its own.
    private static IEnumerable<string> Tokenize(string text)
    {
        var word = new System.Text.StringBuilder();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var isLatin = element.Length == 1 && element[0] < 0x2E80 && !char.IsWhiteSpace(element[0]);
            if (isLatin)
            {
                word.Append(element);
                continue;
            }
            if (char.IsWhiteSpace(element[0]))
            {
                word.Append(element);
                yield return word.ToString();
                word.Clear();
                continue;
            }
            if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
            yield return element;
        }
        if (word.Length > 0) yield return word.ToString();
    }
}
