using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia lumiere/rigs/zenith.ts — 天光族 (top light, the reference image's key look). Line art comes later.</summary>
public static class LumiereZenithRigs
{
    private const string Family = "zenith";

    /// <summary>天井 (n): one top shaft, smoke rising in it.</summary>
    public static LumiereProfile Shaft { get; } = Profile(Family, "zenith-shaft", "天井", LumiereMood.Neutral,
        Rig([
            LumiereRigBase.Shaft,
            Fan,
            Fan with { Spread = 0.55, Length = 0.42, Softness = 1, Intensity = 0.16, Streaks = 0.9, StreakFreq = 40, StreakSpeed = -0.03, Core = 0.2, Sway = null },
        ]),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with { Starfall = Starfall };

    /// <summary>扇光 (l): five fanned thin beams that slowly open and close.</summary>
    public static LumiereProfile FanLight { get; } = Profile(Family, "zenith-fan", "扇光", LumiereMood.Loud,
        Rig([.. new[] { -2, -1, 0, 1, 2 }.Select(k => LumiereRigBase.Shaft with
        {
            Angle = Down + k * 0.24, Spread = 0.035, Width = 0.012, Length = 0.9, Intensity = 0.7 - Math.Abs(k) * 0.08,
            Streaks = 0.4, StreakFreq = 5, Sway = new LumiereOscillation(0.05, 11, k * 0.1),
        })]),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with { Starfall = Starfall with { Rain = 110, RainSpread = 0.6 } };

    /// <summary>圣环 (n): shaft + a large protractor halo, a bigger source glare.</summary>
    public static LumiereProfile Halo { get; } = Profile(Family, "zenith-halo", "圣环", LumiereMood.Neutral,
        Rig([LumiereRigBase.Shaft with { Spread = 0.07 }, Fan with { Spread = 0.42, Intensity = 0.24 }],
            glare: Glare with { Radius = 0.2, Intensity = 1.2, Streak = 0.7 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.8, 1.2)) with
    {
        Region = new LumiereRegion(0.5, 0.5, 0.72, 0.62),
        Starfall = Starfall,
        LineArt = c => LumiereLineArtSpec.Merge(
            ProtractorHalo(c.Aspect * 0.5, 0.03, 0.42, alpha: 0.7, spokeStep: Math.PI / 18),
            ProtractorHalo(c.Aspect * 0.5, 0.03, 0.24, 0.2, 0.4),
            ScatteredSparks(c.Aspect, 20, c.Random, 0.3)),
    };

    /// <summary>游光 (q): one shaft swinging wide across the lines.</summary>
    public static LumiereProfile Drift { get; } = Profile(Family, "zenith-drift", "游光", LumiereMood.Quiet,
        Rig([
            LumiereRigBase.Shaft with { Sway = new LumiereOscillation(0.2, 9, 0), Intensity = 0.85 },
            Fan with { Intensity = 0.2, Sway = new LumiereOscillation(0.2, 9, 0) },
        ], fog => fog with { Density = 0.9 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.7, 1.4)) with { Camera = new LumiereCameraSpec(0.02, 0.01, 0) };

    /// <summary>双柱 (n): two parallel top shafts slowly swaying towards each other.</summary>
    public static LumiereProfile Twin { get; } = Profile(Family, "zenith-twin", "双柱", LumiereMood.Neutral,
        Rig([
            LumiereRigBase.Shaft with { X = 0.33, Spread = 0.06, Sway = new LumiereOscillation(0.06, 12, 0) },
            LumiereRigBase.Shaft with { X = 0.67, Spread = 0.06, Sway = new LumiereOscillation(0.06, 12, 0.5) },
        ], noGlare: true),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(
            ViewfinderFrame(c.Aspect, 0.07, 0.21, 0.93, 0.8, alpha: 0.32),
            ScatteredSparks(c.Aspect, 18, c.Random, 0.2)),
    };

    /// <summary>光雨 (l): fine vertical lines of light falling like rain.</summary>
    public static LumiereProfile Rain { get; } = Profile(Family, "zenith-rain", "光雨", LumiereMood.Loud,
        Rig([.. new[] { 0.18, 0.34, 0.5, 0.66, 0.82, 0.42 }.Select((x, i) => LumiereRigBase.Shaft with
        {
            X = x, Spread = 0.012, Width = 0.01, Length = 1.1, Intensity = 0.45, Streaks = 0.7, StreakFreq = 3,
            Pulse = new LumiereOscillation(0.35, 1.7 + i * 0.37, i * 0.21), Sway = null,
        })], fog => fog with { DriftY = 0.05 }, Glare with { Intensity = 0.5 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(1.3, 0.6))
        with { Starfall = Starfall with { Rain = 220, RainSpeed = 0.45, RainSpread = 1.4, Brightness = 1.1 } };

    /// <summary>光井 (q): a narrow shaft onto a round pool under the text, all else dark.</summary>
    public static LumiereProfile Well { get; } = Profile(Family, "zenith-well", "光井", LumiereMood.Quiet,
        Rig([LumiereRigBase.Shaft with { Spread = 0.05, Width = 0.02, Length = 1.4, Intensity = 0.8, Core = 0.9 }],
            fog => fog with { Density = 0.7, Ambient = 0.015 }, Glare with { Intensity = 0.6 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6), Motes with { Count = 160 }) with
    {
        LineArt = c => new LumiereLineArtSpec(
            [
                new LumiereLinePath(Ellipse(c.Aspect * 0.5, 0.8, 0.26, 0.05), 0.0018, 0.7, 0.1, 0.5),
                new LumiereLinePath(Ellipse(c.Aspect * 0.5, 0.8, 0.17, 0.032), 0.0012, 0.45, 0.2, 0.4, (0.004, 0.008)),
            ],
            [new LumiereLineNode(P(c.Aspect * 0.5, 0.8), 0.03, 0.3, 0)]),
    };

    /// <summary>冠冕 (l): 天井 with a larger glare and EVA crosses on ~40% of sung glyphs, orange-red.</summary>
    public static LumiereProfile Crown { get; } = Profile(Family, "zenith-crown", "冠冕", LumiereMood.Loud,
        context => Shaft.Light(context) with { Glare = new LumiereGlareSpec(0.5, 0.0, 0.18, 1.3, 0.8) },
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.25, 0.7)) with
    {
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.4, 1, 0, 4, new Vector3(1, 0.6f, 0.4f)),
        Starfall = new LumiereStarfallSpec(520, 2.6, 120, 0.32, 0.5, 1.1),
        Camera = new LumiereCameraSpec(0.05, 0, -0.01),
    };

    /// <summary>垂降 (n): the shaft grows downwards over the first seconds of each shot.</summary>
    public static LumiereProfile Descent { get; } = Profile(Family, "zenith-descent", "垂降", LumiereMood.Neutral,
        Rig([LumiereRigBase.Shaft with { Reveal = 3 }, Fan with { Reveal = 3.5 }]),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with { Starfall = Starfall with { Rain = 40 } };

    /// <summary>余烬 (q): a dying shaft, warm afterglow in the smoke and rising sparks.</summary>
    public static LumiereProfile Ember { get; } = Profile(Family, "zenith-ember", "余烬", LumiereMood.Quiet,
        Rig([
            LumiereRigBase.Shaft with { Intensity = 0.45, Tint = new Vector3(1, 0.7f, 0.5f), Pulse = new LumiereOscillation(0.2, 5, 0) },
            Fan with { Intensity = 0.12, Tint = new Vector3(1, 0.6f, 0.4f) },
        ], fog => fog with { Density = 1.2, Ambient = 0.04, DriftY = -0.05 }, Glare with { Intensity = 0.4 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(1.4, 0.8),
        Motes with { Count = 320, DriftY = -0.035, Swirl = 0.03, Gain = 1.6, Ambient = 0.04 })
        with { Region = new LumiereRegion(0.5, 0.5, 0.72, 0.62), Camera = new LumiereCameraSpec(0.02, 0, -0.012) };

    public static IReadOnlyList<LumiereProfile> All { get; } =
        [Shaft, FanLight, Halo, Drift, Twin, Rain, Well, Crown, Descent, Ember];
}
