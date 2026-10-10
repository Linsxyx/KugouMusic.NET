using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/botany.ts — 叶脉族 (photosynthesis): leaves, veins, canopy, flowers and vines grow as line art in the light.</summary>
public static class LumiereBotanyRigs
{
    private const string Family = "botany";

    /// <summary>萌芽 (q): the first L0 profile (the reference image's bottom): a sprout under a narrow shaft.</summary>
    public static LumiereProfile Sprout { get; } = new()
    {
        Kind = "botany-sprout",
        Label = "萌芽",
        Family = Family,
        Mood = LumiereMood.Quiet,
        Light = _ => new LumiereLightRig(
            [
                new LumiereBeamSpec
                {
                    X = 0.5, Y = -0.05, Angle = Math.PI / 2, Spread = 0.075, Width = 0.07, Length = 1.2, Softness = 0.7,
                    Intensity = 0.95, Streaks = 0.5, StreakFreq = 6, StreakSpeed = 0.06, Core = 0.7, Sway = new LumiereOscillation(0.008, 15, 0.2),
                },
                new LumiereBeamSpec
                {
                    X = 0.5, Y = -0.05, Angle = Math.PI / 2, Spread = 0.26, Length = 0.75, Softness = 0.95,
                    Intensity = 0.42, Streaks = 0.85, StreakFreq = 30, StreakSpeed = 0.04, Core = 0.3,
                },
            ],
            new LumiereFogSpec(0.9, 0.14, 2, -0.006, -0.03, 1.1, 0.025),
            new LumiereGlareSpec(0.5, -0.01, 0.1, 0.9, 0.35)),
        Motes = new LumiereMotesSpec(200, 0.004, 0.011, -0.003, -0.012, 0.018, 11, 0.55, 0.012, 1.2),
        Front = new LumiereMotesSpec(6, 0.05, 0.12, -0.004, -0.002, 0.01, 16, 0.2, 0.015, 0.25),
        LineArt = c => LumiereLineArtSpec.Merge(
            LumiereRecipes.Sprout(c.Aspect * 0.5, 0.78, 0.22, alpha: 1),
            ProtractorHalo(c.Aspect * 0.5, 0.02, 0.22, 0.2, 0.35),
            ViewfinderFrame(c.Aspect, 0.07, 0.22, 0.93, 0.8, 0.25, 0.22),
            ScatteredSparks(c.Aspect, 12, c.Random, 0.35, (0.2, 0.3, 0.8, 0.9))),
        // Mostly vertical: the current line stands in the shaft like a stem growing from the leaves.
        Region = new LumiereRegion(0.5, 0.44, 0.72, 0.6),
        HeroSize = 0.08,
        // A narrow shaft: smaller background fragments.
        EchoSize = 0.28,
        Typography = LumiereTypography.Vertical,
        Decay = new LumiereDecaySpec(0.75, 1.2),
        Starfall = new LumiereStarfallSpec(300, 3.6, 50, 0.16, 0.25, 0.85),
        Camera = new LumiereCameraSpec(0.03, 0, 0.004),
    };

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, Motes with { Count = 220, DriftY = -0.012 })
            // Leaves, ferns, flowers and vines are this family's protagonists.
            with { ArtGain = 1.7, Starfall = Starfall with { Rain = 40 } };

    private static LumiereLineArtSpec BotanySparks(LumiereRigContext c) => ScatteredSparks(c.Aspect, 12, c.Random, 0.3);

    /// <summary>叶脉 (n): a big leaf across the frame; slanted light lights its veins in stretches.</summary>
    public static LumiereProfile Veins { get; } = Make("botany-veins", "叶脉", LumiereMood.Neutral,
        Rig([Shaft with { X = 0.15, Y = -0.05, Angle = 1.1, Spread = 0.12, Width = 0.12, Length = 1.4, Intensity = 0.9, Streaks = 0.5, StreakFreq = 6 }],
            fog => fog with { Density = 0.9 }, Glare with { X = 0.15, Y = -0.02, Radius = 0.14, Intensity = 0.8 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.BigLeaf(c.Aspect * 0.52, 0.42, 0.62, -0.45), BotanySparks(c)),
        Region = new LumiereRegion(0.5, 0.72, 0.72, 0.36),
    };

    /// <summary>林冠 (l): looking up into a canopy, light shafts through the leaves.</summary>
    public static LumiereProfile Canopy { get; } = Make("botany-canopy", "林冠", LumiereMood.Loud,
        Rig([.. new[] { 0.25, 0.48, 0.7 }.Select((x, i) => Shaft with
        {
            X = x, Y = -0.05, Angle = 1.45 + i * 0.1, Spread = 0.08, Width = 0.08, Length = 1.3, Softness = 0.6, Intensity = 0.85, Streaks = 0.5, StreakFreq = 6,
            Gobo = new LumiereGoboSpec(LumiereGoboPattern.Leaves, 9, 0.5, 0.14), Sway = new LumiereOscillation(0.03, 6 + i, i * 0.3),
        })], fog => fog with { Density = 1.1, DriftX = 0.008 }, noGlare: true),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => D.Merge(D.Canopy(c.Aspect, c.Random), ScatteredSparks(c.Aspect, 16, c.Random, 0.3)),
        Motes = Motes with { Count = 300, DriftX = 0.008, DriftY = 0.006, Swirl = 0.03 },
        Starfall = Starfall with { Rain = 90, RainSpread = 1.2 },
    };

    /// <summary>蕨卷 (n): a curled fern frond, slowly unfurling.</summary>
    public static LumiereProfile Fern { get; } = Make("botany-fern", "蕨卷", LumiereMood.Neutral,
        Rig([Shaft with { X = 0.3, Spread = 0.08, Intensity = 0.85 }, Fan with { X = 0.3, Intensity = 0.2 }]),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.8, 1.2)) with
    {
        LineArt = c => D.Merge(D.FernCurl(c.Aspect * 0.28, 0.42, 0.16), BotanySparks(c)),
        Region = new LumiereRegion(0.62, 0.5, 0.5, 0.62),
    };

    /// <summary>种子 (q): a beam falls on a seed, roots growing down.</summary>
    public static LumiereProfile Seed { get; } = Make("botany-seed", "种子", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.04, Width = 0.02, Length = 1.2, Intensity = 0.8, Core = 0.9 }], fog => fog with { Density = 0.7, Ambient = 0.02 },
            Glare with { Intensity = 0.6 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.SeedRoots(c.Aspect * 0.5, 0.7, c.Random), ScatteredSparks(c.Aspect, 10, c.Random, 0.3)),
        Region = new LumiereRegion(0.5, 0.38, 0.72, 0.4),
    };

    /// <summary>绽放 (l): petals open one by one around the light point.</summary>
    public static LumiereProfile Bloom { get; } = Make("botany-bloom", "绽放", LumiereMood.Loud,
        Rig([.. Enumerable.Range(0, 6).Select(k => Shaft with
        {
            X = 0.5, Y = 0.42, Angle = k / 6.0 * Math.PI * 2 + 0.26, Spread = 0.12, Width = 0.01, Length = 0.6, Softness = 0.7, Intensity = 0.55,
            Streaks = 0.5, StreakFreq = 7, Core = 0.4, Reach = 0.45, Sway = new LumiereOscillation(0.05, 10, k / 6.0),
        })], fog => fog with { Density = 0.9 }, new LumiereGlareSpec(0.5, 0.42, 0.1, 1.2, 0.4)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => D.Merge(D.Bloom(c.Aspect * 0.5, 0.42, 0.2, 8), BotanySparks(c)),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sweep, 0.5, 3, 1, 3, new Vector3(1, 0.8f, 0.7f)),
    };

    /// <summary>藤旋 (n): a vine spiralling up towards the light.</summary>
    public static LumiereProfile Vine { get; } = Make("botany-vine", "藤旋", LumiereMood.Neutral,
        Rig([Shaft with { X = 0.72, Spread = 0.07, Intensity = 0.9 }, Fan with { X = 0.72, Intensity = 0.2 }], glare: Glare with { X = 0.72 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Vine(c.Aspect * 0.72, 1.02, 0.08, 0.05, 4), BotanySparks(c)),
        Region = new LumiereRegion(0.36, 0.5, 0.5, 0.62),
    };

    /// <summary>叶绿 (q): a microscope field; chloroplasts glow in the light.</summary>
    public static LumiereProfile Chloro { get; } = Make("botany-chloro", "叶绿", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.2, Width = 0.3, Softness = 0.9, Intensity = 0.6, Core = 0.3 }], fog => fog with { Density = 0.7 }, noGlare: true,
            caustic: new LumiereCausticSpec(6, 0.12, 0.3, new LumiereCausticFloor(0.5, 0.45, 0.2, 0.36, 0.35))),
        LumiereTypography.Crossed, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.Cells(c.Aspect * 0.5, 0.45, 0.36, c.Random, 22), ScatteredSparks(c.Aspect, 8, c.Random, 0.3)),
        Region = new LumiereRegion(0.5, 0.5, 0.5, 0.5),
    };

    /// <summary>光合式 (n): molecular line art and a protractor halo, after the reference image's formula.</summary>
    public static LumiereProfile Formula { get; } = Make("botany-formula", "光合式", LumiereMood.Neutral,
        Rig([Shaft, Fan]),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(
            D.Molecule(c.Aspect * 0.24, 0.3, 0.06),
            D.Molecule(c.Aspect * 0.78, 0.74, 0.05, new LumiereStyle(Delay: 0.2)),
            ProtractorHalo(c.Aspect * 0.5, 0.03, 0.28, alpha: 0.5),
            BotanySparks(c)),
    };

    /// <summary>花粉 (l): big pollen grains drifting in slanted light.</summary>
    public static LumiereProfile Pollen { get; } = Make("botany-pollen", "花粉", LumiereMood.Loud,
        Rig([
            Shaft with { X = 0.2, Y = -0.05, Angle = 1.15, Spread = 0.14, Width = 0.16, Length = 1.5, Intensity = 1, Streaks = 0.5 },
            Fan with { X = 0.2, Angle = 1.15, Intensity = 0.3 },
        ], fog => fog with { Density = 1, DriftX = 0.012, DriftY = -0.01 }, Glare with { X = 0.2, Y = 0.0 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = BotanySparks,
        Motes = new LumiereMotesSpec(360, 0.006, 0.022, 0.012, -0.006, 0.04, 10, 0.4, 0.02, 1.6),
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Sprout, Veins, Canopy, Fern, Seed, Bloom, Vine, Chloro, Formula, Pollen];
}
