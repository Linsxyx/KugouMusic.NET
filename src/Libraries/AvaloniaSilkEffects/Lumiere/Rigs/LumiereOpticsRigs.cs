using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using AvaloniaSilkEffects.Lumiere.Text;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>
/// Folia rigs/optics.ts — 光路族 (optical diagrams): each segment of a ray diagram is a thin reach-limited beam, and the
/// line art draws the same path with lenses, mirrors and foci. Blueprint-like; the text mostly sits below.
/// </summary>
public static class LumiereOpticsRigs
{
    private const string Family = "optics";
    private const double Axis = 0.4;

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, Motes with { Count = 180 })
            with { Region = new LumiereRegion(0.5, 0.74, 0.72, 0.3), HeroSize = 0.075 };

    /// <summary>A ray from a to b (frame fractions): a thin beam whose reach ends exactly at b (open: keeps going).</summary>
    private static LumiereBeamSpec Ray(double aspect, (double X, double Y) a, (double X, double Y) b, bool open = false)
    {
        var dx = (b.X - a.X) * aspect;
        var dy = b.Y - a.Y;
        return new LumiereBeamSpec
        {
            X = a.X, Y = a.Y, Angle = Math.Atan2(dy, dx), Spread = 0.004, Width = 0.008, Length = 3, Softness = 0.6,
            Intensity = 0.9, Streaks = 0.1, StreakFreq = 3, StreakSpeed = 0.05, Core = 0.8,
            Reach = open ? 0 : Math.Sqrt(dx * dx + dy * dy) + 0.02,
        };
    }

    /// <summary>The same path as line art (frame fractions → height units).</summary>
    private static LumiereLineArtSpec RayArt(double aspect, (double X, double Y)[] points, double delay = 0.2) =>
        D.RayPath([.. points.Select(p => new LumierePoint(p.X * aspect, p.Y))], new LumiereStyle(Delay: delay));

    private static LumiereLightRig OpticsRig(IEnumerable<LumiereBeamSpec> beams, LumiereGlareSpec? glare = null) =>
        new([.. beams], Fog with { Density = 0.9, TyndallBase = 0.2, Ambient = 0.02 }, glare);

    private static LumiereLineArtSpec OpticsSparks(LumiereRigContext c) => ScatteredSparks(c.Aspect, 10, c.Random, 0.3, (0.05, 0.05, 0.95, 0.6));

    /// <summary>会聚 (n): parallel light converges through a convex lens to the focus.</summary>
    public static LumiereProfile Convex { get; } = Make("optics-convex", "会聚", LumiereMood.Neutral,
        c => OpticsRig(new[] { 0.3, 0.4, 0.5 }.SelectMany(y => new[] { Ray(c.Aspect, (-0.02, y), (0.42, y)), Ray(c.Aspect, (0.42, y), (0.7, Axis), true) }),
            new LumiereGlareSpec(0.7, Axis, 0.05, 0.9, 0.6)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge([
            D.Lens(c.Aspect * 0.42, Axis, 0.3, true, c.Aspect),
            .. new[] { 0.3, 0.4, 0.5 }.Select((y, i) => RayArt(c.Aspect, [(0.02, y), (0.42, y), (0.7, Axis), (0.92, Axis + (Axis - y) * 0.8)], 0.2 + i * 0.08)),
            D.FocusMark(c.Aspect * 0.7, Axis, new LumiereStyle(Delay: 0.5)),
            OpticsSparks(c),
        ]),
    };

    /// <summary>发散 (q): a concave lens spreads parallel light; the virtual focus sits in front (dashed).</summary>
    public static LumiereProfile Concave { get; } = Make("optics-concave", "发散", LumiereMood.Quiet,
        c => OpticsRig(new[] { 0.32, 0.4, 0.48 }.SelectMany(y => new[]
        {
            Ray(c.Aspect, (-0.02, y), (0.42, y)) with { Intensity = 0.7 },
            Ray(c.Aspect, (0.42, y), (0.42 + (0.42 - 0.26), y + (y - Axis) * 1.6), true) with { Intensity = 0.7, Spread = 0.01 },
        })),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.7, 1.4)) with
    {
        LineArt = c => D.Merge([
            D.Lens(c.Aspect * 0.42, Axis, 0.28, false, c.Aspect),
            .. new[] { 0.32, 0.48 }.Select((y, i) => D.Merge(
                RayArt(c.Aspect, [(0.02, y), (0.42, y), (0.62, y + (y - Axis) * 2.2)], 0.2 + i * 0.1),
                new LumiereLineArtSpec([D.Segment(P(c.Aspect * 0.42, y), P(c.Aspect * 0.26, Axis), new LumiereStyle(0.0009, 0.35, 0.5, Dash: (0.005, 0.006)))], []))),
            D.FocusMark(c.Aspect * 0.26, Axis, new LumiereStyle(Delay: 0.6)),
            OpticsSparks(c),
        ]),
        Region = new LumiereRegion(0.72, 0.56, 0.4, 0.72),
    };

    /// <summary>反射 (q): a ray hits a flat mirror; incidence equals reflection, with the normal and angle marks.</summary>
    public static LumiereProfile Mirror { get; } = Make("optics-mirror", "反射", LumiereMood.Quiet,
        c => OpticsRig([
            Ray(c.Aspect, (0.12, 0.08), (0.5, 0.6)) with { Width = 0.02, Intensity = 1 },
            Ray(c.Aspect, (0.5, 0.6), (0.88, 0.08), true) with { Width = 0.02, Intensity = 0.8 },
        ], new LumiereGlareSpec(0.5, 0.6, 0.05, 0.8, 0.7)),
        LumiereTypography.Crossed, new LumiereDecaySpec(0.7, 1.4)) with
    {
        LineArt = c => D.Merge(D.Mirror(c.Aspect * 0.5, 0.6, 0.6, 0), RayArt(c.Aspect, [(0.12, 0.08), (0.5, 0.6), (0.88, 0.08)]), OpticsSparks(c)),
        Region = new LumiereRegion(0.5, 0.34, 0.5, 0.36),
        HeroSize = 0.07,
    };

    /// <summary>折射 (n): a ray bends towards the normal at a medium interface.</summary>
    public static LumiereProfile Refract { get; } = Make("optics-refract", "折射", LumiereMood.Neutral,
        c => OpticsRig([
            Ray(c.Aspect, (0.18, 0.05), (0.46, 0.5)) with { Width = 0.02, Intensity = 1 },
            Ray(c.Aspect, (0.46, 0.5), (0.58, 1.05), true) with { Width = 0.02, Intensity = 0.8 },
            Ray(c.Aspect, (0.46, 0.5), (0.74, 0.05), true) with { Width = 0.01, Intensity = 0.3 },
        ], new LumiereGlareSpec(0.46, 0.5, 0.05, 0.8, 0.8)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(
            D.InterfaceLine(c.Aspect, 0.5),
            RayArt(c.Aspect, [(0.18, 0.05), (0.46, 0.5), (0.58, 0.98)]),
            new LumiereLineArtSpec([D.Segment(P(c.Aspect * 0.46, 0.18), P(c.Aspect * 0.46, 0.82), new LumiereStyle(0.0009, 0.35, 0.4, Dash: (0.006, 0.006)))], []),
            OpticsSparks(c)),
        Region = new LumiereRegion(0.72, 0.3, 0.44, 0.3),
        HeroSize = 0.07,
    };

    /// <summary>光具座 (n): an optical bench with lenses and a ruler; one beam passes through them in turn.</summary>
    public static LumiereProfile Bench { get; } = Make("optics-bench", "光具座", LumiereMood.Neutral,
        c => OpticsRig([
            Ray(c.Aspect, (-0.02, Axis), (0.3, Axis)) with { Width = 0.05, Spread = 0.01, Intensity = 0.8 },
            Ray(c.Aspect, (0.3, Axis), (0.5, Axis)) with { Width = 0.05, Intensity = 0.9, Spread = -0.06 },
            Ray(c.Aspect, (0.5, Axis), (0.7, Axis)) with { Width = 0.02, Intensity = 0.9, Spread = 0.06 },
            Ray(c.Aspect, (0.7, Axis), (1.02, Axis), true) with { Width = 0.05, Intensity = 0.7 },
        ], new LumiereGlareSpec(0.5, Axis, 0.04, 0.7, 0.6)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge([
            .. new[] { 0.3, 0.5, 0.7 }.Select((x, i) => D.Lens(c.Aspect * x, Axis, 0.16 + i % 2 * 0.04, i != 1, c.Aspect, new LumiereStyle(Delay: i * 0.1))),
            D.Ruler(c.Aspect * 0.06, c.Aspect * 0.94, 0.58, 40, new LumiereStyle(Delay: 0.3)),
            OpticsSparks(c),
        ]),
    };

    /// <summary>潜望 (n): the ray bounces between two mirrors, from the top to the bottom.</summary>
    public static LumiereProfile Periscope { get; } = Make("optics-periscope", "潜望", LumiereMood.Neutral,
        c => OpticsRig([
            Ray(c.Aspect, (1.02, 0.14), (0.3, 0.14)) with { Width = 0.02 },
            Ray(c.Aspect, (0.3, 0.14), (0.3, 0.84)) with { Width = 0.02 },
            Ray(c.Aspect, (0.3, 0.84), (1.02, 0.84), true) with { Width = 0.02, Intensity = 0.8 },
        ], new LumiereGlareSpec(0.3, 0.14, 0.04, 0.7, 0.4)),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.8, 1.2)) with
    {
        LineArt = c => D.Merge(
            D.Mirror(c.Aspect * 0.3, 0.14, 0.14, Math.PI / 4),
            D.Mirror(c.Aspect * 0.3, 0.84, 0.14, Math.PI / 4, new LumiereStyle(Delay: 0.1)),
            RayArt(c.Aspect, [(0.98, 0.14), (0.3, 0.14), (0.3, 0.84), (0.98, 0.84)]),
            OpticsSparks(c)),
        Region = new LumiereRegion(0.62, 0.49, 0.4, 0.5),
    };

    /// <summary>光纤 (l): light travels a curved fiber by total reflection and sprays out as a cone.</summary>
    public static LumiereProfile Fiber { get; } = Make("optics-fiber", "光纤", LumiereMood.Loud,
        _ => OpticsRig([
            new LumiereBeamSpec
            {
                X = 0.84, Y = 0.2, Angle = -0.5, Spread = 0.3, Width = 0.02, Length = 0.9, Softness = 0.6, Intensity = 1.2, Streaks = 0.5, StreakFreq = 9,
                StreakSpeed = 0.1, Core = 0.6, Pulse = new LumiereOscillation(0.2, 2.3, 0),
            },
            new LumiereBeamSpec
            {
                X = 0.84, Y = 0.2, Angle = -0.5, Spread = 0.08, Width = 0.02, Length = 1.2, Softness = 0.5, Intensity = 0.9, Streaks = 0.2, StreakFreq = 3,
                StreakSpeed = 0, Core = 0.8,
            },
        ], new LumiereGlareSpec(0.84, 0.2, 0.07, 1.2, 0.8)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(
            D.Fiber(P(c.Aspect * 0.04, 0.86), P(c.Aspect * 0.4, 0.95), P(c.Aspect * 0.5, 0.1), P(c.Aspect * 0.84, 0.2), 0.012),
            OpticsSparks(c)),
        Region = new LumiereRegion(0.46, 0.52, 0.6, 0.5),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.3, 2, 0, 3.2, new Vector3(0.8f, 0.9f, 1)),
        Starfall = new LumiereStarfallSpec(380, 3, 60, 0.3, 0.8, 1),
    };

    /// <summary>暗箱 (q): light enters a pinhole box (converging through the hole, then opening); an inverted image inside.</summary>
    public static LumiereProfile Pinhole { get; } = Make("optics-pinhole", "暗箱", LumiereMood.Quiet,
        c =>
        {
            var distance = (0.45 + 0.02) * c.Aspect;
            return OpticsRig([new LumiereBeamSpec
            {
                X = -0.02, Y = Axis, Angle = 0, Spread = -Math.Atan(0.15 / distance), Width = 0.3, Length = 3, Softness = 0.4, Intensity = 0.8,
                Streaks = 0.3, StreakFreq = 6, StreakSpeed = 0.05, Core = 0.3, Reach = distance + 0.3,
            }], new LumiereGlareSpec(0.45, Axis, 0.03, 1, 0.3));
        },
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.PinholeBox(c.Aspect * 0.45 + 0.15, Axis, 0.3, 0.3), OpticsSparks(c)),
        Region = new LumiereRegion(0.2, 0.52, 0.3, 0.7),
        HeroSize = 0.07,
    };

    /// <summary>望远 (l): the objective focuses parallel light, the eyepiece turns it into a narrower parallel beam.</summary>
    public static LumiereProfile Telescope { get; } = Make("optics-telescope", "望远", LumiereMood.Loud,
        c => OpticsRig(new[] { 0.3, 0.5 }.SelectMany(y => new[]
        {
            Ray(c.Aspect, (-0.02, y), (0.28, y)) with { Width = 0.02 },
            Ray(c.Aspect, (0.28, y), (0.62, Axis)),
            Ray(c.Aspect, (0.72, Axis + (y - Axis) * 0.3), (1.02, Axis + (y - Axis) * 0.3), true) with { Width = 0.02, Intensity = 1.1 },
        }), new LumiereGlareSpec(0.62, Axis, 0.05, 1, 0.8)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => D.Merge([
            D.Lens(c.Aspect * 0.28, Axis, 0.36, true, c.Aspect),
            D.Lens(c.Aspect * 0.72, Axis, 0.12, true, c.Aspect, new LumiereStyle(Delay: 0.1)),
            .. new[] { 0.3, 0.5 }.Select((y, i) => RayArt(c.Aspect,
                [(0.02, y), (0.28, y), (0.62, Axis), (0.72, Axis - (y - Axis) * 0.3), (0.98, Axis - (y - Axis) * 0.3)], 0.2 + i * 0.1)),
            D.FocusMark(c.Aspect * 0.62, Axis, new LumiereStyle(Delay: 0.5)),
            OpticsSparks(c),
        ]),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.25, 2, 1, 3.2, new Vector3(1, 0.8f, 0.55f)),
    };

    /// <summary>追迹 (l): several rays refracting back and forth across the frame, weaving a net.</summary>
    public static LumiereProfile Raytrace { get; } = Make("optics-raytrace", "追迹", LumiereMood.Loud,
        c =>
        {
            var points = Enumerable.Range(0, 7).Select(i => (X: 0.05 + i / 6.0 * 0.9, Y: 0.1 + c.Random.Next() * 0.55)).ToArray();
            return OpticsRig(points.Take(6).Select((point, i) => Ray(c.Aspect, point, points[i + 1]) with { Intensity = 0.8, Width = 0.012 }),
                new LumiereGlareSpec(points[0].X, points[0].Y, 0.05, 0.9, 0.6));
        },
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c =>
        {
            var art = new List<LumiereLineArtSpec>();
            for (var k = 0; k < 4; k++)
            {
                var points = Enumerable.Range(0, 6).Select(i => (X: 0.05 + i / 5.0 * 0.9, Y: 0.08 + c.Random.Next() * 0.6)).ToArray();
                art.Add(RayArt(c.Aspect, points, 0.1 + k * 0.1));
            }
            art.Add(ScatteredSparks(c.Aspect, 16, c.Random, 0.3));
            return D.Merge([.. art]);
        },
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Convex, Concave, Mirror, Refract, Bench, Periscope, Fiber, Pinhole, Telescope, Raytrace];
}
