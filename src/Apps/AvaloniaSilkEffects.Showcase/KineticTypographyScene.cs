using System.Numerics;

namespace AvaloniaSilkEffects.Showcase;

internal sealed class KineticTypographyScene : ShowcaseScene
{
    private readonly TextNode _hero = new()
    {
        Text = "绚烂 · KINETIC",
        FontSize = 78,
        FontWeight = 700,
        Pivot = new(260, 55),
    };
    private readonly TextNode _detail = new()
    {
        Text = "AVALONIA  ×  SILK.NET  ×  OPENGL",
        FontSize = 23,
        FontWeight = 500,
        Pivot = new(205, 20),
    };
    private readonly EffectContainer _root = new();
    private readonly List<ShapeNode> _bars = [];

    public KineticTypographyScene()
    {
        _root.Add(_hero).Add(_detail);
        for (var i = 0; i < 18; i++)
        {
            var bar = new ShapeNode { Size = new(4, 80), Alpha = 0.35f };
            _bars.Add(bar);
            _root.Add(bar);
        }
    }

    public override string Name => "Dynamic type";

    public override void Update(in EffectFrame frame)
    {
        base.Update(frame);
        var beat = Beat;
        _hero.Position = Center + new Vector2(MathF.Sin(Time * 0.7f) * 18, -25);
        _hero.Scale = Vector2.One * (1 + beat * 0.08f * Intensity);
        _hero.Rotation = MathF.Sin(Time * 0.55f) * 0.018f;
        _hero.Color = Accent;
        _hero.RasterScale = TextRasterScale;
        _detail.Position = Center + new Vector2(0, 74 + beat * 9);
        _detail.Color = new(1, 1, 1, 0.82f);
        _detail.RasterScale = TextRasterScale;

        for (var i = 0; i < _bars.Count; i++)
        {
            var angle = MathF.Tau * i / _bars.Count + Time * 0.12f;
            var radius = MathF.Min(Size.Width, Size.Height) * (0.28f + 0.03f * MathF.Sin(Time + i));
            var bar = _bars[i];
            bar.Position = Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            bar.Rotation = angle + MathF.PI * 0.5f;
            bar.Scale = new(1, 0.4f + beat * 1.8f + (i % 4) * 0.12f);
            bar.Color = i % 3 == 0 ? Accent : new EffectColor(1, 1, 1, 0.5f);
        }

        Device.PostProcess.Reset();
        Device.PostProcess.Time = Time;
        Device.PostProcess.Seed = Seed;
        Device.PostProcess.Glow = 0.18f + beat * Intensity * 0.45f;
        Device.PostProcess.Grain = 0.35f * Intensity;
        Device.PostProcess.Vignette = 0.35f;
        Device.PostProcess.RgbSplit = beat * 0.5f * Intensity;
        Device.PostProcess.Glitch = beat > 0.72f ? (beat - 0.72f) * Intensity : 0;
    }

    public override void Render(EffectRenderContext context) => context.Render(_root);
}
