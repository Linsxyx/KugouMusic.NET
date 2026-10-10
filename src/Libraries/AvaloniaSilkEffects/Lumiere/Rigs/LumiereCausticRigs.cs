using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/caustic.ts — 焦散族: flowing light nets from water, glass and crystal.</summary>
public static class LumiereCausticRigs
{
    private const string Family = "caustic";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, Motes with { DriftY = -0.004, Swirl = 0.03, SwirlPeriod = 12 })
            with { Region = new LumiereRegion(0.5, 0.46, 0.72, 0.46) };

    private static Func<LumiereRigContext, LumiereLineArtSpec> CausticSparks(int count = 10) => Sparks(count);

    private static LumiereBeamSpec SoftTop { get; } = Shaft with
    {
        Spread = 0.22, Width = 0.2, Length = 1.4, Softness = 0.8, Intensity = 0.8, Streaks = 0.3, Core = 0.3,
    };

    private static LumiereCausticSpec Water(double scale = 2.2, double speed = 0.35, double inBeam = 0.8, LumiereCausticFloor? floor = null) =>
        new(scale, speed, inBeam, floor);

    /// <summary>池底 (q): top light into water; a caustic net flows in the shaft, a patch shines on the pool floor.</summary>
    public static LumiereProfile Pool { get; } = Make("caustic-pool", "池底", LumiereMood.Quiet,
        Rig([SoftTop], fog => fog with { Density = 0.8, DriftY = -0.015, Ambient = 0.02 }, Glare with { Intensity = 0.6 },
            caustic: Water(floor: new LumiereCausticFloor(0.5, 0.82, 0.42, 0.14, 0.7))),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.7, 1.4)) with
    {
        LineArt = c => new LumiereLineArtSpec([new LumiereLinePath(Ellipse(c.Aspect * 0.5, 0.82, c.Aspect * 0.42, 0.14), 0.0014, 0.4, 0.1, 0.6)],
            CausticSparks()(c).Nodes),
    };

    /// <summary>杯影 (n): side light through a glass throws a caustic on the table.</summary>
    public static LumiereProfile Cup { get; } = Make("caustic-cup", "杯影", LumiereMood.Neutral,
        Rig([Shaft with { X = -0.05, Y = 0.3, Angle = 0.35, Spread = 0.08, Width = 0.18, Length = 1.6, Softness = 0.5, Intensity = 0.9, Streaks = 0.3, Core = 0.4, Sway = null }],
            fog => fog with { Density = 0.8 }, noGlare: true,
            caustic: Water(3.2, 0.25, 0.3, new LumiereCausticFloor(0.36, 0.8, 0.14, 0.07, 1.1))),
        LumiereTypography.Vertical, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.GlassCup(c.Aspect * 0.24, 0.52, 0.1, 0.2), CausticSparks()(c)),
        Region = new LumiereRegion(0.62, 0.48, 0.56, 0.64),
    };

    /// <summary>涟漪 (n): concentric ripples on the water; the caustics in the light wobble with them.</summary>
    public static LumiereProfile Ripple { get; } = Make("caustic-ripple", "涟漪", LumiereMood.Neutral,
        Rig([SoftTop with { Intensity = 0.75 }], fog => fog with { Density = 0.8 }, Glare with { Intensity = 0.5 },
            caustic: Water(3, 0.5, 0.6, new LumiereCausticFloor(0.5, 0.8, 0.34, 0.1, 0.5))),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Ripples(c.Aspect * 0.5, 0.8, 0.34, 5), CausticSparks()(c)),
    };

    /// <summary>水面 (l): looking up from under water, surface glints and a few slanted beams.</summary>
    public static LumiereProfile Surface { get; } = Make("caustic-surface", "水面", LumiereMood.Loud,
        Rig([.. new[] { 0.2, 0.42, 0.6, 0.8 }.Select((x, i) => Shaft with
        {
            X = x, Y = 0.1, Angle = 1.35 + i * 0.06, Spread = 0.04, Width = 0.05, Length = 1.2, Softness = 0.6, Intensity = 0.7, Streaks = 0.5,
            StreakFreq = 6, Core = 0.5, Sway = new LumiereOscillation(0.05, 6 + i, i * 0.2),
        })], fog => fog with { Density = 1.1, DriftY = -0.02 }, noGlare: true,
            caustic: Water(2.6, 0.6, 0.9, new LumiereCausticFloor(0.5, 0.06, 0.6, 0.07, 0.9))),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => D.Merge(D.Waterline(c.Aspect, 0.1, 0.012, 7), CausticSparks(14)(c)),
        Motes = Motes with { Count = 300, DriftY = -0.02, Swirl = 0.03 },
        Region = new LumiereRegion(0.5, 0.58, 0.72, 0.46),
    };

    /// <summary>戒光 (q): the inside of a ring, the caustic curve falling inside it.</summary>
    public static LumiereProfile Ring { get; } = Make("caustic-ring", "戒光", LumiereMood.Quiet,
        Rig([SoftTop with { Spread = 0.14, Width = 0.1, Intensity = 0.7 }], fog => fog with { Density = 0.7 }, Glare with { Intensity = 0.5 },
            caustic: Water(4, 0.2, 0.2, new LumiereCausticFloor(0.5, 0.5, 0.2, 0.36, 0.6))),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(
            new LumiereLineArtSpec([
                D.Circle(c.Aspect * 0.5, 0.5, 0.36, new LumiereStyle(0.0024, 0.6)),
                D.Circle(c.Aspect * 0.5, 0.5, 0.32, new LumiereStyle(0.0009, 0.3, 0.1)),
            ], []),
            CausticSparks()(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.4, 0.62),
    };

    /// <summary>聚点 (l): a magnifier focuses the light to a point right on the text (converges, then opens).</summary>
    public static LumiereProfile Burn { get; } = Make("caustic-burn", "聚点", LumiereMood.Loud,
        Rig([
            Shaft with { X = 0.5, Y = 0.2, Angle = Math.PI / 2, Spread = -0.22, Width = 0.2, Length = 1.4, Softness = 0.5, Intensity = 1.2, Streaks = 0.3, StreakFreq = 5, Core = 0.6, Sway = new LumiereOscillation(0.02, 9, 0) },
            Shaft with { X = 0.5, Y = -0.1, Angle = Math.PI / 2, Spread = 0.03, Width = 0.22, Length = 1, Intensity = 0.4, Streaks = 0.4, Reach = 0.3, Sway = null },
        ], fog => fog with { Density = 1 }, new LumiereGlareSpec(0.5, 0.65, 0.05, 1.1, 0.8)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Magnifier(c.Aspect * 0.5, 0.2, 0.1), CausticSparks(12)(c)),
        Region = new LumiereRegion(0.5, 0.66, 0.72, 0.4),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.35, 1, 0, 3.6, new Vector3(1, 0.7f, 0.45f)),
    };

    /// <summary>粼粼 (n): horizontal water glints shimmering over the text.</summary>
    public static LumiereProfile Shimmer { get; } = Make("caustic-shimmer", "粼粼", LumiereMood.Neutral,
        Rig([Shaft with
        {
            X = -0.05, Y = 0.5, Angle = 0.02, Spread = 0.06, Width = 0.5, Length = 2.4, Softness = 0.4, Intensity = 0.8, Streaks = 0.2, Core = 0.2,
            Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 18, 0.35, 0.3), Sway = null,
        }], fog => fog with { Density = 0.8 }, noGlare: true, caustic: Water(1.4, 0.8, 0.6)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Waterline(c.Aspect, 0.86, 0.008, 9), CausticSparks()(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.72, 0.5),
    };

    /// <summary>晶影 (n): light through a crystal throws radiating spots and a touch of color.</summary>
    public static LumiereProfile CrystalShadow { get; } = Make("caustic-crystal", "晶影", LumiereMood.Neutral,
        Rig([.. new[] { 0, 1, 2, 3, 4 }.Select(k => Shaft with
        {
            X = 0.5, Y = 0.26, Angle = Math.PI / 2 + (k - 2) * 0.45, Spread = 0.05, Width = 0.01, Length = 0.8, Softness = 0.5, Intensity = 0.6,
            Streaks = 0.3, Core = 0.4, Spectrum = 0.7, Sway = new LumiereOscillation(0.05, 12, k * 0.15),
        })], fog => fog with { Density = 0.9 }, Glare with { X = 0.5, Y = 0.26, Radius = 0.07, Intensity = 1, Streak = 0.5 },
            caustic: Water(3.4, 0.2, 0.4, new LumiereCausticFloor(0.5, 0.8, 0.3, 0.09, 0.5))),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Crystal(c.Aspect * 0.5, 0.26, 0.05), CausticSparks(14)(c)),
        Region = new LumiereRegion(0.5, 0.58, 0.72, 0.44),
    };

    /// <summary>漂流 (q): large-scale caustics drifting slowly, almost still.</summary>
    public static LumiereProfile Drift { get; } = Make("caustic-drift", "漂流", LumiereMood.Quiet,
        Rig([SoftTop with { Spread = 0.4, Width = 0.4, Intensity = 1, Softness = 0.95 }],
            fog => fog with { Density = 0.8, DriftX = 0.004, DriftY = 0, Ambient = 0.03 }, Glare with { Intensity = 0.5 },
            caustic: Water(1, 0.08, 0.8, new LumiereCausticFloor(0.5, 0.5, 0.5, 0.4, 0.4))),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.5, 1.8)) with
    {
        LineArt = CausticSparks(8),
        Region = new LumiereRegion(0.5, 0.5, 0.72, 0.62),
    };

    /// <summary>骤光 (l): fast flickering caustics jumping with the drums.</summary>
    public static LumiereProfile Storm { get; } = Make("caustic-storm", "骤光", LumiereMood.Loud,
        Rig([SoftTop with { Intensity = 1, Pulse = new LumiereOscillation(0.35, 0.9, 0) }], fog => fog with { Density = 1.1 }, Glare with { Intensity = 0.8 },
            caustic: Water(3, 1.3, 1, new LumiereCausticFloor(0.5, 0.82, 0.5, 0.14, 0.8))),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Ripples(c.Aspect * 0.5, 0.82, 0.5, 4), CausticSparks(14)(c)),
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Pool, Cup, Ripple, Surface, Ring, Burn, Shimmer, CrystalShadow, Drift, Storm];
}
