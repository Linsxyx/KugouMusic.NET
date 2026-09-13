using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Folia's sonnetFrameDecor.ts: open sides and staggered corner ornaments.</summary>
internal sealed class SonnetFrameDecorView
{
    private readonly List<(ShapeNode Node, Vector2 Length, int Side, float Offset, float Total)> _sides = [];
    private readonly List<(EffectNode Node, int Corner)> _ornaments = [];

    internal static (bool Applied, int Variant) ResolveSpec(SonnetSemanticSegment segment)
    {
        var hash = SonnetRandom.Hash(FormattableString.Invariant($"{segment.Text}:{segment.StartOffset}:{segment.EndOffset}:frame-decor"));
        return ((hash & 1023) / 1024d < 0.4, (int)((hash >> 10) % 4));
    }

    internal static Vector2 LocalDimensions(SonnetTypographyPlacement placement)
    {
        var turns = MathF.Round(placement.Rotation / (MathF.PI / 2));
        return MathF.Abs(placement.Rotation - turns * MathF.PI / 2) < 0.000001f && MathF.Abs(turns % 2) == 1
            ? new Vector2(placement.MeasuredHeight, placement.MeasuredWidth)
            : new Vector2(placement.MeasuredWidth, placement.MeasuredHeight);
    }

    internal SonnetFrameDecorView(SonnetTypographyPlacement placement, float fontSize, SonnetTheme theme,
        int variant, double firstGlyphStart, double shotStart, double shotEnd)
    {
        Root = new EffectContainer { Position = new Vector2(placement.X, placement.Y), Rotation = placement.Rotation, Alpha = 0 };
        StartTime = firstGlyphStart;
        EndTime = firstGlyphStart + SonnetMotion.GlyphMotionDuration(shotStart, shotEnd) * 1.25;
        var pad = Math.Clamp(fontSize * 0.22f, 8, 20);
        var half = LocalDimensions(placement) / 2 + new Vector2(pad);
        var gap = Math.Clamp(Math.Min(half.X, half.Y) * (variant == 1 ? 0.42f : 0.3f), 6, 30);
        var color = theme.Primary with { A = 0.96f };
        var stroke = Math.Clamp(fontSize * 0.03f, 1.2f, 2.2f);
        Vector2[] signs = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        for (var side = 0; side < 4; side++)
        {
            var corner = signs[side] * half;
            var next = signs[(side + 1) % 4] * half;
            var direction = Vector2.Normalize(next - corner);
            var start = corner + direction * gap;
            var end = next - direction * gap;
            var length = Vector2.Distance(start, end);
            var dash = Math.Clamp(fontSize * 0.14f, 5, 8);
            if (variant == 3)
            {
                for (var offset = 0f; offset + dash <= length + 0.001f; offset += dash * 1.7f)
                    AddSide(start + direction * offset, direction * dash, side, offset, length, stroke, color);
            }
            else AddSide(start, end - start, side, 0, length, stroke, color);

            var ornament = new EffectContainer { Position = corner };
            Root.Add(ornament);
            _ornaments.Add((ornament, side));
            var sign = signs[side];
            switch (variant)
            {
                case 0 or 2:
                {
                    var arm = variant == 0 ? Math.Clamp(pad * 0.8f, 5, 12) : Math.Clamp(pad, 6, 14);
                    var offset = variant == 0 ? 3 : 2.5f;
                    AddBracket(ornament, sign * offset, sign, arm, stroke * (variant == 2 ? 1.4f : 1), color with { A = 0.9f });
                    if (variant == 2)
                    {
                        AddBracket(ornament, sign * (offset + 4), sign, arm, stroke * 0.8f, color with { A = 0.5f });
                        var diamond = Math.Clamp(pad * 0.45f, 3.5f, 7);
                        var middle = new EffectContainer { Position = (start + end) / 2 };
                        middle.Add(new PolygonNode { Points = [new Vector2(0, -diamond), new Vector2(diamond, 0), new Vector2(0, diamond), new Vector2(-diamond, 0)], Color = color });
                        Root.Add(middle);
                        _ornaments.Add((middle, side));
                    }

                    break;
                }
                case 1:
                {
                    var radius = Math.Clamp(pad * 0.55f, 4, 9);
                    foreach (var axis in new Vector2[] { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) })
                        AddCircle(ornament, axis * radius, radius * 0.44f, color with { A = 0.8f });
                    AddCircle(ornament, Vector2.Zero, radius * 0.3f, EffectColor.White with { A = 0.5f });
                    break;
                }
                default:
                {
                    var size = Math.Clamp(pad * 0.6f, 4, 9);
                    var diagonal = sign / MathF.Sqrt(2);
                    var perpendicular = new Vector2(sign.Y, -sign.X) / MathF.Sqrt(2);
                    ornament.Add(new PolygonNode { Points = [diagonal * (2 + size), diagonal * 2 + perpendicular * size * 0.7f,
                        diagonal * 2 - perpendicular * size * 0.7f], Color = color });
                    AddCircle(ornament, Vector2.Zero, stroke, color with { A = 0.9f });
                    break;
                }
            }
        }
    }

    internal EffectContainer Root { get; }
    internal double StartTime { get; }
    internal double EndTime { get; }

    // Persistent geometry: animate lengths/transforms without rebuilding paths or textures.
    internal void Update(double time)
    {
        var progress = SonnetMotion.Clamp01((time - StartTime) / Math.Max(0.001, EndTime - StartTime));
        var eased = (float)SonnetMotion.ExpoOut(progress);
        Root.Alpha = eased <= 0 ? 0 : 1;
        foreach (var side in _sides)
        {
            var traced = Math.Clamp((eased - side.Side * 0.2f) / 0.4f, 0, 1) * side.Total;
            var length = side.Length.Length();
            var growth = Math.Clamp((traced - side.Offset) / Math.Max(0.001f, length), 0, 1);
            side.Node.Size = side.Length * growth;
            side.Node.IsVisible = growth > 0;
        }
        foreach (var (node, corner) in _ornaments)
        {
            var local = SonnetMotion.Clamp01((eased - 0.35 - corner * 0.14) / 0.3);
            var growth = (float)SonnetMotion.ElasticOut(local);
            node.IsVisible = local > 0 && growth > 0.02f;
            node.Scale = new Vector2(growth);
        }
    }

    private void AddSide(Vector2 start, Vector2 length, int side, float offset, float total, float width, EffectColor color)
    {
        var line = new ShapeNode { Shape = EffectShapeKind.Line, Position = start, Size = Vector2.Zero, StrokeWidth = width, Color = color };
        Root.Add(line);
        _sides.Add((line, length, side, offset, total));
    }

    private static void AddBracket(EffectContainer root, Vector2 center, Vector2 sign, float arm, float width, EffectColor color) =>
        root.Add(new PolylineNode { Points = [center + new Vector2(sign.X * arm, 0), center, center + new Vector2(0, sign.Y * arm)],
            TailWidth = width, HeadWidth = width, TailAlpha = 1, HeadAlpha = 1, Color = color });

    private static void AddCircle(EffectContainer root, Vector2 center, float radius, EffectColor color) =>
        root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = center - new Vector2(radius), Size = new Vector2(radius * 2), Color = color });
}
