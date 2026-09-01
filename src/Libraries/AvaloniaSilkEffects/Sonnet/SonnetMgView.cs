using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Persistent, seek-stable background particles owned by one Sonnet shot.</summary>
internal sealed class SonnetMgView
{
    private readonly List<ParticleView> _particles = [];
    private readonly List<ParticleView> _icons = [];
    private float _smoothedIconAudio;
    private double? _lastUpdateTime;

    internal SonnetMgView(EffectContainer root, EffectContainer? fixedGeometryLayer,
        SonnetTheme theme, SonnetShotKind kind,
        float width, float height, uint seed, bool buildParticles)
    {
        Root = root;
        FixedGeometryLayer = fixedGeometryLayer;
        ParticleLayer = new EffectContainer();
        Root.Add(ParticleLayer);
        if (buildParticles)
            BuildParticles(theme, kind, width, height, seed);
    }

    internal EffectContainer Root { get; }
    internal EffectContainer ParticleLayer { get; }
    internal EffectContainer? FixedGeometryLayer { get; }
    internal int ParticleCount => _particles.Count;

    internal IReadOnlyList<SonnetParticleSnapshot> Snapshot() => _particles.Select(particle =>
        new SonnetParticleSnapshot(particle.Node.Position, particle.Node.Scale,
            particle.Node.Rotation, particle.Node.Alpha)).ToArray();

    internal void Update(double time, double shotStartTime, double shotEndTime, SonnetAudioFrame audio,
        Vector2 cameraOffset, float cameraScale, float cameraRotation)
    {
        // Folia's non-icon background decor is a static print layer. It moves with
        // the owning shot/camera, but does not independently orbit or swim.
        foreach (var particle in _particles)
        {
            particle.Node.Position = particle.BasePosition;
            particle.Node.Rotation = particle.BaseRotation;
            if (!particle.IsIcon)
            {
                particle.Node.Scale = Vector2.One;
                particle.Node.Alpha = 1;
            }
        }

        ParticleLayer.Position = cameraOffset * 0.4f;
        ParticleLayer.Rotation = (float)((time - shotStartTime) * 0.05);
        ParticleLayer.Scale = new(1 + (cameraScale - 1) * 0.3f);
        if (FixedGeometryLayer is not null)
            FixedGeometryLayer.Rotation = -cameraRotation;

        var audioEnergy = NormalizeAudio(audio.Bass) * 0.34f
            + NormalizeAudio(audio.Vocal) * 0.52f
            + NormalizeAudio(audio.Power) * 0.14f;
        var gatedEnergy = Math.Max(0, (audioEnergy - 0.08f) / 0.92f);
        var targetIconAudio = Math.Min(1, MathF.Pow(gatedEnergy, 0.68f) * 1.35f);
        var delta = _lastUpdateTime is null ? double.PositiveInfinity : time - _lastUpdateTime.Value;
        if (!double.IsFinite(delta) || delta < 0 || delta > 0.25)
        {
            // A seek must land immediately on a deterministic frame instead of
            // inheriting smoothing state from the previously displayed shot.
            _smoothedIconAudio = targetIconAudio;
        }
        else
        {
            var frameSmoothing = targetIconAudio > _smoothedIconAudio ? 0.34f : 0.16f;
            var timeAdjusted = 1 - MathF.Pow(1 - frameSmoothing, (float)(delta * 60));
            _smoothedIconAudio += (targetIconAudio - _smoothedIconAudio) * timeAdjusted;
        }
        _lastUpdateTime = time;

        var sceneDuration = Math.Max(0.01, shotEndTime - shotStartTime);
        foreach (var icon in _icons)
        {
            var entryDuration = Math.Min(
                Math.Min(Math.Max(0.01, icon.PreferredDuration), Math.Max(0.08, sceneDuration * 0.18)),
                sceneDuration);
            var entryDelay = Math.Clamp(icon.EntryPhase, 0, 1) * Math.Max(0, sceneDuration - entryDuration);
            var entryProgress = Math.Clamp((time - shotStartTime - entryDelay) / entryDuration, 0, 1);
            var entryEased = 1 - Math.Pow(1 - entryProgress, 3);
            var loopPulse = (Math.Sin((time - shotStartTime) * Math.PI * 0.7 + icon.Phase) + 1) * 0.5;
            var audioScale = 1 + _smoothedIconAudio * 0.42f;
            var loopScale = 1 + loopPulse * 0.025;

            icon.Node.Alpha = Math.Min(1,
                icon.BaseAlpha * (float)entryEased * (0.72f + _smoothedIconAudio * 0.38f + (float)loopPulse * 0.03f));
            var scale = icon.BaseScale * (0.72f + (float)entryEased * 0.28f) * audioScale * (float)loopScale;
            icon.Node.Scale = new(scale);
        }
    }

    private void BuildParticles(
        SonnetTheme theme,
        SonnetShotKind kind,
        float width,
        float height,
        uint seed)
    {
        var variant = SonnetVariantResolver.BackgroundDecor(seed);
        var count = kind == SonnetShotKind.TypeImpact ? 24 : 12;
        for (var index = 0; index < count; index++)
        {
            var size = 4 + (seed + (uint)index) % 12;
            var shape = ResolveShape(variant, (int)((seed + (uint)index) % 3));
            var color = index % 2 == 0 ? theme.Primary : theme.Secondary;
            var alpha = 0.55f + Hash(seed, index, 59) * 0.3f;
            var isIcon = IsDefaultIconSlot(index, count);
            var node = isIcon
                ? BuildFlowerNode(size, theme.Accent with { A = 1 })
                : BuildParticleNode(shape, size, color with { A = alpha });
            var placement = ResolvePlacement(variant, index, count, seed, width, height);
            node.Position = placement.Position;
            node.Rotation = placement.Rotation;
            if (isIcon) node.Alpha = 0;
            ParticleLayer.Add(node);
            var iconSeed = (ulong)seed + (ulong)index * 17;
            var particle = new ParticleView(node, placement.Position, placement.Rotation, isIcon)
            {
                BaseAlpha = isIcon ? 0.85f : 1,
                PreferredDuration = 0.62 + iconSeed % 4 * 0.08,
                Phase = iconSeed % 31 * 0.2,
            };
            _particles.Add(particle);
            if (isIcon) _icons.Add(particle);
        }

        for (var index = 0; index < _icons.Count; index++)
        {
            _icons[index].EntryPhase = _icons.Count <= 1
                ? 0.12
                : 0.04 + index / (double)(_icons.Count - 1) * 0.82;
        }
    }

    private static bool IsDefaultIconSlot(int index, int particleCount)
    {
        // Folia resolves an empty lyricsIcons list to Lucide Flower and reserves
        // ceil(particleCount / 4) evenly distributed particle slots for it.
        var iconParticleCount = (int)Math.Ceiling(particleCount / 4d);
        var previousBand = index * iconParticleCount / particleCount;
        var currentBand = (index + 1) * iconParticleCount / particleCount;
        return currentBand != previousBand;
    }

    private static EffectContainer BuildFlowerNode(float size, EffectColor color)
    {
        var root = new EffectContainer();
        var orbit = size * 1.65f;
        var petalRadius = size * 1.35f;
        for (var index = 0; index < 5; index++)
        {
            var angle = -MathF.PI / 2 + MathF.Tau * index / 5;
            var petal = new EffectContainer
            {
                Position = new(MathF.Cos(angle) * orbit, MathF.Sin(angle) * orbit),
            };
            AddRing(petal, petalRadius, color);
            root.Add(petal);
        }
        AddRing(root, size * 0.85f, color);
        return root;
    }

    private static int ResolveShape(int variant, int index) => variant switch
    {
        1 => new[] { 3, 4, 5 }[index],
        2 => new[] { 6, 7, 0 }[index],
        3 => new[] { 8, 1, 7 }[index],
        4 => new[] { 5, 3, 2 }[index],
        5 => new[] { 9, 6, 4 }[index],
        _ => new[] { 0, 1, 2 }[index],
    };

    private static ParticlePlacement ResolvePlacement(
        int variant,
        int index,
        int count,
        uint seed,
        float width,
        float height)
    {
        var hw = width / 2;
        var hh = height / 2;
        var radius = Math.Min(width, height);
        float Jitter(int salt, float range) => (Hash(seed, index, salt) - 0.5f) * range;
        var baseRotation = Hash(seed, index, 11) * MathF.Tau;

        return variant switch
        {
            1 => ResolveOrbitPlacement(),
            2 => ResolveEdgeBandPlacement(),
            3 => ResolveCornerClusterPlacement(),
            4 => ResolveConstellationPlacement(),
            5 => ResolveTwinColumnPlacement(),
            _ => new(new(
                    -hw + width * Hash(seed, index, 47),
                    -hh + height * Hash(seed, index, 53)),
                baseRotation),
        };

        ParticlePlacement ResolveOrbitPlacement()
        {
            var ring = index % 2;
            var ringRadius = radius * (0.36f + ring * 0.26f);
            var angle = index / (float)count * MathF.Tau * 2 + Jitter(13, 0.35f);
            return new(new(MathF.Cos(angle) * ringRadius, MathF.Sin(angle) * ringRadius * 0.86f),
                angle + MathF.PI / 2);
        }

        ParticlePlacement ResolveEdgeBandPlacement()
        {
            var side = index % 2 == 0 ? -1 : 1;
            var divisor = Math.Max(1, count / 2);
            var t = (MathF.Floor(index / 2f) + 0.5f) / divisor;
            return new(new(
                    -hw + width * (0.06f + 0.88f * t) + Jitter(17, width * 0.03f),
                    side * hh * 0.78f + Jitter(19, height * 0.05f)),
                side < 0 ? 0 : MathF.PI);
        }

        ParticlePlacement ResolveCornerClusterPlacement()
        {
            var corner = index % 4;
            var sx = corner % 2 == 0 ? -1 : 1;
            var sy = corner < 2 ? -1 : 1;
            return new(new(
                    sx * hw * 0.68f + Jitter(23, width * 0.12f),
                    sy * hh * 0.62f + Jitter(29, height * 0.12f)),
                baseRotation);
        }

        ParticlePlacement ResolveConstellationPlacement()
        {
            const int columns = 6;
            const int rows = 4;
            var column = index % columns;
            var row = index / columns % rows;
            return new(new(
                    -hw * 0.8f + column / (float)(columns - 1) * hw * 1.6f + Jitter(31, width * 0.06f),
                    -hh * 0.72f + row / (float)(rows - 1) * hh * 1.44f + Jitter(37, height * 0.06f)),
                baseRotation);
        }

        ParticlePlacement ResolveTwinColumnPlacement()
        {
            var side = index % 2 == 0 ? -1 : 1;
            var t = (MathF.Floor(index / 2f) + 0.5f) / Math.Max(1, (int)Math.Ceiling(count / 2f));
            return new(new(
                    side * hw * 0.74f + Jitter(41, width * 0.04f),
                    -hh * 0.8f + t * hh * 1.6f + Jitter(43, height * 0.05f)),
                side < 0 ? MathF.PI : 0);
        }
    }

    private static EffectContainer BuildParticleNode(int kind, float size, EffectColor color)
    {
        var root = new EffectContainer();
        switch (kind)
        {
            case 1: // diamond
                AddLine(root, new(0, -size), new(size, 0), size * 0.3f, color);
                AddLine(root, new(size, 0), new(0, size), size * 0.3f, color);
                AddLine(root, new(0, size), new(-size, 0), size * 0.3f, color);
                AddLine(root, new(-size, 0), new(0, -size), size * 0.3f, color);
                break;
            case 2: // sparkle
                AddLine(root, new(0, -size * 1.5f), new(0, size * 1.5f), Math.Max(1, size * 0.22f), color);
                AddLine(root, new(-size * 1.5f, 0), new(size * 1.5f, 0), Math.Max(1, size * 0.22f), color);
                break;
            case 3: // ring
                AddRing(root, size, color);
                break;
            case 4: // hexagon
                AddRegularPolygon(root, size, 6, color);
                break;
            case 5: // dot
                root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = new(-size * 0.42f), Size = new(size * 0.84f), Color = color });
                break;
            case 6: // bar
                root.Add(new ShapeNode { Position = new(-size, -size * 0.18f), Size = new(size * 2, size * 0.36f), Color = color });
                break;
            case 7: // plus
                AddLine(root, new(-size, 0), new(size, 0), Math.Max(1.5f, size * 0.28f), color);
                AddLine(root, new(0, -size), new(0, size), Math.Max(1.5f, size * 0.28f), color);
                break;
            case 8: // triangle
                AddLine(root, new(0, -size), new(size * 0.9f, size * 0.7f), Math.Max(1, size * 0.18f), color);
                AddLine(root, new(size * 0.9f, size * 0.7f), new(-size * 0.9f, size * 0.7f), Math.Max(1, size * 0.18f), color);
                AddLine(root, new(-size * 0.9f, size * 0.7f), new(0, -size), Math.Max(1, size * 0.18f), color);
                break;
            case 9: // chevron
                AddLine(root, new(-size * 0.5f, -size * 0.55f), new(size * 0.35f, 0), Math.Max(1.5f, size * 0.2f), color);
                AddLine(root, new(size * 0.35f, 0), new(-size * 0.5f, size * 0.55f), Math.Max(1.5f, size * 0.2f), color);
                break;
            default: // square
                root.Add(new ShapeNode { Position = new(-size / 2), Size = new(size), Color = color });
                break;
        }
        return root;
    }

    private static void AddRing(EffectContainer root, float radius, EffectColor color)
    {
        var points = Enumerable.Range(0, 33)
            .Select(index =>
            {
                var angle = MathF.Tau * index / 32;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            })
            .ToArray();
        for (var index = 1; index < points.Length; index++)
            AddLine(root, points[index - 1], points[index], Math.Max(1, radius * 0.22f), color);
    }

    private static void AddRegularPolygon(EffectContainer root, float radius, int sides, EffectColor color)
    {
        var points = Enumerable.Range(0, sides + 1)
            .Select(index =>
            {
                var angle = MathF.Tau * index / sides - MathF.PI / 2;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            })
            .ToArray();
        for (var index = 1; index < points.Length; index++)
            AddLine(root, points[index - 1], points[index], Math.Max(1, radius * 0.16f), color);
    }

    private static void AddLine(EffectContainer root, Vector2 start, Vector2 end, float width, EffectColor color) =>
        root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = start, Size = end - start,
            StrokeWidth = width, Color = color, BlendMode = EffectBlendMode.Screen });

    private static float Hash(uint seed, int index, int salt) =>
        (float)SonnetRandom.Hash01(seed, index, unchecked((uint)salt));
    private static float NormalizeAudio(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private sealed record ParticleView(
        EffectContainer Node, Vector2 BasePosition, float BaseRotation, bool IsIcon)
    {
        internal float BaseScale { get; init; } = 1;
        internal float BaseAlpha { get; init; } = 1;
        internal double EntryPhase { get; set; }
        internal double PreferredDuration { get; init; }
        internal double Phase { get; init; }
    }
    private readonly record struct ParticlePlacement(Vector2 Position, float Rotation);
}

internal readonly record struct SonnetParticleSnapshot(Vector2 Position, Vector2 Scale, float Rotation, float Alpha);
