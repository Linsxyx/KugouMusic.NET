using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Persistent, seek-stable background particles owned by one Sonnet shot.</summary>
internal sealed class SonnetMgView
{
    private readonly List<ParticleView> _particles = [];

    internal SonnetMgView(EffectContainer root, SonnetTheme theme, float width, float height,
        uint seed, float density, bool buildParticles)
    {
        Root = root;
        ParticleLayer = new EffectContainer();
        Root.Add(ParticleLayer);
        if (buildParticles)
            BuildParticles(theme, Math.Min(width, height), seed, density);
    }

    internal EffectContainer Root { get; }
    internal EffectContainer ParticleLayer { get; }
    internal int ParticleCount => _particles.Count;

    internal IReadOnlyList<SonnetParticleSnapshot> Snapshot() => _particles.Select(particle =>
        new SonnetParticleSnapshot(particle.Node.Position, particle.Node.Scale,
            particle.Node.Rotation, particle.Node.Alpha)).ToArray();

    internal void Update(double time, double shotStartTime, SonnetAudioFrame audio,
        Vector2 cameraOffset, float cameraScale)
    {
        var elapsed = Math.Max(0, time - shotStartTime);
        var power = ClampAudio(audio.Power);
        var bass = ClampAudio(audio.Bass);
        var vocal = ClampAudio(audio.Vocal);

        foreach (var particle in _particles)
        {
            var angle = particle.Angle + (float)elapsed * particle.AngularSpeed;
            var radialPulse = 1 + 0.075f * MathF.Sin((float)elapsed * particle.SwimSpeed + particle.Phase);
            var swim = new Vector2(
                MathF.Cos((float)elapsed * particle.SwimSpeed * 0.73f + particle.Phase),
                MathF.Sin((float)elapsed * particle.SwimSpeed + particle.Phase)) * particle.SwimRadius;
            var orbit = new Vector2(MathF.Cos(angle), MathF.Sin(angle) * particle.Eccentricity) *
                (particle.Radius * radialPulse);
            var parallax = cameraOffset * (0.22f + particle.Depth * 0.28f);
            particle.Node.Position = orbit + swim + parallax;
            particle.Node.Rotation = particle.BaseRotation + (float)elapsed * particle.RotationSpeed;
            var audioScale = 1 + power * 0.15f + bass * particle.BassResponse;
            var pulse = 1 + MathF.Sin((float)elapsed * 1.7f + particle.Phase) * 0.055f;
            particle.Node.Scale = new(particle.BaseScale * audioScale * pulse *
                (1 + (cameraScale - 1) * particle.Depth * 0.3f));
            particle.Node.Alpha = Math.Clamp(particle.BaseAlpha *
                (0.82f + vocal * 0.28f + power * 0.18f), 0, 1);
        }
    }

    private void BuildParticles(SonnetTheme theme, float radius, uint seed, float density)
    {
        var count = Math.Clamp((int)MathF.Round(18 * density), 8, 48);
        for (var index = 0; index < count; index++)
        {
            var angle = Hash(seed, index, 0x5041) * MathF.Tau;
            var orbitRadius = radius * (0.18f + Hash(seed, index, 0x5044) * 0.72f);
            var size = 3 + Hash(seed, index, 0x5053) * 8;
            var color = index % 2 == 0 ? theme.Accent : theme.Secondary;
            var node = BuildParticleNode(index % 4, size, color with { A = 0.72f });
            ParticleLayer.Add(node);
            _particles.Add(new(node, angle, orbitRadius,
                0.035f + Hash(seed, index, 0x5057) * 0.075f,
                0.76f + Hash(seed, index, 0x5045) * 0.22f,
                Hash(seed, index, 0x5050) * MathF.Tau,
                0.34f + Hash(seed, index, 0x5056) * 0.62f,
                radius * (0.008f + Hash(seed, index, 0x5058) * 0.024f),
                Hash(seed, index, 0x5042),
                0.62f + Hash(seed, index, 0x5052) * 0.28f,
                0.72f + Hash(seed, index, 0x504c) * 0.5f,
                Hash(seed, index, 0x5054) * MathF.Tau,
                (Hash(seed, index, 0x5051) - 0.5f) * 0.8f,
                0.08f + Hash(seed, index, 0x5046) * 0.16f));
        }
    }

    private static EffectContainer BuildParticleNode(int kind, float size, EffectColor color)
    {
        var root = new EffectContainer();
        switch (kind)
        {
            case 0:
                AddLine(root, new(0, -size), new(size, 0), size * 0.3f, color);
                AddLine(root, new(size, 0), new(0, size), size * 0.3f, color);
                AddLine(root, new(0, size), new(-size, 0), size * 0.3f, color);
                AddLine(root, new(-size, 0), new(0, -size), size * 0.3f, color);
                break;
            case 1:
                root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = new(-size * 0.42f), Size = new(size * 0.84f), Color = color });
                break;
            case 2:
                AddLine(root, new(-size, 0), new(size, 0), Math.Max(1.5f, size * 0.28f), color);
                AddLine(root, new(0, -size), new(0, size), Math.Max(1.5f, size * 0.28f), color);
                break;
            default:
                root.Add(new ShapeNode { Position = new(-size, -size * 0.18f), Size = new(size * 2, size * 0.36f), Color = color });
                break;
        }
        return root;
    }

    private static void AddLine(EffectContainer root, Vector2 start, Vector2 end, float width, EffectColor color) =>
        root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = start, Size = end - start,
            StrokeWidth = width, Color = color, BlendMode = EffectBlendMode.Screen });

    private static float Hash(uint seed, int index, int salt) =>
        (float)SonnetRandom.Hash01(seed, index, unchecked((uint)salt));
    private static float ClampAudio(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private sealed record ParticleView(EffectContainer Node, float Angle, float Radius, float AngularSpeed,
        float Eccentricity, float Phase, float SwimSpeed, float SwimRadius, float Depth, float BaseAlpha,
        float BaseScale, float BaseRotation, float RotationSpeed, float BassResponse);
}

internal readonly record struct SonnetParticleSnapshot(Vector2 Position, Vector2 Scale, float Rotation, float Alpha);
