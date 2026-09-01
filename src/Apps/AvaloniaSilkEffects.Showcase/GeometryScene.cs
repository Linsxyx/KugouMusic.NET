using System.Numerics;

namespace AvaloniaSilkEffects.Showcase;

internal sealed class GeometryScene : ShowcaseScene
{
    private readonly EffectContainer _root = new();
    private readonly List<ShapeNode> _particles = [];

    public GeometryScene()
    {
        var random = new DeterministicRandom(DeterministicRandom.Hash("geometry-showcase"));
        var alphaParticles = new EffectContainer();
        var additiveParticles = new EffectContainer();
        _root.Add(alphaParticles).Add(additiveParticles);
        for (var i = 0; i < 120; i++)
        {
            var size = 3 + random.NextSingle() * 13;
            var particle = new ShapeNode
            {
                Shape = i % 3 == 0 ? EffectShapeKind.Rectangle : EffectShapeKind.Ellipse,
                Size = new(size, size),
                Pivot = new(size * 0.5f),
                Alpha = 0.3f + random.NextSingle() * 0.7f,
                BlendMode = i % 5 == 0 ? EffectBlendMode.Additive : EffectBlendMode.Alpha,
            };
            _particles.Add(particle);
            (particle.BlendMode == EffectBlendMode.Additive ? additiveParticles : alphaParticles).Add(particle);
        }
    }

    public override string Name => "Geometry field";

    public override void Update(in EffectFrame frame)
    {
        base.Update(frame);
        var radius = MathF.Min(Size.Width, Size.Height) * 0.38f;
        var beat = Beat;
        for (var i = 0; i < _particles.Count; i++)
        {
            var layer = 0.2f + (i % 9) / 9f;
            var angle = i * 2.3999632f + Time * (0.08f + layer * 0.16f);
            var wave = MathF.Sin(Time * 1.7f + i * 0.31f);
            var distance = radius * MathF.Sqrt((i + 1f) / _particles.Count) * (1 + beat * 0.12f * Intensity);
            var particle = _particles[i];
            particle.Position = Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            particle.Scale = Vector2.One * (0.65f + layer * 0.8f + wave * 0.18f + beat * 0.45f);
            particle.Rotation = -angle + Time * 0.3f;
            particle.Color = i % 4 == 0 ? Accent : new EffectColor(0.9f, 0.45f + layer * 0.4f, 1, 0.8f);
        }

        Device.PostProcess.Reset();
        Device.PostProcess.Time = Time;
        Device.PostProcess.Seed = Seed;
        Device.PostProcess.Glow = 0.45f * Intensity + beat * 0.4f;
        Device.PostProcess.LensDistortion = 0.2f * Intensity;
        Device.PostProcess.LensDispersion = 0.22f * Intensity;
        Device.PostProcess.Vignette = 0.5f;
        Device.PostProcess.Contrast = 0.12f;
        Device.PostProcess.Grain = 0.18f;
        Device.PostProcess.Glitch = 0;
    }

    public override void Render(EffectRenderContext context) => context.Render(_root);
}
