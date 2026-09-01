using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

// Exact port of Folia v0.7.2 sonnetGlyphLayout.ts.
public static class SonnetGlyphLayout
{
    public static double ResolveMotionDuration(double startTime, double endTime)
    {
        var shotDuration = Math.Max(0.001, endTime - startTime);
        var preferred = Math.Min(1.8, Math.Max(0.65, shotDuration * 0.42));
        return Math.Min(preferred, shotDuration * 0.72);
    }

    public static IReadOnlyList<SonnetGlyphPlacement> Build(
        SonnetSemanticSegment segment,
        SonnetTypographyPlacement placement,
        float fontSize,
        Func<string, float> measureGlyph,
        double motionStartTime,
        double motionEndTime)
    {
        IReadOnlyList<SonnetGraphemeTiming> graphemes;
        if (segment.Graphemes.Count > 0)
        {
            graphemes = segment.Graphemes;
        }
        else
        {
            var fallbackChars = segment.Text.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
            graphemes = fallbackChars.Select((text, index) => new SonnetGraphemeTiming(
                text,
                segment.StartTime + (segment.EndTime - segment.StartTime) * index / Math.Max(1, fallbackChars.Length),
                segment.StartTime + (segment.EndTime - segment.StartTime) * (index + 1) / Math.Max(1, fallbackChars.Length)))
                .ToArray();
        }

        var advances = graphemes.Select(item => placement.Vertical
            ? fontSize * 0.9f
            : Math.Max(fontSize * 0.2f, measureGlyph(item.Text))).ToArray();
        var cursor = -advances.Sum() / 2;
        var motionDuration = ResolveMotionDuration(motionStartTime, motionEndTime);
        var cosine = MathF.Cos(placement.Rotation);
        var sine = MathF.Sin(placement.Rotation);
        var output = new List<SonnetGlyphPlacement>(graphemes.Count);

        for (var index = 0; index < graphemes.Count; index++)
        {
            var advance = advances[index];
            var localX = placement.Vertical ? 0 : cursor + advance / 2;
            var localY = placement.Vertical ? cursor + advance / 2 : 0;
            cursor += advance;
            var stagger = index % 2 == 0 ? -1 : 1;
            var position = new Vector2(
                placement.X + localX * cosine - localY * sine,
                placement.Y + localX * sine + localY * cosine);
            var entrance = new Vector2(
                placement.EnterX + (placement.Vertical ? stagger * fontSize * 0.28f : 0),
                placement.EnterY + (placement.Vertical ? 0 : stagger * fontSize * 0.24f));
            var startTime = graphemes[index].StartTime;
            output.Add(new SonnetGlyphPlacement(
                graphemes[index].Text,
                position,
                entrance,
                stagger * (SonnetTypographyRoles.IsEmphasis(placement.Role) ? 0.055f : 0.035f),
                startTime,
                Math.Max(startTime, startTime + motionDuration)));
        }

        return output;
    }
}
