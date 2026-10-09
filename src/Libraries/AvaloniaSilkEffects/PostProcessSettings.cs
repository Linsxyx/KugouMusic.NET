using System.Numerics;

namespace AvaloniaSilkEffects;

public sealed class PostProcessSettings
{
    internal bool UseSonnetPasses { get; set; }
    internal float SonnetNoiseSeed { get; set; }
    /// <summary>Resolution used by offscreen effects. Values below one reduce Retina fill cost.</summary>
    public float ResolutionScale { get; set; } = 0.75f;
    /// <summary>Scene MSAA sample count; one disables it. Limited by GL_MAX_SAMPLES.</summary>
    public int MultisampleCount { get; set; } = 1;
    public float Blur { get; set; }
    public float Glow { get; set; }
    public float Grain { get; set; }
    public float Contrast { get; set; }
    public float RgbSplit { get; set; }
    public float Halftone { get; set; }
    public float Vignette { get; set; }
    public float LensDistortion { get; set; }
    public float LensDispersion { get; set; }
    public float Glitch { get; set; }
    /// <summary>Intensity of the multi-scale bloom built from pixels above <see cref="BloomThreshold"/>.</summary>
    public float Bloom { get; set; }
    /// <summary>Brightness (0..1, premultiplied) where bloom starts; a soft knee eases it in.</summary>
    public float BloomThreshold { get; set; } = 0.62f;
    /// <summary>Spread of the bloom, 0 keeps it tight and 1 reaches across the frame.</summary>
    public float BloomRadius { get; set; } = 0.6f;
    public EffectColor BloomTint { get; set; } = EffectColor.White;
    /// <summary>Horizontal anamorphic streak drawn from the same bright pixels as the bloom.</summary>
    public float BloomStreak { get; set; }
    /// <summary>Radial motion blur towards <see cref="ZoomBlurCenter"/>; 1 smears about 12% of the radius.</summary>
    public float ZoomBlur { get; set; }
    /// <summary>Zoom blur origin in normalized top-left screen coordinates.</summary>
    public Vector2 ZoomBlurCenter { get; set; } = new(0.5f, 0.5f);
    public float Time { get; set; }
    public float Seed { get; set; }
    public Matrix4x4 ColorMatrix { get; set; } = Matrix4x4.Identity;

    public bool IsEnabled =>
        Blur > 0.001f || Glow > 0.001f || Grain > 0.001f || Contrast > 0.001f ||
        RgbSplit > 0.001f || Halftone > 0.001f || Vignette > 0.001f ||
        LensDistortion > 0.001f || LensDispersion > 0.001f || Glitch > 0.001f ||
        Bloom > 0.001f || ZoomBlur > 0.001f ||
        ColorMatrix != Matrix4x4.Identity;

    public void Reset()
    {
        UseSonnetPasses = false;
        MultisampleCount = 1;
        Blur = Glow = Grain = Contrast = RgbSplit = Halftone = Vignette = 0;
        LensDistortion = LensDispersion = Glitch = 0;
        Bloom = BloomStreak = ZoomBlur = 0;
        BloomThreshold = 0.62f;
        BloomRadius = 0.6f;
        BloomTint = EffectColor.White;
        ZoomBlurCenter = new Vector2(0.5f, 0.5f);
        ColorMatrix = Matrix4x4.Identity;
    }
}
