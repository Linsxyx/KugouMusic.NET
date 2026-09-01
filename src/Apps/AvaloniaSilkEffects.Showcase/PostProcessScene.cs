using Avalonia;
using System.Numerics;

namespace AvaloniaSilkEffects.Showcase;

internal sealed class PostProcessScene : ShowcaseScene
{
    private readonly EffectContainer _root = new();
    private readonly TextNode _label = new()
    {
        Text = "POST / PROCESS",
        FontSize = 70,
        FontWeight = 700,
        Pivot = new(245, 54),
    };

    public PostProcessScene()
    {
        _root.Add(new ShapeNode
        {
            Position = new(70, 70),
            Size = new(540, 8),
            Color = new(1, 1, 1, 0.7f),
        });
        _root.Add(new ShapeNode
        {
            Position = new(110, 130),
            Size = new(180, 180),
            Shape = EffectShapeKind.Ellipse,
            BlendMode = EffectBlendMode.Screen,
        });
        _root.Add(_label);
    }

    public override string Name => "Post process lab";

    public override void Update(in EffectFrame frame)
    {
        base.Update(frame);
        _label.Position = Center;
        _label.Color = Accent;
        _label.RasterScale = TextRasterScale;
        _label.Rotation = MathF.Sin(Time * 0.65f) * 0.04f;
        var beat = Beat;
        Device.PostProcess.Reset();
        Device.PostProcess.Time = Time;
        Device.PostProcess.Seed = Seed;
        Device.PostProcess.Blur = 0.08f * Intensity;
        Device.PostProcess.Glow = 0.3f * Intensity;
        Device.PostProcess.Grain = 0.7f * Intensity;
        Device.PostProcess.Contrast = 0.4f * Intensity;
        Device.PostProcess.RgbSplit = (0.25f + beat * 0.6f) * Intensity;
        Device.PostProcess.Halftone = 0.75f * Intensity;
        Device.PostProcess.Vignette = 0.8f * Intensity;
        Device.PostProcess.LensDistortion = 0.35f * Intensity;
        Device.PostProcess.LensDispersion = 0.4f * Intensity;
        Device.PostProcess.Glitch = beat * Intensity;
    }

    public override void Render(EffectRenderContext context)
    {
        using (context.PushClip(new Rect(36, 36, Math.Max(1, Size.Width - 72), Math.Max(1, Size.Height - 72))))
            context.Render(_root);
    }
}
