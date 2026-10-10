using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/astral.ts — 星象族: eclipse, corona, orbits, star trails, constellations, moon and nebula; full skies.</summary>
public static class LumiereAstralRigs
{
    private const string Family = "astral";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, Motes with { Count = 160 })
            // Orbits, trails, constellations and the armillary are this family's protagonists.
            with { Starfall = Starfall with { Stars = 620, Rain = 20, Brightness = 1.1 }, ArtGain = 2 };

    private static LumiereLineArtSpec AstralSparks(LumiereRigContext c) => ScatteredSparks(c.Aspect, 20, c.Random, 0.3);

    /// <summary>A ring of beams radiating from the edge of disc (cx, cy, r) (corona, eclipse).</summary>
    private static LumiereBeamSpec[] Radial(double cx, double cy, double r, double aspect, int count, Func<LumiereBeamSpec, LumiereBeamSpec>? overrides = null) =>
        [.. Enumerable.Range(0, count).Select(k =>
        {
            var angle = (double)k / count * Math.PI * 2 + 0.3;
            var beam = Shaft with
            {
                X = cx + Math.Cos(angle) * r / aspect, Y = cy + Math.Sin(angle) * r, Angle = angle, Spread = 0.14, Width = 0.04, Length = 0.35, Softness = 0.8,
                Intensity = 0.7, Streaks = 0.7, StreakFreq = 12, Core = 0.3, Sway = new LumiereOscillation(0.04, 9 + k, (double)k / count),
            };
            return overrides?.Invoke(beam) ?? beam;
        })];

    /// <summary>日食 (l): corona light around a black disc, a Baily's bead flashing at its edge.</summary>
    public static LumiereProfile Eclipse { get; } = Make("astral-eclipse", "日食", LumiereMood.Loud,
        c => new LumiereLightRig(Radial(0.5, 0.34, 0.13, c.Aspect, 6),
            new LumiereFogSpec(0.8, 0.2, 2.4, 0.004, -0.01, 0.9, 0.02),
            new LumiereGlareSpec(0.5 + 0.13 * Math.Cos(-0.8) / c.Aspect, 0.34 + 0.13 * Math.Sin(-0.8), 0.05, 1.3, 0.9)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Corona(c.Aspect * 0.5, 0.34, 0.13, c.Random), AstralSparks(c)),
        Region = new LumiereRegion(0.5, 0.7, 0.72, 0.4),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.35, 1, 0, 3.6, new Vector3(1, 0.6f, 0.45f)),
    };

    /// <summary>轨道 (q): concentric elliptical orbits with planet points on them.</summary>
    public static LumiereProfile Orbit { get; } = Make("astral-orbit", "轨道", LumiereMood.Quiet,
        Rig([Shaft with { Spread = 0.05, Intensity = 0.45, Reach = 0.4 }], fog => fog with { Density = 0.6, Ambient = 0.015 },
            new LumiereGlareSpec(0.5, 0.42, 0.05, 1, 0.5)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.Orbits(c.Aspect * 0.5, 0.42, [0.12, 0.2, 0.3, 0.42], -0.18, 0.34, c.Random), AstralSparks(c)),
        Region = new LumiereRegion(0.5, 0.74, 0.72, 0.32),
    };

    /// <summary>星轨 (q): rings of star trails turning slowly around the pole.</summary>
    public static LumiereProfile Trail { get; } = Make("astral-trail", "星轨", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.72, Spread = 0.04, Intensity = 0.35 }], fog => fog with { Density = 0.5, Ambient = 0.01 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.5, 1.8)) with
    {
        LineArt = c => D.Merge(D.StarTrails(c.Aspect * 0.72, 0.12, c.Random, 60, 0.9)),
        Region = new LumiereRegion(0.36, 0.54, 0.44, 0.6),
    };

    /// <summary>六分仪 (n): a scale arc, two radii and a sight line aimed at the sun.</summary>
    public static LumiereProfile Sextant { get; } = Make("astral-sextant", "六分仪", LumiereMood.Neutral,
        Rig([Shaft with { X = 0.85, Y = -0.05, Angle = 2.2, Spread = 0.05, Width = 0.03, Length = 1.4, Intensity = 0.9 }], fog => fog with { Density = 0.8 },
            Glare with { X = 0.85, Y = -0.02 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Sextant(c.Aspect * 0.4, 0.14, 0.32), AstralSparks(c)),
        Region = new LumiereRegion(0.52, 0.66, 0.72, 0.4),
    };

    /// <summary>星座 (n): stars joined into a constellation, the text like star names.</summary>
    public static LumiereProfile Constellation { get; } = Make("astral-constellation", "星座", LumiereMood.Neutral,
        Rig([Shaft with { Spread = 0.06, Intensity = 0.4 }], fog => fog with { Density = 0.6, Ambient = 0.015 }, Glare with { Intensity = 0.4 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(
            D.Constellation(c.Aspect, c.Random, 8, (0.1, 0.1, 0.9, 0.42)),
            D.Constellation(c.Aspect, c.Random, 5, (0.6, 0.62, 0.95, 0.9), new LumiereStyle(Delay: 0.3))),
        Region = new LumiereRegion(0.42, 0.66, 0.6, 0.4),
    };

    /// <summary>日冕 (l): the sun at the top, fine coronal streamers at its edge.</summary>
    public static LumiereProfile Corona { get; } = Make("astral-corona", "日冕", LumiereMood.Loud,
        c => new LumiereLightRig(Radial(0.5, 0.12, 0.1, c.Aspect, 6, beam => beam with { Length = 0.6, Intensity = 0.9 }),
            new LumiereFogSpec(1, 0.15, 2.4, 0.004, -0.02, 0.9, 0.03),
            new LumiereGlareSpec(0.5, 0.12, 0.16, 1.3, 0.7)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Corona(c.Aspect * 0.5, 0.12, 0.1, c.Random), AstralSparks(c)),
        Region = new LumiereRegion(0.5, 0.62, 0.72, 0.46),
        Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.3, 2, 0, 3.4, new Vector3(1, 0.65f, 0.4f)),
    };

    /// <summary>月光 (q): a crescent at the top right; cold light slants down through flowing cloud.</summary>
    public static LumiereProfile Moon { get; } = Make("astral-moon", "月光", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.82, Y = 0.12, Angle = 2.1, Spread = 0.12, Width = 0.08, Length = 1.4, Softness = 0.8, Intensity = 0.75, Tint = new Vector3(0.72f, 0.82f, 1) }],
            fog => fog with { Density = 1.2, DriftX = -0.01, DriftY = 0, Ambient = 0.035, Warp = 1.3 }, new LumiereGlareSpec(0.82, 0.12, 0.1, 0.8, 0.2)),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => D.Merge(D.Crescent(c.Aspect * 0.82, 0.12, 0.07), AstralSparks(c)),
        Region = new LumiereRegion(0.38, 0.54, 0.5, 0.62),
    };

    /// <summary>星云 (n): large colored smoke with light showing through.</summary>
    public static LumiereProfile Nebula { get; } = Make("astral-nebula", "星云", LumiereMood.Neutral,
        Rig([
            Shaft with { X = 0.4, Y = 0.45, Angle = -0.6, Spread = 0.6, Width = 0.1, Length = 0.8, Softness = 1, Intensity = 0.5, Streaks = 0.6, StreakFreq = 3, Core = 0.2, Spectrum = 0.7, Sway = new LumiereOscillation(0.2, 20, 0) },
            Shaft with { X = 0.6, Y = 0.45, Angle = 2.4, Spread = 0.6, Width = 0.1, Length = 0.8, Softness = 1, Intensity = 0.4, Streaks = 0.6, StreakFreq = 3, Core = 0.2, Spectrum = 0.7, Sway = new LumiereOscillation(0.2, 23, 0.5) },
        ], fog => fog with { Density = 1.4, Ambient = 0.06, Warp = 1.6, Scale = 1.6, DriftX = 0.006, DriftY = 0.002 }, new LumiereGlareSpec(0.5, 0.45, 0.08, 0.7, 0.2)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = AstralSparks,
    };

    /// <summary>浑天 (n): an armillary sphere's rings turning slowly.</summary>
    public static LumiereProfile Armillary { get; } = Make("astral-armillary", "浑天", LumiereMood.Neutral,
        Rig([Shaft, Fan with { Intensity = 0.2 }]),
        LumiereTypography.Vertical, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => D.Merge(D.Armillary(c.Aspect * 0.5, 0.42, 0.26), AstralSparks(c)),
        Region = new LumiereRegion(0.5, 0.5, 0.5, 0.62),
    };

    /// <summary>流星 (l): a few slanted streaks with trails.</summary>
    public static LumiereProfile Meteor { get; } = Make("astral-meteor", "流星", LumiereMood.Loud,
        Rig([.. new[] { 0, 1, 2 }.Select(i => Shaft with
        {
            X = 0.95 - i * 0.2, Y = -0.02 + i * 0.05, Angle = 2.5, Spread = 0.01, Width = 0.006, Length = 0.6, Intensity = 0.9, Streaks = 0.2, Core = 0.8,
            Reach = 0.5 + i * 0.1, Pulse = new LumiereOscillation(0.7, 2.2 + i * 0.7, i * 0.3), Sway = null,
        })], fog => fog with { Density = 0.7, Ambient = 0.015 }, noGlare: true),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7)) with
    {
        LineArt = c => D.Merge(D.Meteors(c.Aspect, c.Random, 6), AstralSparks(c)),
        Starfall = Starfall with { Stars = 620, Rain = 60, RainSpeed = 0.6, RainSpread = 1.8, Brightness = 1.2 },
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Eclipse, Orbit, Trail, Sextant, Constellation, Corona, Moon, Nebula, Armillary, Meteor];
}
