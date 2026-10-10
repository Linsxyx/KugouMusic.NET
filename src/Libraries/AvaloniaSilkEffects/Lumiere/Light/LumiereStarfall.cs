using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere.Light;

/// <summary>
/// Folia lumiere/light/starfall.ts: the shot opens dark; hundreds of points pour down from above the frame (with
/// trails, slowing before they land), flash once on landing, then stay as a twinkling starfield (dense at the top).
/// The main shaft ignites halfway through (the unit drives that). After the opening a sparse light rain falls near
/// the shaft, brighter inside beams. All closed-form in (seed, t).
/// </summary>
internal sealed class LumiereStarfall
{
    private readonly LumiereStarfallSpec _spec;
    private readonly float _height;
    private readonly EffectTexture _dot;
    private readonly EffectTexture _star;
    private readonly EffectTexture _streak;
    private readonly Star[] _stars;
    private readonly Drop[] _drops;

    private readonly record struct Star(double X, double Y, double FromY, double Delay, double Fall, double Size, bool Bright,
        double TwinklePhase, double TwinkleFreq);

    private readonly record struct Drop(double X, double Period, double Phase, double Size, double Sway);

    public LumiereStarfall(float width, float height, string seed, LumiereStarfallSpec spec, LumiereSprites sprites)
    {
        _spec = spec;
        _height = height;
        _dot = sprites.Dot;
        _star = sprites.Star;
        _streak = sprites.Streak;
        var aspect = width / (double)height;
        var rng = new LumiereRng($"{seed}:starfall");
        _stars = new Star[spec.Stars];
        for (var index = 0; index < _stars.Length; index++)
        {
            var bright = rng.Next() < 0.08;
            _stars[index] = new Star(
                rng.Next() * aspect,
                // Dense at the top, sparse below.
                0.02 + Math.Pow(rng.Next(), 1.7) * 0.9,
                -0.05 - rng.Next() * 0.25,
                // Most land early in the opening, a few trail into the end.
                spec.Opening * (0.02 + 0.7 * Math.Pow(rng.Next(), 1.4)),
                0.45 + rng.Next() * 0.7,
                bright ? 0.018 + rng.Next() * 0.02 : 0.003 + rng.Next() * 0.006,
                bright,
                rng.Next() * Math.PI * 2,
                0.4 + rng.Next() * 1.8);
        }
        _drops = new Drop[spec.Rain];
        for (var index = 0; index < _drops.Length; index++)
        {
            // Across: concentrated around the center line (the shaft), roughly normal.
            var spread = (rng.Next() + rng.Next() + rng.Next() - 1.5) / 1.5;
            _drops[index] = new Drop(
                aspect * (0.5 + spread * spec.RainSpread * 0.5),
                1.25 / Math.Max(spec.RainSpeed, 0.01) * (0.7 + rng.Next() * 0.6),
                rng.Next(),
                0.003 + rng.Next() * 0.005,
                (rng.Next() - 0.5) * 0.02);
        }
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static double EaseOutCubic(double value) => 1 - Math.Pow(1 - Clamp01(value), 3);

    /// <param name="local">Seconds since the shot started (the opening runs on it).</param>
    /// <param name="time">Absolute time (twinkle, rain).</param>
    public void Draw(EffectPrimitiveRenderer primitives, Matrix3x2 root, double local, double time,
        IReadOnlyList<LumiereResolvedBeam> beams, Vector3 color, double fade)
    {
        var tint = LumiereColor.Tint(color);
        // Folia keeps trails in a container under the points: draw every visible trail first, then the points.
        for (var pass = 0; pass < 2; pass++)
        {
            var trails = pass == 0;
            foreach (var star in _stars)
            {
                var progress = (local - star.Delay) / star.Fall;
                if (progress <= 0) continue;
                var y = star.FromY + (star.Y - star.FromY) * EaseOutCubic(progress);
                // Fall speed (height units / s): the derivative of easeOutCubic.
                var speed = progress < 1 ? (star.Y - star.FromY) * 3 * Math.Pow(1 - progress, 2) / star.Fall : 0;
                var landedAt = star.Delay + star.Fall;
                var flash = local >= landedAt ? Math.Exp(-(local - landedAt) / 0.25) : 0;
                var twinkle = 0.55 + 0.45 * Math.Sin(time * star.TwinkleFreq * Math.PI * 2 + star.TwinklePhase);
                var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, star.X, y));
                var baseline = progress < 1 ? 0.9 : (star.Bright ? 0.7 : 0.45) * twinkle;
                var alpha = Clamp01((baseline + lit * 0.8 + flash) * _spec.Brightness * fade);
                Place(primitives, root, trails, star.Bright ? _star : _dot, star.X, y, star.Size * (1 + flash * 1.5), alpha, speed, tint);
            }
            // Rain starts after the opening, fading in.
            var rainIn = Clamp01((local - _spec.Opening * 0.6) / 1.5);
            foreach (var drop in _drops)
            {
                var cycle = (time / drop.Period + drop.Phase) % 1;
                if (cycle < 0) cycle += 1;
                var y = -0.1 + cycle * 1.25;
                var x = drop.X + Math.Sin(time * 0.7 + drop.Phase * 6) * drop.Sway;
                var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, x, y));
                var edge = Clamp01(cycle / 0.08) * Clamp01((1 - cycle) / 0.15);
                var alpha = Clamp01((0.18 + lit * 1.1) * edge * rainIn * fade * _spec.Brightness);
                Place(primitives, root, trails, _dot, x, y, drop.Size, alpha, 1.25 / drop.Period, tint);
            }
        }
    }

    private void Place(EffectPrimitiveRenderer primitives, Matrix3x2 root, bool trail, EffectTexture texture,
        double x, double y, double size, double alpha, double speed, EffectColor tint)
    {
        if (alpha <= 0.004) return;
        var px = (float)(x * _height);
        var py = (float)(y * _height);
        if (!trail)
        {
            var side = (float)(size * _height);
            LumiereDraw.Sprite(primitives, texture, root, px, py, side, side, 0, (float)alpha, tint);
            return;
        }
        if (speed <= 0.05) return;
        // Trail length follows speed and points up (anchor at the tail end = the point; rotated a quarter turn).
        LumiereDraw.Sprite(primitives, _streak, root, px, py,
            (float)(Math.Min(0.25, speed * 0.09) * _height), MathF.Max(1.5f, (float)(size * _height * 0.7)),
            MathF.PI / 2, (float)(alpha * 0.6), tint, 1, 0.5f);
    }
}
