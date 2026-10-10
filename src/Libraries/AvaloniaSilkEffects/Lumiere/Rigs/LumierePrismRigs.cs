using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/prism.ts — 棱镜族 (dispersion): white light split by prisms, shards and droplets.</summary>
public static class LumierePrismRigs
{
    private const string Family = "prism";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay) with { Starfall = Starfall with { Rain = 50 } };

    private static LumiereLineArtSpec PrismSparks(LumiereRigContext c) => ScatteredSparks(c.Aspect, 16, c.Random, 0.3);

    /// <summary>A thin beam in one spectral color (spectral lines, combining).</summary>
    private static LumiereBeamSpec Tinted(Vector3 tint) => Shaft with
    {
        Spread = 0.012, Width = 0.012, Length = 1.4, Intensity = 0.7, Streaks = 0.2, StreakFreq = 3, Core = 0.7, Sway = null, Tint = tint,
    };

    /// <summary>分光 (l): white light enters a prism from the left and leaves as a spectrum fan.</summary>
    public static LumiereProfile Split { get; } = Make("prism-split", "分光", LumiereMood.Loud,
        Rig([
            Shaft with { X = -0.02, Y = 0.36, Angle = 0.08, Spread = 0.01, Width = 0.02, Length = 3, Intensity = 0.9, Streaks = 0.2, Sway = null, Reach = 0.78 },
            Shaft with
            {
                X = 0.42, Y = 0.4, Angle = 0.3, Spread = 0.2, Width = 0.02, Length = 1.1, Softness = 0.4,
                Intensity = 1.1, Streaks = 0.35, StreakFreq = 9, Core = 0.2, Spectrum = 1, Sway = new LumiereOscillation(0.03, 11, 0),
            },
        ], fog => fog with { Density = 0.9 }, Glare with { X = 0.42, Y = 0.4, Radius = 0.06, Intensity = 1, Streak = 0.3 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(PrismTriangle(c.Aspect * 0.42, 0.42, 0.2, alpha: 0.8), PrismSparks(c)),
        Region = new LumiereRegion(0.62, 0.66, 0.6, 0.44),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.3, 2, 1, 3.4, new Vector3(0.8f, 0.9f, 1)),
    };

    /// <summary>光谱带 (n): a continuous spectrum band across the frame, the text on it.</summary>
    public static LumiereProfile Band { get; } = Make("prism-band", "光谱带", LumiereMood.Neutral,
        Rig([Shaft with
        {
            X = -0.05, Y = 0.4, Angle = 0.12, Spread = 0.05, Width = 0.16, Length = 2.5, Softness = 0.4, Intensity = 0.9, Streaks = 0.25, StreakFreq = 6,
            Core = 0.3, Spectrum = 1, Sway = new LumiereOscillation(0.02, 14, 0),
        }], fog => fog with { Density = 0.8 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with { Region = new LumiereRegion(0.5, 0.56, 0.72, 0.46) };

    /// <summary>虹边 (q): a top beam with a slight rainbow fringe.</summary>
    public static LumiereProfile Fringe { get; } = Make("prism-fringe", "虹边", LumiereMood.Quiet,
        Rig([
            Shaft with { Spread = 0.1, Softness = 0.3, Spectrum = 0.35, Intensity = 0.9 },
            Shaft with { Spread = 0.3, Length = 0.6, Softness = 0.9, Intensity = 0.25, Streaks = 0.8, StreakFreq = 24, Core = 0.4, Spectrum = 0.5 },
        ]),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.7, 1.4)) with { Region = new LumiereRegion(0.5, 0.5, 0.72, 0.62) };

    /// <summary>碎晶 (l): scattered shards each refract a small spectrum.</summary>
    public static LumiereProfile Shard { get; } = Make("prism-shard", "碎晶", LumiereMood.Loud,
        c => new LumiereLightRig(
            [.. Enumerable.Range(0, 5).Select(i =>
            {
                var x = 0.15 + c.Random.Next() * 0.7;
                var y = 0.15 + c.Random.Next() * 0.5;
                var angle = c.Random.Next() * Math.PI * 2;
                var reach = 0.5 + c.Random.Next() * 0.3;
                var phase = c.Random.Next();
                return Shaft with
                {
                    X = x, Y = y, Angle = angle, Spread = 0.12, Width = 0.01, Length = 0.5, Softness = 0.5, Intensity = 0.8, Streaks = 0.3,
                    StreakFreq = 6, Core = 0.3, Spectrum = 1, Reach = reach, Sway = new LumiereOscillation(0.1, 9 + i, phase),
                };
            })],
            new LumiereFogSpec(0.9, 0.12, 2.4, 0.008, -0.03, 0.9, 0.03), null),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Shards(c.Aspect, c.Random, 8), ScatteredSparks(c.Aspect, 18, c.Random, 0.3)),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sweep, 0.4, 3, 0, 3, new Vector3(0.85f, 0.9f, 1)),
    };

    /// <summary>谱线 (q): a row of vertical emission lines in the dark, the text as their labels.</summary>
    public static LumiereProfile Lines { get; } = Make("prism-lines", "谱线", LumiereMood.Quiet,
        Rig([
            Tinted(new Vector3(1, 0.35f, 0.3f)) with { X = 0.22, Y = -0.05, Angle = Math.PI / 2 },
            Tinted(new Vector3(1, 0.8f, 0.3f)) with { X = 0.36, Y = -0.05, Angle = Math.PI / 2, Intensity = 0.5 },
            Tinted(new Vector3(0.5f, 1, 0.5f)) with { X = 0.47, Y = -0.05, Angle = Math.PI / 2, Intensity = 0.8 },
            Tinted(new Vector3(0.4f, 0.8f, 1)) with { X = 0.61, Y = -0.05, Angle = Math.PI / 2, Intensity = 0.6 },
            Tinted(new Vector3(0.6f, 0.45f, 1)) with { X = 0.78, Y = -0.05, Angle = Math.PI / 2, Intensity = 0.45 },
        ], fog => fog with { Density = 0.7, Ambient = 0.015 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.SpectrumLines(c.Aspect * 0.12, c.Aspect * 0.88, 0.84, 0.16, c.Random, 18), ScatteredSparks(c.Aspect, 10, c.Random, 0.3)),
        Region = new LumiereRegion(0.5, 0.48, 0.72, 0.6),
    };

    /// <summary>合光 (n): red, green and blue beams meet and continue as one white beam.</summary>
    public static LumiereProfile Merge { get; } = Make("prism-merge", "合光", LumiereMood.Neutral,
        Rig([
            Tinted(new Vector3(1, 0.35f, 0.3f)) with { X = -0.02, Y = 0.18, Angle = 0.47, Reach = 0.72, Intensity = 0.8 },
            Tinted(new Vector3(0.45f, 1, 0.45f)) with { X = -0.02, Y = 0.4, Angle = 0.1, Reach = 0.62, Intensity = 0.8 },
            Tinted(new Vector3(0.4f, 0.6f, 1)) with { X = -0.02, Y = 0.66, Angle = -0.29, Reach = 0.68, Intensity = 0.8 },
            Shaft with { X = 0.36, Y = 0.46, Angle = 0.02, Spread = 0.02, Width = 0.02, Length = 2, Intensity = 1.1, Streaks = 0.2, StreakFreq = 3, Core = 0.7, Sway = null },
        ], fog => fog with { Density = 0.9 }, Glare with { X = 0.36, Y = 0.46, Radius = 0.06, Intensity = 1.1, Streak = 0.5 }),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(PrismTriangle(c.Aspect * 0.34, 0.46, 0.12, alpha: 0.6), PrismSparks(c)),
    };

    /// <summary>分束 (n): a beam enters a cube splitter; one path goes straight, one turns down.</summary>
    public static LumiereProfile Cube { get; } = Make("prism-cube", "分束", LumiereMood.Neutral,
        // Reach is in height units while x is a width fraction: the incoming beam must reach the splitter's center.
        c => Rig([
            Shaft with { X = -0.02, Y = 0.34, Angle = 0, Spread = 0.01, Width = 0.03, Length = 3, Intensity = 0.9, Streaks = 0.2, Sway = null, Reach = (0.42 + 0.02) * c.Aspect },
            Shaft with { X = 0.42, Y = 0.34, Angle = 0, Spread = 0.01, Width = 0.03, Length = 3, Intensity = 0.7, Streaks = 0.2, Sway = null, Spectrum = 0.3 },
            Shaft with { X = 0.42, Y = 0.34, Angle = Math.PI / 2, Spread = 0.01, Width = 0.03, Length = 3, Intensity = 0.6, Streaks = 0.2, Sway = null, Spectrum = 0.3 },
        ], fog => fog with { Density = 0.9 }, Glare with { X = 0.42, Y = 0.34, Radius = 0.05, Intensity = 0.9, Streak = 0.4 })(c),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.CubeSplitter(c.Aspect * 0.42, 0.34, 0.08), ScatteredSparks(c.Aspect, 12, c.Random, 0.3)),
        Region = new LumiereRegion(0.6, 0.62, 0.6, 0.44),
    };

    /// <summary>虹扇 (l): dispersed fans slowly turning around the source.</summary>
    public static LumiereProfile Wheel { get; } = Make("prism-wheel", "虹扇", LumiereMood.Loud,
        Rig([.. new[] { 0, 1, 2 }.Select(k => Shaft with
        {
            X = 0.5, Y = 0.08, Angle = Math.PI / 2 + (k - 1) * 0.7, Spread = 0.18, Width = 0.01, Length = 1.1, Softness = 0.4, Intensity = 0.8,
            Streaks = 0.3, StreakFreq = 8, Core = 0.2, Spectrum = 1, Sway = new LumiereOscillation(0.5, 16, k / 3.0),
        })], fog => fog with { Density = 0.9 }, Glare with { Y = 0.08, Radius = 0.1, Intensity = 1.1, Streak = 0.4 }),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(PrismTriangle(c.Aspect * 0.5, 0.1, 0.1, alpha: 0.6), PrismSparks(c)),
    };

    /// <summary>虹霓 (q): after rain, a faint rainbow and its secondary.</summary>
    public static LumiereProfile Droplet { get; } = Make("prism-droplet", "虹霓", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.5, Y = 1.2, Angle = -Math.PI / 2, Spread = 0.55, Width = 0.1, Length = 1.1, Softness = 0.25, Intensity = 0.55, Streaks = 0.1, Core = 0, Spectrum = 1, Sway = null }],
            fog => fog with { Density = 0.7, DriftY = 0.02 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.RainbowArcs(c.Aspect * 0.5, 1.02, 0.62), ScatteredSparks(c.Aspect, 12, c.Random, 0.3)),
        Starfall = Starfall with { Rain = 160, RainSpeed = 0.5, RainSpread = 1.6, Brightness = 0.8 },
        Region = new LumiereRegion(0.5, 0.46, 0.72, 0.6),
    };

    /// <summary>扫谱 (n): a spectrum band scans along the line.</summary>
    public static LumiereProfile SweepBand { get; } = Make("prism-sweep", "扫谱", LumiereMood.Neutral,
        Rig([Shaft with
        {
            X = 0.5, Y = -0.1, Angle = Math.PI / 2, Spread = 0.08, Width = 0.1, Length = 1.4, Softness = 0.4, Intensity = 0.95, Streaks = 0.2, Core = 0.3,
            Spectrum = 1, Sway = new LumiereOscillation(0.35, 8, 0),
        }], fog => fog with { Density = 0.9 }, Glare with { Y = -0.05, Intensity = 0.6 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1));

    public static IReadOnlyList<LumiereProfile> All { get; } = [Split, Band, Fringe, Shard, Lines, Merge, Cube, Wheel, Droplet, SweepBand];
}
