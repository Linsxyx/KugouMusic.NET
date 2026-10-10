using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>Folia lumiere/color.ts: linear 0..1 triples; Pixi tints are 0xRRGGBB, so tints quantize to 8 bits.</summary>
public static class LumiereColor
{
    public static Vector3 White => Vector3.One;
    /// <summary>The reference image's champagne gold.</summary>
    public static Vector3 Champagne => new(1, 0.86f, 0.62f);

    public static Vector3 Mix(Vector3 a, Vector3 b, float amount) => a + (b - a) * amount;

    public static float Luminance(Vector3 rgb) => 0.2126f * rgb.X + 0.7152f * rgb.Y + 0.0722f * rgb.Z;

    /// <summary>Same as Pixi receiving <c>hexOf(rgb)</c> as a tint.</summary>
    public static EffectColor Tint(Vector3 rgb) => new(Q(rgb.X), Q(rgb.Y), Q(rgb.Z));

    private static float Q(float value) => MathF.Round(Math.Clamp(value, 0, 1) * 255) / 255;

    public static Vector3 Rgb(EffectColor color) => new(color.R, color.G, color.B);
}

public sealed record LumierePalette(Vector3 Light, Vector3 Lit, Vector3 Unlit)
{
    /// <summary>The cool blue-grey unsung glyphs lean towards.</summary>
    private static readonly Vector3 UnlitCool = new(0.62f, 0.68f, 0.8f);

    /// <summary>Scale to the brightest channel: keeps hue and drops darkness; near-black falls back.</summary>
    private static Vector3 GlowOf(Vector3 rgb, Vector3 fallback)
    {
        var peak = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
        return peak < 0.04f ? fallback : rgb / peak;
    }

    /// <summary>
    /// Folia resolveLumierePalette. themeMix 0 is champagne light (with 18% accent); higher follows the theme:
    /// light → accent, lit glyphs → primary, unsung glyphs → secondary (or primary).
    /// </summary>
    public static LumierePalette Resolve(LumiereTheme theme, float themeMix = 0)
    {
        var mix = Math.Clamp(themeMix, 0, 1);
        var accent = theme.Accent is { } a ? LumiereColor.Rgb(a) : LumiereColor.Champagne;
        var light = LumiereColor.Mix(LumiereColor.Mix(LumiereColor.Champagne, accent, 0.18f), GlowOf(accent, LumiereColor.Champagne), mix);
        light /= MathF.Max(MathF.Max(light.X, MathF.Max(light.Y, light.Z)), 1e-3f);
        var warmLit = LumiereColor.Mix(light, LumiereColor.White, 0.2f);
        var primary = GlowOf(theme.Primary is { } p ? LumiereColor.Rgb(p) : warmLit, warmLit);
        var secondary = theme.Secondary is { } s ? GlowOf(LumiereColor.Rgb(s), primary) : primary;
        return new LumierePalette(
            light,
            LumiereColor.Mix(warmLit, LumiereColor.Mix(primary, LumiereColor.White, 0.2f), mix),
            LumiereColor.Mix(LumiereColor.Mix(light, UnlitCool, 0.55f), LumiereColor.Mix(secondary, UnlitCool, 0.35f), mix));
    }
}

public enum LumiereAnimationIntensity
{
    Calm,
    Normal,
    Chaotic,
}

public sealed record LumiereTheme(
    EffectColor Background,
    EffectColor? Primary = null,
    EffectColor? Secondary = null,
    EffectColor? Accent = null,
    string FontFamily = "PingFang SC",
    int FontWeight = 500,
    LumiereAnimationIntensity AnimationIntensity = LumiereAnimationIntensity.Normal)
{
    /// <summary>Theme keywords and their colors (Folia theme.wordColors): lit keywords take the keyword's light.</summary>
    public IReadOnlyList<Text.LumiereWordColor>? WordColors { get; init; }
}

/// <summary>Folia lumiereDarkField.ts: the theme background darkened, laid under every scene.</summary>
public static class LumiereDarkField
{
    private const float BrightBackgroundLuminance = 0.18f;
    /// <summary>Light themes keep at least this much: a bright background showing through breaks the light.</summary>
    public const float BrightFloor = 0.94f;
    private const float Shade = 0.06f;

    /// <summary>Premultiplied color of the dark field.</summary>
    public static EffectColor Resolve(EffectColor background, float darkField)
    {
        var rgb = LumiereColor.Rgb(background);
        var strength = float.IsFinite(darkField) ? Math.Clamp(darkField, 0, 1) : 0;
        var alpha = LumiereColor.Luminance(rgb) > BrightBackgroundLuminance ? MathF.Max(strength, BrightFloor) : strength;
        // Pixi tints the white sprite with hexOf(color), so the color quantizes before alpha applies.
        return LumiereColor.Tint(rgb * Shade) with { A = alpha };
    }
}
