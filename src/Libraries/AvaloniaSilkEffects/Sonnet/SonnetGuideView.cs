using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Port of Folia v0.7.2 sonnetGuides.ts using persistent joined curve meshes.</summary>
internal sealed class SonnetGuideView
{
    private const int CurveSegments = 48;
    private readonly PolylineNode _leadCurve;
    private readonly ShapeNode _leadGlow;
    private readonly ShapeNode _leadCore;
    private readonly List<TrailView> _trails = [];
    private readonly List<BurstView> _bursts = [];

    internal SonnetGuideView(SonnetSemanticSegment segment, SonnetTypographyPlacement placement,
        float fontSize, SonnetTheme theme, uint seed)
    {
        Root = new EffectContainer { Position = new Vector2(placement.X, placement.Y), Alpha = 0 };
        var hero = SonnetTypographyLayout.IsEmphasis(placement.Role);
        var direction = placement.TimingPhase < 0.5f ? -1 : 1;
        var start = new Vector2(placement.EnterX != 0 ? placement.EnterX : direction * fontSize * 1.8f,
            placement.EnterY != 0 ? placement.EnterY : -fontSize * 0.9f);
        var color = hero ? theme.Accent : theme.Secondary;
        var cueLead = Math.Min(0.38, Math.Max(0.2, 0.18 + (segment.EndTime - segment.StartTime) * 0.1));
        StartTime = segment.StartTime - cueLead;
        EndTime = segment.StartTime + 0.65;
        MaxAlpha = hero ? 0.95f : 0.7f;

        _leadCurve = Curve(start, start * new Vector2(0.6f, 0.4f),
            start * new Vector2(0.2f, 0.1f), Vector2.Zero, color, hero);
        _leadGlow = Circle(hero ? 14 : 9, color with { A = 0.5f });
        _leadCore = Circle(hero ? 4.5f : 3, EffectColor.White);
        Root.Add(_leadCurve).Add(_leadGlow).Add(_leadCore);

        if (hero || Hash(seed, 0, 0x7101) > 0.4f)
        {
            var d = Hash(seed, 0, 0x7102) > 0.5f ? 1f : -1f;
            var y = (Hash(seed, 0, 0x7103) - 0.5f) * fontSize * 0.8f;
            AddTrail(new Vector2(-d * fontSize * 2.5f, y + fontSize * 1.5f),
                new Vector2(-d * fontSize * 0.8f, y - fontSize * 2),
                new Vector2(d * fontSize * 0.8f, y + fontSize * 2),
                new Vector2(d * fontSize * 2.5f, y - fontSize * 1.5f),
                Hash(seed, 0, 0x7104) * 0.15f, color, hero);
        }
        if (hero || Hash(seed, 1, 0x7111) > 0.6f)
        {
            var d = Hash(seed, 1, 0x7112) > 0.5f ? 1f : -1f;
            AddTrail(new Vector2(-fontSize * 2, -d * fontSize * 1.8f),
                new Vector2(fontSize * 2, d * fontSize * 1.8f),
                new Vector2(-fontSize * 2, d * fontSize * 1.8f),
                new Vector2(fontSize * 2, -d * fontSize * 1.8f),
                Hash(seed, 1, 0x7113) * 0.1f, color, hero);
        }

        var burstCount = hero ? 6 : 3;
        for (var index = 0; index < burstCount; index++)
        {
            var size = (hero ? 3 : 1.5f) + Hash(seed, index, 0x7121) * 3.5f;
            var node = BurstShape(index % 4, size, color);
            Root.Add(node);
            _bursts.Add(new BurstView(node, Hash(seed, index, 0x7122) * MathF.Tau,
                (15 + Hash(seed, index, 0x7123) * 45) * (hero ? 1 : 0.7f),
                (Hash(seed, index, 0x7124) - 0.5f) * 8));
        }
    }

    internal EffectContainer Root { get; }
    internal double StartTime { get; }
    internal double EndTime { get; }
    internal float MaxAlpha { get; }
    internal int VisibleTrailSegments => _trails.Sum(trail =>
        Math.Max(0, trail.Line.EndPointIndex - trail.Line.StartPointIndex));
    internal int TotalTrailSegments => _trails.Count * CurveSegments;

    internal void Update(double progress)
    {
        var p = Clamp01(progress);
        var drawProgress = Clamp01(p / 0.35);
        var fadeOut = 1 - Clamp01((p - 0.4) / 0.3);
        Root.Alpha = MaxAlpha;
        UpdateCurve(_leadCurve, 0, (float)drawProgress, (float)fadeOut);
        var leadHead = Point(_leadCurve, (float)drawProgress);
        SetHead(_leadGlow, leadHead, fadeOut > 0 && drawProgress > 0, (float)fadeOut);
        SetHead(_leadCore, leadHead, fadeOut > 0 && drawProgress > 0, (float)fadeOut);

        foreach (var trail in _trails)
        {
            var local = (p - trail.Delay) / 0.55;
            var visible = local > 0 && local < 1.3 && fadeOut > 0;
            var headT = (float)Math.Min(1, Math.Max(0, local));
            var tailT = (float)Math.Max(0, local - 0.35);
            UpdateCurve(trail.Line, tailT, headT, visible ? (float)fadeOut : 0);
            var head = Point(trail.Line, headT);
            SetHead(trail.Head, head, visible && headT is > 0 and < 1, (float)fadeOut);
            trail.Ring.Position = head;
            trail.Ring.IsVisible = visible && headT is > 0 and < 1;
            trail.Ring.Alpha = (float)fadeOut * 0.4f;
        }

        var burstProgress = Clamp01((p - 0.3) / 0.7);
        var ease = 1 - Math.Pow(1 - burstProgress, 3);
        foreach (var burst in _bursts)
        {
            burst.Node.Position = new Vector2(MathF.Cos(burst.Angle) * burst.Speed * (float)ease,
                MathF.Sin(burst.Angle) * burst.Speed * (float)ease);
            burst.Node.Rotation = burst.RotationSpeed * (float)burstProgress;
            burst.Node.Alpha = (float)(1 - burstProgress);
            burst.Node.Scale = new Vector2(1 - (float)burstProgress * 0.4f);
        }
    }

    private void AddTrail(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        float delay, EffectColor color, bool hero)
    {
        var line = Curve(p0, p1, p2, p3, color, hero);
        var head = Circle(hero ? 7 : 4, color with { A = 0.9f });
        var ring = Ring(hero ? 20 : 12, color, hero ? 2 : 1);
        Root.Add(line).Add(ring).Add(head);
        _trails.Add(new TrailView(line, head, ring, delay));
    }

    private static PolylineNode Curve(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        EffectColor color, bool hero) => new()
    {
        Points = Enumerable.Range(0, CurveSegments + 1)
            .Select(index => Bezier(p0, p1, p2, p3, index / (float)CurveSegments)).ToArray(),
        StartPointIndex = 0,
        EndPointIndex = 1,
        TailWidth = hero ? 1.4f : 0.8f,
        HeadWidth = hero ? 7.5f : 4.5f,
        TailAlpha = 0.05f,
        HeadAlpha = hero ? 0.9f : 0.65f,
        Color = color,
        BlendMode = EffectBlendMode.Screen,
        IsVisible = false,
    };

    private static void UpdateCurve(PolylineNode line, float tailT, float headT, float alpha)
    {
        line.IsVisible = alpha > 0 && headT > tailT;
        if (!line.IsVisible) return;
        line.StartPointIndex = Math.Clamp((int)MathF.Floor(tailT * CurveSegments), 0, CurveSegments - 1);
        line.EndPointIndex = Math.Clamp((int)MathF.Ceiling(headT * CurveSegments), line.StartPointIndex + 1, CurveSegments);
        line.Alpha = alpha;
    }

    private static Vector2 Point(PolylineNode line, float progress) =>
        line.Points[Math.Clamp((int)MathF.Round(progress * CurveSegments), 0, CurveSegments)];

    private static void SetHead(ShapeNode node, Vector2 center, bool visible, float alpha)
    {
        node.IsVisible = visible;
        node.Position = center - node.Size * 0.5f;
        node.Alpha = alpha;
    }

    private static ShapeNode Circle(float radius, EffectColor color) => new()
    {
        Shape = EffectShapeKind.Ellipse,
        Size = new Vector2(radius * 2),
        Color = color,
        BlendMode = EffectBlendMode.Screen,
        IsVisible = false,
    };

    private static EffectContainer Ring(float radius, EffectColor color, float width)
    {
        var root = new EffectContainer { IsVisible = false, BlendMode = EffectBlendMode.Screen };
        var previous = new Vector2(radius, 0);
        for (var index = 1; index <= 24; index++)
        {
            var angle = index / 24f * MathF.Tau;
            var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = previous,
                Size = point - previous, StrokeWidth = width, Color = color, BlendMode = EffectBlendMode.Screen });
            previous = point;
        }
        return root;
    }

    private static EffectContainer BurstShape(int kind, float size, EffectColor color)
    {
        var root = new EffectContainer();
        if (kind == 0)
            root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = new Vector2(-size), Size = new Vector2(size * 2), Color = color });
        else if (kind == 1)
            root.Add(new ShapeNode { Position = new Vector2(-size), Size = new Vector2(size * 2), Color = color });
        else
        {
            root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = new Vector2(-size, 0), Size = new Vector2(size * 2, 0), StrokeWidth = 2, Color = color });
            root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = new Vector2(0, -size), Size = new Vector2(0, size * 2), StrokeWidth = 2, Color = color });
        }
        return root;
    }

    private static float Hash(uint seed, int index, int salt) =>
        (float)SonnetRandom.Hash01(seed, index, unchecked((uint)salt));
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        var mt = 1 - t;
        return a * (mt * mt * mt) + b * (3 * mt * mt * t) + c * (3 * mt * t * t) + d * (t * t * t);
    }

    private sealed record TrailView(PolylineNode Line, ShapeNode Head, EffectContainer Ring, float Delay);
    private sealed record BurstView(EffectContainer Node, float Angle, float Speed, float RotationSpeed);
}
