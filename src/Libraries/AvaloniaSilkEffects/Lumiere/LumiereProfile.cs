using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using AvaloniaSilkEffects.Lumiere.Rigs;

namespace AvaloniaSilkEffects.Lumiere;

// Folia lumiere/types.ts. A profile (a "light position", the look of a shot) is pure data: beams + fog +
// motes + front bokeh + line art + text region + camera, interpreted by one scene builder.

public enum LumiereMood
{
    Quiet,
    Neutral,
    Loud,
}

public enum LumiereTypography
{
    /// <summary>Mostly horizontal.</summary>
    Horizontal,
    /// <summary>Mostly vertical, right to left.</summary>
    Vertical,
    /// <summary>The current line horizontal, neighbours free in both orientations.</summary>
    Crossed,
}

public readonly record struct LumiereRigContext(double Aspect, LumiereRng Random);

/// <summary>Glyph decay: how long after lighting a glyph starts drifting off, and how strongly.</summary>
public readonly record struct LumiereDecaySpec(double Strength, double Delay);

public enum LumiereBurstMode
{
    /// <summary>Chosen glyphs each burst as they are sung.</summary>
    Sung,
    /// <summary>The whole line bursts in one sweep at its start.</summary>
    Sweep,
}

/// <summary>EVA-style cross flashes along a line.</summary>
public sealed record LumiereBurstSpec(LumiereBurstMode Mode, double Density, int Every, int Offset, double Size, Vector3 Tint);

public sealed record LumiereStarfallSpec(
    int Stars,
    double Opening,
    int Rain,
    double RainSpeed,
    double RainSpread,
    double Brightness)
{
    /// <summary>When the main beam ignites, relative to the shot start.</summary>
    public double Ignition => Opening * 0.45;
}

public readonly record struct LumiereCameraSpec(double Push, double DriftX, double DriftY);

public sealed record LumiereProfile
{
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public required string Family { get; init; }
    public required LumiereMood Mood { get; init; }
    public required Func<LumiereRigContext, LumiereLightRig> Light { get; init; }
    public required LumiereMotesSpec Motes { get; init; }
    /// <summary>Line art for the shot (seeded by the shot); defaults to protractor halo + viewfinder + sparks.</summary>
    public Func<LumiereRigContext, LumiereLineArtSpec> LineArt { get; init; } = context => LumiereRigBase.StandardArt(context);
    /// <summary>Foreground bokeh above the text; null for none.</summary>
    public LumiereMotesSpec? Front { get; init; }
    /// <summary>Text region (frame fractions, center + size).</summary>
    public LumiereRegion Region { get; init; } = new(0.5, 0.54, 0.72, 0.5);
    /// <summary>Current line font size, as a fraction of the frame height.</summary>
    public double HeroSize { get; init; } = 0.085;
    public required LumiereTypography Typography { get; init; }
    public required LumiereDecaySpec Decay { get; init; }
    public LumiereBurstSpec? Burst { get; init; }
    public LumiereStarfallSpec? Starfall { get; init; }
    /// <summary>Largest background lyric fragment size (frame height); Folia defaults to 0.34.</summary>
    public double EchoSize { get; init; } = 0.34;
    public double ArtGain { get; init; } = 1;
    public LumiereCameraSpec Camera { get; init; } = new(0.035, 0, -0.006);
}

public sealed record LumiereSceneTuning
{
    public double LightIntensity { get; init; } = 1;
    /// <summary>How much bass lifts the beams (1 = +15% at full bass).</summary>
    public double AudioResponse { get; init; } = 1;
    public double FogDensity { get; init; } = 1;
    public float DarkField { get; init; } = 0.75f;
    public double MoteAmount { get; init; } = 1;
    /// <summary>Graphics group bloom multiplier.</summary>
    public float Bloom { get; init; } = 1;
    public float TextBloom { get; init; } = 1;
    public double UnlitOpacity { get; init; } = 0.22;
    public int WindowNeighbors { get; init; } = 2;
    public double Decay { get; init; } = 1;
    public double Echo { get; init; } = 1;
    public double FogOctaves { get; init; } = 5;
    public bool LineArt { get; init; } = true;
    public bool FrontBokeh { get; init; } = true;
    public bool OverlayFrame { get; init; } = true;
    public bool Trails { get; init; }
    public bool HideTrails { get; init; }
    public bool TextOnly { get; init; }
    /// <summary>Folia showText: false hides the lyrics (and the background echo, which is made of them); light unchanged.</summary>
    public bool ShowText { get; init; } = true;
    public bool KeywordColors { get; init; } = true;
    public bool ThemeIcons { get; init; } = true;
    public float ThemeColorMix { get; init; } = 0.3f;
    /// <summary>Graphics group resolution per axis (quality: full 1, balanced 0.7, low 0.5).</summary>
    public float GraphicsScale { get; init; } = 1;
}
