using System.Numerics;

namespace AvaloniaSilkEffects;

public sealed class PostProcessSettings
{
    /// <summary>Resolution used by offscreen effects. Values below one reduce Retina fill cost.</summary>
    public float ResolutionScale { get; set; } = 0.75f;
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
    public float Time { get; set; }
    public float Seed { get; set; }
    public Matrix4x4 ColorMatrix { get; set; } = Matrix4x4.Identity;

    public bool IsEnabled =>
        Blur > 0.001f || Glow > 0.001f || Grain > 0.001f || Contrast > 0.001f ||
        RgbSplit > 0.001f || Halftone > 0.001f || Vignette > 0.001f ||
        LensDistortion > 0.001f || LensDispersion > 0.001f || Glitch > 0.001f ||
        ColorMatrix != Matrix4x4.Identity;

    public void Reset()
    {
        Blur = Glow = Grain = Contrast = RgbSplit = Halftone = Vignette = 0;
        LensDistortion = LensDispersion = Glitch = 0;
        ColorMatrix = Matrix4x4.Identity;
    }
}
