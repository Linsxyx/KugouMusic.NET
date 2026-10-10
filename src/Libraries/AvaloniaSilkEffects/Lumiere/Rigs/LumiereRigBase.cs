using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia lumiere/rigs/base.ts: building blocks shared by every family.</summary>
public static class LumiereRigBase
{
    public const double Down = Math.PI / 2;

    /// <summary>Main shaft: narrow and bright, with fine streaks inside.</summary>
    public static LumiereBeamSpec Shaft { get; } = new()
    {
        X = 0.5, Y = -0.04, Angle = Down, Spread = 0.09, Width = 0.05, Length = 0.8, Softness = 0.6,
        Intensity = 0.95, Streaks = 0.6, StreakFreq = 7, StreakSpeed = 0.08, Core = 0.75,
        Sway = new LumiereOscillation(0.012, 13, 0),
    };

    /// <summary>A radiating fan around the main shaft.</summary>
    public static LumiereBeamSpec Fan { get; } = new()
    {
        X = 0.5, Y = -0.04, Angle = Down, Spread = 0.3, Length = 0.6, Softness = 0.9,
        Intensity = 0.32, Streaks = 0.85, StreakFreq = 26, StreakSpeed = 0.05, Core = 0.4,
        Sway = new LumiereOscillation(0.02, 17, 0.3),
    };

    public static LumiereFogSpec Fog { get; } = new(1, 0.12, 2.4, 0.008, -0.04, 0.9, 0.03);
    public static LumiereGlareSpec Glare { get; } = new(0.5, 0.0, 0.12, 0.9, 0.45);

    public static LumiereMotesSpec Motes { get; } = new(260, 0.004, 0.013, 0.004, -0.01, 0.02, 9, 0.5, 0.015, 1.3);
    public static LumiereMotesSpec Front { get; } = new(9, 0.05, 0.15, 0.006, -0.002, 0.012, 14, 0.2, 0.02, 0.3);
    public static LumiereStarfallSpec Starfall { get; } = new(420, 3.2, 70, 0.22, 0.35, 1);

    /// <summary>Protractor halo + viewfinder + scattered sparks (the reference image's line art).</summary>
    public static LumiereLineArtSpec StandardArt(LumiereRigContext context, double haloRadius = 0.3) => LumiereLineArtSpec.Merge(
        LumiereRecipes.ProtractorHalo(context.Aspect * 0.5, 0.03, haloRadius, alpha: 0.55),
        LumiereRecipes.ViewfinderFrame(context.Aspect, 0.07, 0.21, 0.93, 0.8, 0.1, 0.32),
        LumiereRecipes.ScatteredSparks(context.Aspect, 16, context.Random, 0.3));

    /// <summary>Only the viewfinder and sparks (families that draw their own diagrams).</summary>
    public static LumiereLineArtSpec FrameArt(LumiereRigContext context) => LumiereLineArtSpec.Merge(
        LumiereRecipes.ViewfinderFrame(context.Aspect, 0.07, 0.21, 0.93, 0.8, 0.1, 0.28),
        LumiereRecipes.ScatteredSparks(context.Aspect, 14, context.Random, 0.3));

    /// <summary>Sparks only (count, delay 0.3, optional region).</summary>
    public static Func<LumiereRigContext, LumiereLineArtSpec> Sparks(int count, (double, double, double, double)? region = null) =>
        context => LumiereRecipes.ScatteredSparks(context.Aspect, count, context.Random, 0.3, region);

    public static LumierePoint[] Ellipse(double cx, double cy, double rx, double ry, int steps = 96) =>
        [.. LumiereRecipes.ArcPoints(0, 0, 1, 0, Math.PI * 2, steps).Select(p => new LumierePoint(cx + p.X * rx, cy + p.Y * ry))];

    /// <summary>A rig with the default fog and glare unless given.</summary>
    public static Func<LumiereRigContext, LumiereLightRig> Rig(
        LumiereBeamSpec[] beams,
        Func<LumiereFogSpec, LumiereFogSpec>? fog = null,
        LumiereGlareSpec? glare = null,
        bool noGlare = false,
        LumiereCausticSpec? caustic = null,
        LumiereWaveSpec? wave = null)
    {
        var rig = new LumiereLightRig(beams, fog?.Invoke(Fog) ?? Fog, noGlare ? null : glare ?? Glare, caustic, wave);
        return _ => rig;
    }

    /// <summary>A profile with the family's defaults; each profile only states what differs.</summary>
    public static LumiereProfile Profile(string family, string kind, string label, LumiereMood mood,
        Func<LumiereRigContext, LumiereLightRig> light, LumiereTypography typography, LumiereDecaySpec decay,
        LumiereMotesSpec? motes = null) => new()
    {
        Family = family,
        Kind = kind,
        Label = label,
        Mood = mood,
        Light = light,
        Motes = motes ?? Motes,
        Front = Front,
        Typography = typography,
        Decay = decay,
    };
}
