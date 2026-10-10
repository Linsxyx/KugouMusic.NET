using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/wave.ts — 衍射族 (interference and diffraction): fringes, rings, moiré, iridescence.</summary>
public static class LumiereWaveRigs
{
    private const string Family = "wave";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay) with { Starfall = Starfall with { Rain = 40 } };

    private static LumiereLineArtSpec WaveSparks(LumiereRigContext c) => ScatteredSparks(c.Aspect, 12, c.Random, 0.3);

    /// <summary>双缝 (n): top light through two slits; bright and dark fringes on the screen below.</summary>
    public static LumiereProfile Slits { get; } = Make("wave-slits", "双缝", LumiereMood.Neutral,
        Rig([Shaft with { Spread = 0.05, Width = 0.04, Length = 0.6, Intensity = 0.9 }], fog => fog with { Density = 0.9 }, Glare,
            wave: new LumiereWaveSpec(LumiereWaveMode.Slits, 0.5, 0.72, 0.3, 5, 0.4, 0.55)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(
            SlitBarrier(c.Aspect * 0.5, 0.3, c.Aspect * 0.5, 0.012, 0.08),
            ViewfinderFrame(c.Aspect, 0.2, 0.52, 0.8, 0.92, 0.2, 0.28),
            WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.72, 0.5),
    };

    /// <summary>牛顿环 (q): concentric bright and dark rings, the text at their center.</summary>
    public static LumiereProfile Newton { get; } = Make("wave-newton", "牛顿环", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.06, Width = 0.04, Length = 0.6, Intensity = 0.6 }], fog => fog with { Density = 0.7 }, Glare with { Intensity = 0.5 },
            wave: new LumiereWaveSpec(LumiereWaveMode.Rings, 0.5, 0.5, 0.4, 5, 0.3, 0.5, Shield: 0.75)),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.Rings(c.Aspect * 0.5, 0.5, 0.4, 6), WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.4, 0.62),
    };

    /// <summary>艾里斑 (q): a point source's diffraction spot with faint outer rings.</summary>
    public static LumiereProfile Airy { get; } = Make("wave-airy", "艾里斑", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.5, Y = -0.05, Spread = 0.02, Width = 0.01, Length = 0.6, Intensity = 0.5, Reach = 0.4 }], fog => fog with { Density = 0.6 },
            new LumiereGlareSpec(0.5, 0.4, 0.04, 1, 0.4), wave: new LumiereWaveSpec(LumiereWaveMode.Airy, 0.5, 0.4, 0.3, 3, 0, 0.7)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.Rings(c.Aspect * 0.5, 0.4, 0.3, 4, new LumiereStyle(Alpha: 0.3)), WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.72, 0.72, 0.36),
    };

    /// <summary>光栅 (n): a beam hits a grating and splits into diffraction orders with colored edges.</summary>
    public static LumiereProfile Grating { get; } = Make("wave-grating", "光栅", LumiereMood.Neutral,
        Rig([
            Shaft with { Spread = 0.01, Width = 0.03, Length = 2, Intensity = 0.9, Reach = 0.32, Sway = null },
            .. new[] { -2, -1, 0, 1, 2 }.Select(k => Shaft with
            {
                Y = 0.3, Angle = Math.PI / 2 + k * 0.32, Spread = 0.02, Width = 0.02, Length = 1.2, Intensity = 0.8 - Math.Abs(k) * 0.15,
                Streaks = 0.2, Core = 0.6, Spectrum = k == 0 ? 0 : 0.8, Sway = null,
            }),
        ], fog => fog with { Density = 0.9 }, Glare with { Y = 0.3, Radius = 0.05, Intensity = 0.9, Streak = 0.6 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Grating(c.Aspect * 0.5, 0.3, c.Aspect * 0.4, 16), WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.7, 0.72, 0.38),
    };

    /// <summary>波叠 (l): two point sources' waves interfere in hyperbolic fringes.</summary>
    public static LumiereProfile Sources { get; } = Make("wave-sources", "波叠", LumiereMood.Loud,
        Rig([Shaft with { Spread = 0.15, Intensity = 0.4 }], fog => fog with { Density = 0.8 }, Glare with { Intensity = 0.5 },
            wave: new LumiereWaveSpec(LumiereWaveMode.Sources, 0.5, 0.5, 0.46, 4, 1.2, 0.6, 0.14)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.TwoSources(c.Aspect * 0.5, 0.5, 0.14, 0.3), WaveSparks(c)),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.3, 2, 0, 3.2, new Vector3(0.85f, 0.9f, 1)),
    };

    /// <summary>莫尔 (n): two fine gratings slightly rotated make slowly flowing moiré light.</summary>
    public static LumiereProfile Moire { get; } = Make("wave-moire", "莫尔", LumiereMood.Neutral,
        Rig([
            Shaft with { Spread = 0.2, Width = 0.3, Length = 1.3, Softness = 0.6, Intensity = 0.6, Streaks = 0, Core = 0.2, Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 22, 0.5, 0.02), Sway = null },
            Shaft with { Angle = Math.PI / 2 + 0.06, Spread = 0.2, Width = 0.3, Length = 1.3, Softness = 0.6, Intensity = 0.6, Streaks = 0, Core = 0.2, Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 23, 0.5, -0.02), Sway = new LumiereOscillation(0.03, 14, 0) },
        ], fog => fog with { Density = 0.8 }, Glare with { Intensity = 0.5 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Moire(c.Aspect * 0.5, 0.32, 0.4, 24, 0.08), WaveSparks(c)),
    };

    /// <summary>薄膜 (q): flowing iridescence like a soap bubble.</summary>
    public static LumiereProfile Film { get; } = Make("wave-film", "薄膜", LumiereMood.Quiet,
        Rig([Shaft with
        {
            Spread = 0.4, Width = 0.3, Length = 1.2, Softness = 0.95, Intensity = 0.45, Streaks = 0.6, StreakFreq = 3, StreakSpeed = 0.2, Core = 0.2, Spectrum = 1,
            Sway = new LumiereOscillation(0.15, 13, 0),
        }], fog => fog with { Density = 0.7 }, noGlare: true, wave: new LumiereWaveSpec(LumiereWaveMode.Rings, 0.5, 0.4, 0.3, 1.6, 0.6, 0.25)),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(
            new LumiereLineArtSpec([
                D.Circle(c.Aspect * 0.5, 0.4, 0.3, new LumiereStyle(0.0016, 0.45)),
                D.Circle(c.Aspect * 0.5 - 0.08, 0.3, 0.06, new LumiereStyle(0.001, 0.3, 0.2)),
            ], []),
            WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.4, 0.62),
    };

    /// <summary>偏振 (n): two polarizers; the light through them breathes bright and dark as one turns.</summary>
    public static LumiereProfile Polar { get; } = Make("wave-polar", "偏振", LumiereMood.Neutral,
        c =>
        {
            var second = c.Aspect * 0.5 + 0.12;
            return Rig([
                Shaft with { X = -0.02, Y = 0.36, Angle = 0, Spread = 0.02, Width = 0.14, Length = 3, Intensity = 0.8, Streaks = 0.2, Core = 0.4, Reach = second + 0.02 * c.Aspect, Sway = null },
                Shaft with { X = second / c.Aspect, Y = 0.36, Angle = 0, Spread = 0.02, Width = 0.14, Length = 3, Intensity = 0.7, Streaks = 0.2, Core = 0.4, Sway = null, Pulse = new LumiereOscillation(0.9, 6, 0) },
            ], fog => fog with { Density = 0.8 }, noGlare: true)(c);
        },
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Polarizers(c.Aspect * 0.5, 0.36, 0.1, 0.24, Math.PI / 3), WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.7, 0.72, 0.36),
    };

    /// <summary>驻波 (l): standing-wave lines above the text, vibrating with the beat.</summary>
    public static LumiereProfile Standing { get; } = Make("wave-standing", "驻波", LumiereMood.Loud,
        Rig([.. new[] { 0.26, 0.34, 0.42 }.Select((y, i) => Shaft with
        {
            X = -0.02, Y = y, Angle = 0, Spread = 0.004, Width = 0.01, Length = 3, Intensity = 0.7, Streaks = 0.6, StreakFreq = 4, Core = 0.8, Sway = null,
            Pulse = new LumiereOscillation(0.6, 1.2, i / 3.0),
        })], fog => fog with { Density = 0.9 }, noGlare: true),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.StandingWave(c.Aspect * 0.08, c.Aspect * 0.92, 0.34, 0.07, 6), WaveSparks(c)),
    };

    /// <summary>全息 (l): the text like a hologram, fine diagonal fringes and flowing iridescence.</summary>
    public static LumiereProfile Holo { get; } = Make("wave-holo", "全息", LumiereMood.Loud,
        Rig([Shaft with
        {
            Spread = 0.3, Width = 0.3, Length = 1.3, Softness = 0.8, Intensity = 0.8, Streaks = 0.3, Core = 0.3, Spectrum = 0.8,
            Gobo = new LumiereGoboSpec(LumiereGoboPattern.Lattice, 24, 0.7, 0.15), Sway = new LumiereOscillation(0.1, 10, 0),
        }], fog => fog with { Density = 0.8 }, Glare with { Intensity = 0.6 }, wave: new LumiereWaveSpec(LumiereWaveMode.Rings, 0.5, 0.5, 0.5, 8, 1, 0.15)),
        LumiereTypography.Vertical, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.GridPanel(c.Aspect * 0.3, 0.15, c.Aspect * 0.4, 0.7, 6, 8, true, new LumiereStyle(Alpha: 0.3)), WaveSparks(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.4, 0.62),
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Slits, Newton, Airy, Grating, Sources, Moire, Film, Polar, Standing, Holo];
}
