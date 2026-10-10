using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>
/// Folia lumiere/rigs/stage.ts — 追光族 (stage): spot, searchlights, footlights, backlight, strobe, laser,
/// projector. Thicker haze; lamp heads are drawn as line art (later).
/// </summary>
public static class LumiereStageRigs
{
    private const string Family = "stage";

    private static readonly LumiereFogSpec Haze = Fog with
    {
        Density = 1.25, TyndallBase = 0.08, Scale = 2, DriftX = 0.01, DriftY = -0.015, Warp = 1.1, Ambient = 0.035,
    };

    private static readonly LumiereMotesSpec StageMotes = Motes with { Count = 200 };

    private static readonly (double X, double Y) Center = (0.5, 0.56);

    /// <summary>A lamp at <paramref name="a"/> aimed at <paramref name="b"/> (frame fractions).</summary>
    private static LumiereBeamSpec Lamp(double aspect, (double X, double Y) a, (double X, double Y) b) => LumiereRigBase.Shaft with
    {
        X = a.X, Y = a.Y, Angle = Math.Atan2(b.Y - a.Y, (b.X - a.X) * aspect), Spread = 0.08, Width = 0.03, Length = 1.3,
        Softness = 0.55, Intensity = 1, Streaks = 0.4, StreakFreq = 6, Core = 0.6, Sway = null,
    };

    private static LumiereLightRig StageRig(LumiereBeamSpec[] beams, LumiereGlareSpec? glare = null,
        Func<LumiereFogSpec, LumiereFogSpec>? fog = null) => new(beams, fog?.Invoke(Haze) ?? Haze, glare);

    /// <summary>A lamp head per beam + sparks.</summary>
    private static LumiereLineArtSpec LampsArt(LumiereRigContext context, IReadOnlyList<LumiereBeamSpec> beams) => LumiereLineArtSpec.Merge(
        [
            .. beams.Select((beam, i) => LumiereDiagrams.LampHead(beam.X * context.Aspect, beam.Y, beam.Angle, 0.035, new LumiereStyle(Delay: i * 0.05))),
            LumiereRecipes.ScatteredSparks(context.Aspect, 10, context.Random, 0.3),
        ]);

    /// <summary>Stage profiles draw lamp heads at their beams' sources unless they say otherwise.</summary>
    private static LumiereProfile Stage(string kind, string label, LumiereMood mood,
        Func<LumiereRigContext, LumiereLightRig> light, LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, StageMotes) with
        {
            LineArt = context => LampsArt(context, light(context).Beams),
        };

    /// <summary>独光 (n): one follow spot from the top left onto the text.</summary>
    public static LumiereProfile Spot { get; } = Stage("stage-spot", "独光", LumiereMood.Neutral,
        c => StageRig([Lamp(c.Aspect, (0.14, 0.02), Center) with { Sway = new LumiereOscillation(0.02, 12, 0) }], new LumiereGlareSpec(0.14, 0.02, 0.08, 0.9, 0.5)),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1));

    /// <summary>交叉 (l): two beams from the top corners crossing on the text.</summary>
    public static LumiereProfile Cross { get; } = Stage("stage-cross", "交叉", LumiereMood.Loud,
        c => StageRig([
            Lamp(c.Aspect, (0.06, 0.02), Center) with { Sway = new LumiereOscillation(0.03, 9, 0) },
            Lamp(c.Aspect, (0.94, 0.02), Center) with { Sway = new LumiereOscillation(0.03, 9, 0.5) },
        ], new LumiereGlareSpec(0.5, 0.56, 0.06, 0.6, 0.6)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.3, 0.7))
        with { Burst = new LumiereBurstSpec(LumiereBurstMode.Sung, 0.3, 2, 1, 3.4, new Vector3(1, 0.75f, 0.5f)) };

    /// <summary>探照 (l): searchlights from below sweeping through the haze.</summary>
    public static LumiereProfile Search { get; } = Stage("stage-search", "探照", LumiereMood.Loud,
        _ => StageRig([.. new[] { 0.15, 0.38, 0.62, 0.85 }.Select((x, i) => LumiereRigBase.Shaft with
        {
            X = x, Y = 1.02, Angle = -Math.PI / 2, Spread = 0.035, Width = 0.03, Length = 1.4, Softness = 0.5, Intensity = 0.8,
            Streaks = 0.3, StreakFreq = 5, Core = 0.6, Sway = new LumiereOscillation(0.45, 7 + i * 1.3, i * 0.27),
        })]),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.3, 0.7)) with { Region = new LumiereRegion(0.5, 0.44, 0.72, 0.46) };

    /// <summary>脚光 (q): a row of warm footlights shining upwards.</summary>
    public static LumiereProfile Footlight { get; } = Stage("stage-footlight", "脚光", LumiereMood.Quiet,
        _ => StageRig([.. new[] { 0.25, 0.5, 0.75 }.Select(x => LumiereRigBase.Shaft with
        {
            X = x, Y = 1.03, Angle = -Math.PI / 2, Spread = 0.22, Width = 0.08, Length = 0.7, Softness = 0.9, Intensity = 0.6,
            Streaks = 0.4, StreakFreq = 10, Core = 0.3, Tint = new Vector3(1, 0.8f, 0.6f), Sway = null,
        })], null, fog => fog with { DriftY = -0.03 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with { Region = new LumiereRegion(0.5, 0.46, 0.5, 0.62) };

    /// <summary>逆光 (n): a strong light behind the text radiating outwards; the text reads as a silhouette.</summary>
    public static LumiereProfile Backlight { get; } = Stage("stage-backlight", "逆光", LumiereMood.Neutral,
        _ => StageRig([.. Enumerable.Range(0, 6).Select(k => LumiereRigBase.Shaft with
        {
            X = 0.5, Y = 0.5, Angle = k / 6.0 * Math.PI * 2 + 0.5, Spread = 0.12, Width = 0.02, Length = 0.8, Softness = 0.7,
            Intensity = 0.6, Streaks = 0.6, StreakFreq = 10, Core = 0.4, Sway = new LumiereOscillation(0.05, 11, k / 6.0),
        })], new LumiereGlareSpec(0.5, 0.5, 0.22, 1.2, 0.8)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with { Region = new LumiereRegion(0.5, 0.5, 0.72, 0.5), LineArt = Sparks(18) };

    /// <summary>针光 (q): an extremely narrow cone lighting only the text.</summary>
    public static LumiereProfile Pinspot { get; } = Stage("stage-pinspot", "针光", LumiereMood.Quiet,
        c => StageRig([Lamp(c.Aspect, (0.5, 0.02), Center) with { Spread = 0.03, Width = 0.01, Intensity = 0.9, Core = 0.9 }],
            null, fog => fog with { Ambient = 0.015 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6));

    /// <summary>频闪 (l): lamps strobing on the beat.</summary>
    public static LumiereProfile Strobe { get; } = Stage("stage-strobe", "频闪", LumiereMood.Loud,
        c => StageRig([.. new[] { 0.1, 0.36, 0.64, 0.9 }.Select((x, i) => Lamp(c.Aspect, (x, 0.02), (0.5, 0.6)) with
        {
            Spread = 0.08, Intensity = 0.55, Pulse = new LumiereOscillation(0.95, 0.5, i * 0.25),
        })], new LumiereGlareSpec(0.5, 0.02, 0.2, 0.6, 0.4)),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.4, 0.6))
        with { Burst = new LumiereBurstSpec(LumiereBurstMode.Sweep, 0.5, 2, 0, 3, new Vector3(1, 0.9f, 0.8f)) };

    /// <summary>幕缝 (q): a vertical line of light through a gap in the curtains.</summary>
    public static LumiereProfile Curtain { get; } = Stage("stage-curtain", "幕缝", LumiereMood.Quiet,
        _ => StageRig([LumiereRigBase.Shaft with { Spread = 0.01, Width = 0.04, Length = 1.6, Softness = 0.5, Intensity = 0.9, Streaks = 0.3, Core = 0.7, Sway = null }],
            new LumiereGlareSpec(0.5, -0.02, 0.08, 0.8, 0.5), fog => fog with { Ambient = 0.02 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        Region = new LumiereRegion(0.5, 0.5, 0.3, 0.66),
        LineArt = c => LumiereDiagrams.Merge(LumiereDiagrams.Curtains(c.Aspect, 0.06),
            LumiereRecipes.ScatteredSparks(c.Aspect, 8, c.Random, 0.3, (0.3, 0.2, 0.7, 0.9))),
    };

    /// <summary>激光 (l): a fan of thin sharp lasers from below, sweeping in the haze.</summary>
    public static LumiereProfile Laser { get; } = Stage("stage-laser", "激光", LumiereMood.Loud,
        _ => StageRig([.. Enumerable.Range(0, 6).Select(k => LumiereRigBase.Shaft with
        {
            X = 0.5, Y = 1.02, Angle = -Math.PI / 2 + (k - 2.5) * 0.22, Spread = 0.002, Width = 0.004, Length = 2, Softness = 0.3,
            Intensity = 1.2, Streaks = 0, Core = 0.9, Sway = new LumiereOscillation(0.25, 4.5, k * 0.08),
        })], new LumiereGlareSpec(0.5, 1.0, 0.06, 1, 0.6), fog => fog with { Density = 1.4 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.3, 0.7)) with { Region = new LumiereRegion(0.5, 0.4, 0.72, 0.46) };

    /// <summary>放映 (n): a projector on the left, its cone full of dust, the text projected in the light.</summary>
    public static LumiereProfile Projector { get; } = Stage("stage-projector", "放映", LumiereMood.Neutral,
        _ => StageRig([LumiereRigBase.Shaft with
        {
            X = 0.12, Y = 0.4, Angle = 0.05, Spread = 0.2, Width = 0.02, Length = 1.8, Softness = 0.4, Intensity = 1, Streaks = 0.5,
            StreakFreq = 9, Core = 0.4, Sway = null, Pulse = new LumiereOscillation(0.05, 0.12, 0),
        }], new LumiereGlareSpec(0.12, 0.4, 0.05, 1, 0.4)),
        LumiereTypography.Vertical, new LumiereDecaySpec(1, 1))
        with
        {
            Motes = Motes with { Count = 380, DriftX = 0.004, Swirl = 0.03, Gain = 1.8 },
            Region = new LumiereRegion(0.62, 0.5, 0.44, 0.58),
            LineArt = c => LumiereDiagrams.Merge(LumiereDiagrams.Projector(c.Aspect * 0.12, 0.4, 0.1),
                LumiereRecipes.ScatteredSparks(c.Aspect, 10, c.Random, 0.3)),
        };

    public static IReadOnlyList<LumiereProfile> All { get; } =
        [Spot, Cross, Search, Footlight, Backlight, Pinspot, Strobe, Curtain, Laser, Projector];
}
