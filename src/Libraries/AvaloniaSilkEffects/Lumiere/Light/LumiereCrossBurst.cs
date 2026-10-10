using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere.Light;

/// <summary>A glyph that can burst: its index in the line and when it lights.</summary>
public readonly record struct LumiereBurstGlyph(int GlyphIndex, double Start);

public readonly record struct LumiereBurstTrigger(int LineIndex, int GlyphIndex, double Time, double Size, double Dy);

/// <summary>A burst in flight: age, position (logical px), vertical bar length and color.</summary>
public readonly record struct LumiereBurstEvent(double Age, float X, float Y, float Length, Vector3 Color);

/// <summary>
/// Folia lumiere/light/crossBurst.ts: EVA-style little crosses along a line. Seeded glyphs each pop a cross the moment
/// they are sung (or the whole line in one sweep): a white flash, a vertical bar shooting out, a crossbar opening and a
/// small shock ring, gone within 0.6 s. Each frame depends only on the age of each burst.
/// </summary>
internal sealed class LumiereCrossBurst(LumiereSprites sprites)
{
    public const int MaxBursts = 14;
    public const double Duration = 0.6;
    /// <summary>Seconds between neighbouring crosses in a sweep.</summary>
    private const double SweepStep = 0.07;

    /// <summary>Which glyphs burst and when (pure, seeded).</summary>
    public static List<LumiereBurstTrigger> Plan(LumiereBurstSpec spec, IReadOnlyList<IReadOnlyList<LumiereBurstGlyph>> lines, string seed)
    {
        var triggers = new List<LumiereBurstTrigger>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (lineIndex < spec.Offset || (lineIndex - spec.Offset) % Math.Max(1, spec.Every) != 0) continue;
            var glyphs = lines[lineIndex];
            var rng = new LumiereRng($"{seed}:burst:{lineIndex}");
            var chosen = glyphs.Where(_ => rng.Next() < spec.Density).ToList();
            // At least one, even for a sparse density on a short line.
            if (chosen.Count == 0 && glyphs.Count > 0) chosen.Add(glyphs[(int)Math.Floor(rng.Next() * glyphs.Count)]);
            var sweepStart = glyphs.Count > 0 ? glyphs[0].Start : 0;
            for (var order = 0; order < chosen.Count; order++)
                triggers.Add(new LumiereBurstTrigger(lineIndex, chosen[order].GlyphIndex,
                    spec.Mode == LumiereBurstMode.Sweep ? sweepStart + order * SweepStep : chosen[order].Start,
                    0.7 + rng.Next() * 0.6, (rng.Next() - 0.5) * 0.9));
        }
        return [.. triggers.OrderBy(trigger => trigger.Time)];
    }

    /// <summary>How much one cross lifts the light field (the caller caps the sum).</summary>
    public static double LightBoost(double age) => age < 0 ? 0 : 0.18 * Math.Exp(-age / 0.1);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static double EaseOutExpo(double value)
    {
        var t = Clamp01(value);
        return t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    }
    private static double EaseOutCubic(double value) => 1 - Math.Pow(1 - Clamp01(value), 3);

    public void Draw(EffectPrimitiveRenderer primitives, Matrix3x2 root, IReadOnlyList<LumiereBurstEvent> events)
    {
        for (var index = 0; index < Math.Min(events.Count, MaxBursts); index++)
        {
            var e = events[index];
            if (e.Age < 0 || e.Age > Duration) continue;
            var age = e.Age;
            var length = e.Length;
            var glow = LumiereColor.Tint(e.Color);
            var core = LumiereColor.Tint(LumiereColor.Mix(e.Color, LumiereColor.White, 0.75f));
            // 30 ms to light, then a fast decay (0.16 s constant), gone within 0.6 s.
            var envelope = Clamp01(age / 0.03) * Math.Exp(-Math.Max(0, age - 0.03) / 0.16);
            var spread = 1 + age * 2;
            var holder = Matrix3x2.CreateTranslation(e.X, e.Y) * root;
            void Bar(float x, float y, float width, float thickness, float rotation, double alpha, EffectColor tint) =>
                LumiereDraw.Sprite(primitives, sprites.Streak, holder, x, y, width, thickness, rotation, (float)alpha, tint);

            // Pool order: ring, vertical glow, horizontal glow, vertical core, horizontal core, flash.
            var ring = EaseOutCubic(age / 0.4);
            var ringSize = (float)(length * 0.9 * ring);
            LumiereDraw.Sprite(primitives, sprites.Bokeh, holder, 0, -length * 0.42f, ringSize, ringSize, 0,
                (float)(0.35 * (1 - ring) * Clamp01(age / 0.03)), glow);

            // Vertical bar: from 0.3L below the glyph to 0.7L above (a Latin cross), full within 80 ms.
            var vertical = (float)Math.Max(1, length * EaseOutExpo(age / 0.08));
            // Crossbar: 20 ms later, open within 100 ms, crossing the upper part of the bar.
            var horizontal = (float)Math.Max(1, length * 0.62 * EaseOutExpo((age - 0.02) / 0.1));
            var crossIn = Clamp01((age - 0.02) / 0.03);
            Bar(0, -length * 0.2f, vertical, (float)(length * 0.1 * spread), MathF.PI / 2, 0.6 * envelope, glow);
            Bar(0, -length * 0.42f, horizontal, (float)(length * 0.09 * spread), 0, 0.55 * envelope * crossIn, glow);
            Bar(0, -length * 0.2f, vertical, (float)(length * 0.02 * spread), MathF.PI / 2, envelope, core);
            Bar(0, -length * 0.42f, horizontal, (float)(length * 0.018 * spread), 0, 0.95 * envelope * crossIn, core);

            // The ignition flash.
            var flashSize = (float)(length * (0.35 + age * 0.8));
            LumiereDraw.Sprite(primitives, sprites.Dot, holder, 0, -length * 0.42f, flashSize, flashSize, 0,
                (float)Math.Exp(-age / 0.07), core);
        }
    }
}
