using Avalonia;
using System.Numerics;

namespace AvaloniaSilkEffects.Showcase;

internal abstract class ShowcaseScene : EffectScene
{
    protected PixelSize Size;
    protected EffectFrame Frame;

    public float Intensity { get; set; } = 0.65f;
    public uint Seed { get; set; } = 20260901;
    public EffectColor Accent { get; set; } = new(0.2f, 0.9f, 1f);
    public float TextRasterScale { get; set; } = 2;
    public float FilterResolutionScale { get; set; } = 0.65f;
    public abstract string Name { get; }

    public override void Resize(PixelSize pixelSize, double renderScaling) => Size = pixelSize;
    public override void Update(in EffectFrame frame)
    {
        Frame = frame;
        Device.PostProcess.ResolutionScale = FilterResolutionScale;
    }

    protected Vector2 Center => new(Size.Width * 0.5f, Size.Height * 0.5f);
    protected float Time => (float)Frame.Elapsed.TotalSeconds;
    protected float Beat => MathF.Pow(MathF.Max(0, MathF.Sin(Time * MathF.Tau * 1.8f)), 7);
}
