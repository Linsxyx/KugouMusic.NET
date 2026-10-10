using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;
using static AvaloniaSilkEffects.Lumiere.Rigs.LumiereRigBase;
using D = AvaloniaSilkEffects.Lumiere.LineArt.LumiereDiagrams;

namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia rigs/lattice.ts — 窗隙族: light through windows, door gaps and leaves; gobos cut the beams into patterns.</summary>
public static class LumiereLatticeRigs
{
    private const string Family = "lattice";

    private static LumiereProfile Make(string kind, string label, LumiereMood mood, Func<LumiereRigContext, LumiereLightRig> light,
        LumiereTypography typography, LumiereDecaySpec decay) =>
        Profile(Family, kind, label, mood, light, typography, decay, Motes with { DriftX = 0.008, DriftY = -0.006 })
            with { Region = new LumiereRegion(0.56, 0.58, 0.66, 0.5) };

    /// <summary>A wide slanted beam from a top-left window (the common window light).</summary>
    private static LumiereBeamSpec WindowBeam { get; } = Shaft with
    {
        X = 0.1, Y = 0.06, Angle = 0.62, Spread = 0.1, Width = 0.34, Length = 1.3, Softness = 0.35,
        Intensity = 1, Streaks = 0.25, StreakFreq = 4, Core = 0.3, Sway = new LumiereOscillation(0.01, 17, 0),
    };

    private static LumiereLineArtSpec WindowSparks(LumiereRigContext c, (double, double, double, double)? region = null) =>
        ScatteredSparks(c.Aspect, 14, c.Random, 0.3, region ?? (0.3, 0.3, 0.95, 0.9));

    /// <summary>百叶 (n): blinds cut a wide slanted window beam into parallel bands.</summary>
    public static LumiereProfile Blinds { get; } = Make("lattice-blinds", "百叶", LumiereMood.Neutral,
        Rig([WindowBeam with { Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 13, 0.52, 0.02) }],
            fog => fog with { DriftX = 0.012, DriftY = -0.02 }, Glare with { X = 0.1, Y = 0.06, Radius = 0.16, Intensity = 0.8 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(BlindsWindow(c.Aspect * 0.03, 0.02, c.Aspect * 0.16, 0.26, 9, alpha: 0.5), WindowSparks(c)),
    };

    /// <summary>十字窗 (q): a cross window throws four patches with a mullion shadow.</summary>
    public static LumiereProfile Cross { get; } = Make("lattice-cross", "十字窗", LumiereMood.Quiet,
        Rig([WindowBeam with { X = 0.9, Angle = Math.PI - 0.7, Width = 0.3, Intensity = 0.9, Gobo = new LumiereGoboSpec(LumiereGoboPattern.Cross, 0, 0.16, 0) }],
            fog => fog with { DriftX = -0.008 }, Glare with { X = 0.9, Y = 0.06, Radius = 0.14, Intensity = 0.7 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.7, 1.4)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.CrossWindow(c.Aspect * 0.8, 0.01, c.Aspect * 0.16, 0.26), WindowSparks(c, (0.05, 0.3, 0.7, 0.9))),
        Region = new LumiereRegion(0.4, 0.52, 0.6, 0.62),
    };

    /// <summary>拱窗 (l): a tall arched window throws a long Tyndall beam.</summary>
    public static LumiereProfile Arch { get; } = Make("lattice-arch", "拱窗", LumiereMood.Loud,
        Rig([
            WindowBeam with { X = 0.14, Y = -0.02, Angle = 0.95, Width = 0.14, Spread = 0.06, Length = 1.6, Intensity = 1.3, Streaks = 0.55, StreakFreq = 8, Core = 0.6 },
            WindowBeam with { X = 0.14, Y = -0.02, Angle = 0.95, Width = 0.3, Spread = 0.2, Length = 0.9, Softness = 0.9, Intensity = 0.3, Streaks = 0.8, StreakFreq = 22 },
        ], fog => fog with { Density = 1.1 }, Glare with { X = 0.14, Y = 0.0, Radius = 0.18, Intensity = 1.1, Streak = 0.6 }),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.ArchWindow(c.Aspect * 0.08, -0.06, c.Aspect * 0.12, 0.32), WindowSparks(c)),
        Starfall = Starfall with { Rain = 90, RainSpread = 0.8 },
    };

    /// <summary>玫瑰窗 (l): a rose window radiates a fan of thin beams.</summary>
    public static LumiereProfile Rose { get; } = Make("lattice-rose", "玫瑰窗", LumiereMood.Loud,
        Rig([.. new[] { -2, -1, 0, 1, 2, 3 }.Select(k => Shaft with
        {
            Y = 0.08, Angle = Math.PI / 2 + (k - 0.5) * 0.2, Spread = 0.035, Width = 0.03, Length = 1, Intensity = 0.65 - Math.Abs(k - 0.5) * 0.06,
            Streaks = 0.4, StreakFreq = 5, Sway = new LumiereOscillation(0.015, 13, k * 0.1),
        })], glare: Glare with { Y = 0.08, Radius = 0.16, Intensity = 1.1, Streak = 0.5 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.RoseWindow(c.Aspect * 0.5, 0.08, 0.12), ScatteredSparks(c.Aspect, 16, c.Random, 0.3)),
        Region = new LumiereRegion(0.5, 0.6, 0.72, 0.46),
        Starfall = Starfall,
    };

    /// <summary>门缝 (q): a door gap on the left; an extremely narrow line of light crosses the frame.</summary>
    public static LumiereProfile Slit { get; } = Make("lattice-slit", "门缝", LumiereMood.Quiet,
        Rig([Shaft with { X = 0.1, Y = 0.42, Angle = 0.06, Spread = 0.02, Width = 0.012, Length = 2.4, Intensity = 1, Streaks = 0.3, StreakFreq = 3, Core = 0.8, Sway = new LumiereOscillation(0.01, 15, 0) }],
            fog => fog with { Density = 0.8, Ambient = 0.015 }, Glare with { X = 0.1, Y = 0.42, Radius = 0.08, Intensity = 0.8, Streak = 0.9 }),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.DoorSlit(c.Aspect * 0.1, 0.08, 0.86, 0.012),
            ScatteredSparks(c.Aspect, 10, c.Random, 0.3, (0.2, 0.2, 0.95, 0.9))),
        Region = new LumiereRegion(0.55, 0.5, 0.6, 0.64),
    };

    /// <summary>障子 (q): soft diffuse light through a paper screen, the grid's thin shadows in it.</summary>
    public static LumiereProfile Shoji { get; } = Make("lattice-shoji", "障子", LumiereMood.Quiet,
        Rig([WindowBeam with
        {
            X = 0.12, Y = 0.1, Angle = 0.4, Width = 0.5, Spread = 0.18, Softness = 0.95, Intensity = 0.7, Streaks = 0.1, Core = 0.2,
            Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 10, 0.86, 0),
        }], fog => fog with { Density = 0.7, TyndallBase = 0.25 }, noGlare: true),
        LumiereTypography.Horizontal, new LumiereDecaySpec(0.6, 1.6)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.GridPanel(c.Aspect * 0.02, 0.06, c.Aspect * 0.18, 0.4, 3, 5), ScatteredSparks(c.Aspect, 10, c.Random, 0.3)),
    };

    /// <summary>格栅 (n): a diamond grille's light flows over the text.</summary>
    public static LumiereProfile Grille { get; } = Make("lattice-grille", "格栅", LumiereMood.Neutral,
        Rig([WindowBeam with { Width = 0.4, Spread = 0.12, Intensity = 1, Gobo = new LumiereGoboSpec(LumiereGoboPattern.Lattice, 9, 0.62, 0.04) }],
            fog => fog with { DriftX = 0.01 }, Glare with { X = 0.1, Y = 0.06, Radius = 0.14, Intensity = 0.7 }),
        LumiereTypography.Crossed, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.GridPanel(c.Aspect * 0.02, 0.02, c.Aspect * 0.16, 0.26, 4, 4, true), WindowSparks(c)),
    };

    /// <summary>扫窗 (n): a moving source sweeps the blinds' shadow across the line.</summary>
    public static LumiereProfile Sweep { get; } = Make("lattice-sweep", "扫窗", LumiereMood.Neutral,
        Rig([WindowBeam with { Angle = 0.75, Width = 0.3, Intensity = 1, Sway = new LumiereOscillation(0.32, 10, 0), Gobo = new LumiereGoboSpec(LumiereGoboPattern.Blinds, 11, 0.55, 0.05) }],
            fog => fog with { DriftX = 0.01 }, Glare with { X = 0.1, Y = 0.06, Radius = 0.14, Intensity = 0.7 }),
        LumiereTypography.Horizontal, new LumiereDecaySpec(1, 1)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(BlindsWindow(c.Aspect * 0.03, 0.02, c.Aspect * 0.16, 0.26, 8, alpha: 0.45), WindowSparks(c)),
        Camera = new LumiereCameraSpec(0.02, 0.012, 0),
    };

    /// <summary>叶隙 (q): dappled light through leaves, swaying in the wind.</summary>
    public static LumiereProfile Komorebi { get; } = Make("lattice-komorebi", "叶隙", LumiereMood.Quiet,
        Rig([
            Shaft with
            {
                X = 0.35, Y = -0.05, Angle = 1.35, Spread = 0.2, Width = 0.2, Length = 1.3, Softness = 0.7, Intensity = 1, Streaks = 0.2, Core = 0.3,
                Gobo = new LumiereGoboSpec(LumiereGoboPattern.Leaves, 7, 0.52, 0.12), Sway = new LumiereOscillation(0.03, 7, 0),
            },
            Shaft with
            {
                X = 0.72, Y = -0.05, Angle = 1.8, Spread = 0.14, Width = 0.12, Length = 1.1, Softness = 0.7, Intensity = 0.7, Streaks = 0.2, Core = 0.3,
                Gobo = new LumiereGoboSpec(LumiereGoboPattern.Leaves, 8, 0.55, 0.1), Sway = new LumiereOscillation(0.03, 8, 0.4),
            },
        ], fog => fog with { Density = 0.9, DriftX = 0.01 }, noGlare: true),
        LumiereTypography.Vertical, new LumiereDecaySpec(0.8, 1.2)) with
    {
        LineArt = c => D.Merge(D.Canopy(c.Aspect, c.Random), ScatteredSparks(c.Aspect, 12, c.Random, 0.3)),
        Motes = Motes with { Count = 220, DriftX = 0.01, DriftY = 0.004, Swirl = 0.03 },
        Region = new LumiereRegion(0.5, 0.54, 0.72, 0.6),
    };

    /// <summary>殿堂 (l): a row of tall windows throws parallel slanted beams through layered haze.</summary>
    public static LumiereProfile Nave { get; } = Make("lattice-nave", "殿堂", LumiereMood.Loud,
        Rig([.. new[] { 0.12, 0.34, 0.56, 0.78 }.Select((x, i) => WindowBeam with
        {
            X = x, Y = -0.02, Angle = 1.1, Width = 0.06, Spread = 0.05, Length = 1.5, Softness = 0.5, Intensity = 0.75, Streaks = 0.5, StreakFreq = 6, Core = 0.5,
            Sway = new LumiereOscillation(0.008, 19, i * 0.2),
        })], fog => fog with { Density = 1.2, DriftX = 0.006, DriftY = -0.03 }, Glare with { X = 0.45, Y = 0.0, Radius = 0.25, Intensity = 0.6, Streak = 0.3 }),
        LumiereTypography.Crossed, new LumiereDecaySpec(1.2, 0.8)) with
    {
        LineArt = c => LumiereLineArtSpec.Merge(D.TallWindows(c.Aspect * 0.05, c.Aspect * 0.95, -0.08, 0.26, 4), ScatteredSparks(c.Aspect, 18, c.Random, 0.3)),
        Starfall = Starfall with { Rain = 80, RainSpread = 1.2 },
    };

    public static IReadOnlyList<LumiereProfile> All { get; } = [Blinds, Cross, Arch, Rose, Slit, Shoji, Grille, Sweep, Komorebi, Nave];
}
