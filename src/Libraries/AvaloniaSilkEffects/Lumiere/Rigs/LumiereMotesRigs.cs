using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>
/// Folia rigs/motes.ts — 萤尘族: smoke and particles, almost no beams — dust, fireflies, smoke, embers, light snow, bokeh,
/// mist, swarms, dissolving, aurora. Also the main source of interlude shots.
/// </summary>
public static class LumiereMotesRigs
{
    private const string Family = "motes";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay, LumiereMotesSpec motes) =>
        Profile(Family, kind, label, mood, light, typography, decay, motes)
            with { Starfall = Starfall with { Rain = 30 }, LineArt = Sparks(10) };

    /// <summary>浮尘 (q): one slanted beam in a dark room, dust slowly turning in it.</summary>
    public static LumiereProfile Dust { get; } = Make("motes-dust", "浮尘", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.15, Y = -0.05, Angle = 1.05, Spread = 0.1, Width = 0.1, Length = 1.5, Intensity = 0.8, Streaks = 0.4 }],
            fog => fog with { Density = 0.8, Ambient = 0.015 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.7, 1.4),
        Motes with { Count = 420, SizeMin = 0.003, SizeMax = 0.012, DriftX = 0.002, DriftY = -0.004, Swirl = 0.035, SwirlPeriod = 14, Gain = 1.6 });

    /// <summary>萤火 (q): no beams, just scattered glowing points flickering on their own.</summary>
    public static LumiereProfile Firefly { get; } = Make("motes-firefly", "萤火", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.05, Intensity = 0.15 }], fog => fog with { Density = 0.6, Ambient = 0.02 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6),
        Motes with { Count = 90, SizeMin = 0.008, SizeMax = 0.022, DriftX = 0.003, DriftY = -0.003, Swirl = 0.06, SwirlPeriod = 11, Twinkle = 0.9, Ambient = 0.55, Gain = 0.8 })
        with { Front = Front with { Count = 14, Ambient = 0.12, Twinkle = 0.7 }, Region = new LumiereRegion(0.5, 0.5, 0.5, 0.62) };

    /// <summary>烟缕 (n): wisps of rising smoke outlined by side light.</summary>
    public static LumiereProfile Smoke { get; } = Make("motes-smoke", "烟缕", LumiereMood.Neutral,
        Rig([Shaft with { X = -0.05, Y = 0.55, Angle = -0.08, Spread = 0.18, Width = 0.3, Length = 1.6, Softness = 0.7, Intensity = 0.42, Streaks = 0.2, Core = 0.3, Sway = null }],
            fog => fog with { Density = 1.5, TyndallBase = 0.05, Scale = 1.8, Warp = 1.8, DriftX = 0.004, DriftY = -0.06, Ambient = 0.05 }, noGlare: true),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1), Motes with { Count = 140 });

    /// <summary>飞烬 (l): warm red light burning up from below, sparks rising.</summary>
    public static LumiereProfile Embers { get; } = Make("motes-embers", "飞烬", LumiereMood.Loud,
        Rig([Shaft with
        {
            X = 0.5, Y = 1.05, Angle = -Math.PI / 2, Spread = 0.35, Width = 0.3, Length = 0.6, Softness = 0.9, Intensity = 0.8, Streaks = 0.6, StreakFreq = 8, Core = 0.3,
            Tint = new Vector3(1, 0.55f, 0.3f), Pulse = new LumiereOscillation(0.2, 1.3, 0), Sway = null,
        }], fog => fog with { Density = 1.1, DriftY = -0.05 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.4, 0.6),
        Motes with { Count = 380, SizeMin = 0.003, SizeMax = 0.01, DriftX = 0.004, DriftY = -0.07, Swirl = 0.03, SwirlPeriod = 5, Twinkle = 0.8, Ambient = 0.1, Gain = 1.8 })
        with { Region = new LumiereRegion(0.5, 0.44, 0.72, 0.46) };

    /// <summary>光雪 (q): light points falling slowly like snow.</summary>
    public static LumiereProfile Snow { get; } = Make("motes-snow", "光雪", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.2, Width = 0.2, Softness = 0.9, Intensity = 0.5, Core = 0.3 }], fog => fog with { Density = 0.8, DriftY = 0.01 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6),
        Motes with { Count = 460, SizeMin = 0.003, SizeMax = 0.012, DriftX = 0.003, DriftY = 0.035, Swirl = 0.02, SwirlPeriod = 8, Twinkle = 0.3, Ambient = 0.15, Gain = 1 })
        with
        {
            Region = new LumiereRegion(0.5, 0.5, 0.5, 0.62),
            Starfall = Starfall with { Rain = 60, RainSpeed = 0.12, RainSpread = 1.8, Brightness = 0.7 },
        };

    /// <summary>散景 (n): big defocused discs in the foreground, focus on the text.</summary>
    public static LumiereProfile Bokeh { get; } = Make("motes-bokeh", "散景", LumiereMood.Neutral,
        Rig([Shaft with { Intensity = 0.6 }], fog => fog with { Density = 0.8 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1), Motes with { Count = 120 })
        with { Front = Front with { Count = 34, SizeMin = 0.06, SizeMax = 0.2, Ambient = 0.1, Gain = 0.5, Twinkle = 0.3, DriftX = 0.008 } };

    /// <summary>晨雾 (q): a low mist layer with light coming through from behind.</summary>
    public static LumiereProfile Mist { get; } = Make("motes-mist", "晨雾", LumiereMood.Quiet,
        // Light behind mist: beam and glare kept low, no horizontal streak (it used to wash out as a line across the frame).
        Rig([Shaft with { X = 0.7, Y = 0.62, Angle = -2.8, Spread = 0.5, Width = 0.3, Length = 1.2, Softness = 1, Intensity = 0.55, Streaks = 0.3, Core = 0.2, Sway = null }],
            fog => fog with { Density = 1.5, TyndallBase = 0.1, Scale = 1.2, Warp = 0.6, DriftX = 0.02, DriftY = 0, Ambient = 0.05 },
            new LumiereGlareSpec(0.7, 0.62, 0.16, 0.5, 0.08)),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6), Motes with { Count = 120, DriftX = 0.01 })
        with
        {
            LineArt = c => D.Merge(D.Horizon(c.Aspect, 0.64), ScatteredSparks(c.Aspect, 8, c.Random, 0.3, (0.05, 0.05, 0.95, 0.55))),
            Region = new LumiereRegion(0.36, 0.42, 0.44, 0.5),
        };

    /// <summary>光群 (l): particles flowing in swarms around the text, then scattering.</summary>
    public static LumiereProfile Swarm { get; } = Make("motes-swarm", "光群", LumiereMood.Loud,
        Rig([Shaft with { Intensity = 0.8 }, Shaft with { Spread = 0.3, Length = 0.6, Softness = 0.9, Intensity = 0.25, Streaks = 0.8, StreakFreq = 24 }]),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7),
        Motes with { Count = 460, SizeMin = 0.003, SizeMax = 0.012, DriftX = 0, DriftY = 0, Swirl = 0.14, SwirlPeriod = 6, Twinkle = 0.6, Ambient = 0.08, Gain = 1.6 });

    /// <summary>化尘 (n): the text crumbles into light dust as soon as it is sung (bridging).</summary>
    public static LumiereProfile Dissolve { get; } = Make("motes-dissolve", "化尘", LumiereMood.Neutral,
        Rig([Shaft with { Intensity = 0.7 }]),
        LumiereTypography.Crossed, new LumiereDecaySpec(2.2, 0.3), Motes with { Count = 380, DriftY = -0.02, Swirl = 0.04, Gain = 1.5 });

    /// <summary>极光 (l): curtains of light flowing across the top of the frame.</summary>
    public static LumiereProfile Aurora { get; } = Make("motes-aurora", "极光", LumiereMood.Loud,
        Rig([.. new[] { 0.2, 0.4, 0.6, 0.8 }.Select((x, i) => Shaft with
        {
            X = x, Y = -0.1, Angle = Math.PI / 2 + 0.1, Spread = 0.06, Width = 0.16, Length = 0.7, Softness = 0.8, Intensity = 0.7, Streaks = 0.9, StreakFreq = 30,
            StreakSpeed = 0.3, Core = 0.2, Spectrum = 0.5, Tint = new Vector3(0.6f, 1, 0.8f), Sway = new LumiereOscillation(0.12, 8 + i * 1.7, i * 0.2),
        })], fog => fog with { Density = 0.9, Ambient = 0.03 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8), Motes)
        with { Region = new LumiereRegion(0.5, 0.62, 0.72, 0.4), Starfall = Starfall with { Stars = 560, Rain = 20 } };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Dust, Firefly, Smoke, Embers, Snow, Bokeh, Mist, Swarm, Dissolve, Aurora];
}
