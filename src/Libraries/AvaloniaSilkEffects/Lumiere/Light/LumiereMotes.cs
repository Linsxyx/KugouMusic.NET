using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere.Light;

public sealed record LumiereMotesSpec(
    int Count,
    // Sprite diameter range (height units).
    double SizeMin,
    double SizeMax,
    // Drift (height units / second).
    double DriftX,
    double DriftY,
    // Swirl amplitude (height units) and period (seconds).
    double Swirl,
    double SwirlPeriod,
    double Twinkle,
    // Brightness outside the beams.
    double Ambient,
    // Gain inside the beams.
    double Gain);

/// <summary>
/// Folia lumiere/light/motes.ts: dust and foreground bokeh. Positions are closed-form in (seed, t) — base +
/// drift·t + two sines of swirl, wrapped around the frame — so any t renders without history. Brightness
/// comes from the CPU light field: a mote shines inside a beam and goes dark outside it.
/// </summary>
internal sealed class LumiereMotes
{
    private const double Margin = 0.08;

    private readonly LumiereMotesSpec _spec;
    private readonly EffectTexture _texture;
    private readonly double _aspect;
    private readonly float _height;
    private readonly Mote[] _motes;

    private readonly record struct Mote(
        double Bx, double By, double Depth, double Size,
        double PhaseA, double PhaseB, double FreqA, double FreqB,
        double TwinklePhase, double TwinkleFreq);

    public LumiereMotes(float width, float height, string seed, LumiereMotesSpec spec, EffectTexture texture)
    {
        _spec = spec;
        _texture = texture;
        _aspect = width / (double)height;
        _height = height;
        var rng = new LumiereRng(seed);
        _motes = new Mote[Math.Max(0, spec.Count)];
        for (var index = 0; index < _motes.Length; index++)
        {
            var depth = rng.Next();
            _motes[index] = new Mote(
                rng.Next() * (_aspect + Margin * 2) - Margin,
                rng.Next() * (1 + Margin * 2) - Margin,
                depth,
                spec.SizeMin + (spec.SizeMax - spec.SizeMin) * depth * depth,
                rng.Next() * Math.PI * 2,
                rng.Next() * Math.PI * 2,
                0.6 + rng.Next() * 0.8,
                0.5 + rng.Next() * 0.9,
                rng.Next() * Math.PI * 2,
                0.8 + rng.Next() * 2.2);
        }
    }

    private static double Wrap(double value, double min, double max)
    {
        var span = max - min;
        return min + ((value - min) % span + span) % span;
    }

    /// <param name="root">Stage logical pixels → target pixels.</param>
    public void Draw(EffectPrimitiveRenderer primitives, Matrix3x2 root, double time,
        IReadOnlyList<LumiereResolvedBeam> beams, Vector3 color, double intensity)
    {
        var spec = _spec;
        var tint = LumiereColor.Tint(color);
        var omega = Math.PI * 2 / Math.Max(spec.SwirlPeriod, 0.1);
        foreach (var mote in _motes)
        {
            var speed = 0.45 + mote.Depth * 0.9;
            var swirl = spec.Swirl * (0.5 + mote.Depth);
            var x = Wrap(mote.Bx + spec.DriftX * speed * time + swirl * Math.Sin(time * omega * mote.FreqA + mote.PhaseA),
                -Margin, _aspect + Margin);
            var y = Wrap(mote.By + spec.DriftY * speed * time + swirl * Math.Cos(time * omega * mote.FreqB + mote.PhaseB),
                -Margin, 1 + Margin);
            var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, x, y)) * spec.Gain;
            var twinkle = 1 - spec.Twinkle + spec.Twinkle *
                (0.5 + 0.5 * Math.Sin(time * mote.TwinkleFreq * Math.PI * 2 + mote.TwinklePhase));
            var alpha = Math.Min(1, (lit + spec.Ambient) * twinkle * intensity);
            if (alpha <= 0.004) continue;
            var px = (float)(mote.Size * _height);
            LumiereDraw.Sprite(primitives, _texture, root, (float)(x * _height), (float)(y * _height), px, px, 0,
                (float)alpha, tint);
        }
    }
}
