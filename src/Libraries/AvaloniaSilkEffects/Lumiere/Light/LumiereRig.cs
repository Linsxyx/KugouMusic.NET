using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere.Light;

// Folia lumiere/light/rig.ts. A rig is the declarative light of one shot: beams, fog, the source glare
// and optional caustics / interference. The beam formula exists twice, here and in the light field
// shader, and both must stay identical: glyphs, motes and line art read their brightness from this CPU
// copy while the GPU copy paints the volume, so whatever a beam sweeps over lights up.
//
// Coordinates: inside the light field everything is in "frame height = 1" units, y down; angle 0 points
// right and π/2 points down. Specs give positions as frame fractions (x of width, y of height).

public readonly record struct LumiereOscillation(double Amplitude, double Period, double Phase);

public enum LumiereGoboPattern
{
    None = 0,
    Blinds = 1,
    Cross = 2,
    Lattice = 3,
    Leaves = 4,
}

/// <summary>Window shadow cut into a beam's cross-section (u across −1..1, v along the beam).</summary>
public sealed record LumiereGoboSpec(LumiereGoboPattern Pattern, double Frequency, double Duty, double Drift);

public sealed record LumiereBeamSpec
{
    /// <summary>Source position (frame fractions, may lie outside the frame).</summary>
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>Radians; π/2 points straight down.</summary>
    public double Angle { get; init; }
    /// <summary>Half-angle. Negative converges to a focus and opens again past it (hourglass).</summary>
    public double Spread { get; init; }
    /// <summary>Width at the source (height units).</summary>
    public double Width { get; init; }
    /// <summary>Exponential falloff length along the beam (height units).</summary>
    public double Length { get; init; }
    /// <summary>Edge softness, a fraction of the half width.</summary>
    public double Softness { get; init; }
    public double Intensity { get; init; }
    public double Streaks { get; init; }
    public double StreakFreq { get; init; }
    public double StreakSpeed { get; init; }
    public double Core { get; init; } = 0.5;
    public LumiereOscillation? Sway { get; init; }
    public LumiereOscillation? Pulse { get; init; }
    public Vector3? Tint { get; init; }
    /// <summary>Seconds after the shot starts for the beam to grow from its source to full length.</summary>
    public double Reveal { get; init; }
    public LumiereGoboSpec? Gobo { get; init; }
    public double Spectrum { get; init; }
    /// <summary>Range (height units) after which the beam is cut; 0 keeps going.</summary>
    public double Reach { get; init; }
}

public sealed record LumiereFogSpec(
    double Density,
    double TyndallBase,
    double Scale,
    double DriftX,
    double DriftY,
    double Warp,
    double Ambient);

public sealed record LumiereGlareSpec(double X, double Y, double Radius, double Intensity, double Streak);

public sealed record LumiereCausticFloor(double Cx, double Cy, double Rx, double Ry, double Strength);

public sealed record LumiereCausticSpec(double Scale, double Speed, double InBeam, LumiereCausticFloor? Floor = null);

public enum LumiereWaveMode
{
    Rings = 1,
    Slits = 2,
    Sources = 3,
    Airy = 4,
}

public sealed record LumiereWaveSpec(
    LumiereWaveMode Mode,
    double Cx,
    double Cy,
    double Radius,
    double Frequency,
    double Speed,
    double Strength,
    double Separation = 0,
    double? Shield = null);

public sealed record LumiereLightRig(
    IReadOnlyList<LumiereBeamSpec> Beams,
    LumiereFogSpec Fog,
    LumiereGlareSpec? Glare,
    LumiereCausticSpec? Caustic = null,
    LumiereWaveSpec? Wave = null);

/// <summary>A beam resolved at one instant, in height units.</summary>
public struct LumiereResolvedBeam
{
    public double Ox, Oy, Dx, Dy;
    public double HalfWidth, TanSpread, Softness, Length;
    public double Intensity, Streaks, StreakFreq, StreakPhase, Core;
    public double R, G, B;
    public double GoboPattern, GoboFreq, GoboDuty, GoboPhase;
    public double Spectrum, Reach;
}

public readonly record struct LumiereLightDrive(
    double Intensity,
    double Bass,
    Vector3 Color,
    double? Local = null);

public static class LumiereLight
{
    public const int MaxBeams = 6;
    /// <summary>Light that still leaks through a gobo's shadow (pure black stripes read as a barcode).</summary>
    public const double GoboLeak = 0.12;
    /// <summary>How much a full bass lifts the beams.</summary>
    public const double AudioGain = 0.15;

    /// <summary>Bass → beam gain; the 1.5 power keeps small wobbles still and lets only hits through.</summary>
    public static double AudioLift(double bass) => 1 + AudioGain * Math.Pow(Math.Max(0, bass), 1.5);

    private static double Oscillate(LumiereOscillation? oscillation, double time) => oscillation is { } o
        ? o.Amplitude * Math.Sin((time / Math.Max(o.Period, 0.001) + o.Phase) * Math.PI * 2)
        : 0;

    public static void ResolveBeams(LumiereLightRig rig, double time, double aspect, in LumiereLightDrive drive,
        List<LumiereResolvedBeam> output)
    {
        var count = Math.Min(rig.Beams.Count, MaxBeams);
        for (var index = 0; index < count; index++)
            output.Add(Resolve(rig.Beams[index], time, aspect, drive));
    }

    public static LumiereResolvedBeam Resolve(LumiereBeamSpec beam, double time, double aspect, in LumiereLightDrive drive)
    {
        var angle = beam.Angle + Oscillate(beam.Sway, time);
        var pulse = 1 + Oscillate(beam.Pulse, time);
        var tint = beam.Tint ?? Vector3.One;
        var reveal = beam.Reveal > 0 && drive.Local is { } local
            ? 0.06 + 0.94 * SmoothStep(0, 1, local / beam.Reveal)
            : 1;
        var gobo = beam.Gobo;
        return new LumiereResolvedBeam
        {
            Ox = beam.X * aspect,
            Oy = beam.Y,
            Dx = Math.Cos(angle),
            Dy = Math.Sin(angle),
            HalfWidth = beam.Width / 2,
            TanSpread = Math.Tan(beam.Spread),
            Softness = beam.Softness,
            Length = beam.Length * reveal,
            Intensity = beam.Intensity * pulse * drive.Intensity * AudioLift(drive.Bass),
            Streaks = beam.Streaks,
            StreakFreq = beam.StreakFreq,
            StreakPhase = time * beam.StreakSpeed,
            Core = beam.Core,
            R = tint.X * drive.Color.X,
            G = tint.Y * drive.Color.Y,
            B = tint.Z * drive.Color.Z,
            GoboPattern = gobo is null ? 0 : (int)gobo.Pattern,
            GoboFreq = gobo?.Frequency ?? 0,
            GoboDuty = gobo?.Duty ?? 1,
            GoboPhase = (gobo?.Drift ?? 0) * time,
            Spectrum = beam.Spectrum,
            Reach = beam.Reach,
        };
    }

    // Noise identical to the GLSL side (Dave Hoskins' sin-free hashes).

    private static double Fract(double value) => value - Math.Floor(value);

    public static double Hash11(double input)
    {
        var p = Fract(input * 0.1031);
        p *= p + 33.33;
        p *= p + p;
        return Fract(p);
    }

    public static double ValueNoise1(double x)
    {
        var i = Math.Floor(x);
        var f = x - i;
        var u = f * f * (3 - 2 * f);
        return Hash11(i) * (1 - u) + Hash11(i + 1) * u;
    }

    public static double Hash12(double x, double y)
    {
        var a = Fract(x * 0.1031);
        var b = Fract(y * 0.1031);
        var c = Fract(x * 0.1031);
        var d = a * (b + 33.33) + b * (c + 33.33) + c * (a + 33.33);
        a += d;
        b += d;
        c += d;
        return Fract((a + b) * c);
    }

    public static double ValueNoise2(double x, double y)
    {
        var ix = Math.Floor(x);
        var iy = Math.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var ux = fx * fx * (3 - 2 * fx);
        var uy = fy * fy * (3 - 2 * fy);
        var a = Hash12(ix, iy);
        var b = Hash12(ix + 1, iy);
        var c = Hash12(ix, iy + 1);
        var d = Hash12(ix + 1, iy + 1);
        return (a * (1 - ux) + b * ux) * (1 - uy) + (c * (1 - ux) + d * ux) * uy;
    }

    public static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Min(1, Math.Max(0, (x - edge0) / (edge1 - edge0)));
        return t * t * (3 - 2 * t);
    }

    private static double Band(double x, double duty)
    {
        var f = Fract(x);
        return 1 - SmoothStep(duty - 0.06, duty + 0.06, f) + SmoothStep(1 - 0.06, 1, f);
    }

    public static double GoboMask(double pattern, double frequency, double duty, double phase, double u, double v) =>
        pattern > 0 ? GoboLeak + (1 - GoboLeak) * GoboPattern(pattern, frequency, duty, phase, u, v) : 1;

    private static double GoboPattern(double pattern, double frequency, double duty, double phase, double u, double v) => pattern switch
    {
        1 => Math.Min(1, Band(u * frequency * 0.5 + phase, duty)),
        2 => SmoothStep(duty * 0.5, duty * 0.5 + 0.08, Math.Abs(u)),
        3 => Math.Min(1, Band(u * frequency * 0.5 + v * frequency * 0.8 + phase, duty))
             * Math.Min(1, Band(u * frequency * 0.5 - v * frequency * 0.8 - phase, duty)),
        4 => SmoothStep(duty - 0.12, duty + 0.12, ValueNoise2(u * frequency, v * frequency * 0.6 + phase)),
        _ => 1,
    };

    /// <summary>One beam's strength at (x, y) in height units, before fog and color. Mirrors GLSL beamMask.</summary>
    public static double BeamMask(in LumiereResolvedBeam beam, double x, double y)
    {
        var px = x - beam.Ox;
        var py = y - beam.Oy;
        var along = px * beam.Dx + py * beam.Dy;
        if (along <= 0) return 0;
        var signedPerp = px * -beam.Dy + py * beam.Dx;
        // Absolute value: a converging beam opens again past its focus.
        var half = Math.Abs(beam.HalfWidth + along * beam.TanSpread) + 1e-4;
        var s = Math.Abs(signedPerp) / half;
        var edge = SmoothStep(1, 1 - Math.Max(beam.Softness, 0.02), s);
        if (edge <= 0) return 0;
        var signedS = s * Math.Sign(signedPerp);
        var streak = 1 - beam.Streaks + beam.Streaks * ValueNoise1(signedS * beam.StreakFreq + beam.StreakPhase);
        var core = 1 - beam.Core + beam.Core * (1 - s * s);
        var falloff = Math.Exp(-along / Math.Max(beam.Length, 0.001));
        var gobo = beam.GoboPattern > 0
            ? GoboMask(beam.GoboPattern, beam.GoboFreq, beam.GoboDuty, beam.GoboPhase, signedS, along)
            : 1;
        // The reach tail grows with the beam width: a hard cut on a wide beam reads as a square end.
        var reach = beam.Reach > 0 ? 1 - SmoothStep(beam.Reach - Math.Max(0.04, half * 2.5), beam.Reach, along) : 1;
        // Ease in from the source: nothing lies behind it, and a wide source would leave a hard edge.
        var start = SmoothStep(0, Math.Abs(beam.HalfWidth) * 2 + 0.01, along);
        return edge * streak * core * falloff * gobo * reach * start * beam.Intensity;
    }

    /// <summary>Sum of all beams at (x, y), height units.</summary>
    public static double LightAt(IReadOnlyList<LumiereResolvedBeam> beams, double x, double y)
    {
        var sum = 0.0;
        for (var index = 0; index < beams.Count; index++)
        {
            var beam = beams[index];
            sum += BeamMask(beam, x, y);
        }
        return sum;
    }

    /// <summary>The shader's soft compression on a scalar, so overlaps do not blow out.</summary>
    public static double CompressLight(double value) => 1 - Math.Exp(-value);
}
