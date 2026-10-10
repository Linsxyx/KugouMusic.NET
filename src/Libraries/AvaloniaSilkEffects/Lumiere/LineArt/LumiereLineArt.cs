using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere.LineArt;

/// <summary>One polyline (height units), drawn progressively.</summary>
public sealed record LumiereLinePath(
    LumierePoint[] Points,
    // Line width (height units) and base brightness 0..1.
    double Width,
    double Alpha,
    // When drawing starts (0..1 of the group's draw time) and how much of it this path takes.
    double Delay,
    double Span,
    // Dash: on and off lengths (height units).
    (double On, double Off)? Dash = null);

/// <summary>A sparkle on the art: position, diameter (height units), appearance time 0..1, twinkle phase.</summary>
public readonly record struct LumiereLineNode(LumierePoint At, double Size, double Delay, double TwinklePhase);

public sealed record LumiereLineArtSpec(IReadOnlyList<LumiereLinePath> Paths, IReadOnlyList<LumiereLineNode> Nodes)
{
    public static LumiereLineArtSpec Empty { get; } = new([], []);

    public static LumiereLineArtSpec Merge(params LumiereLineArtSpec[] specs) =>
        new([.. specs.SelectMany(spec => spec.Paths)], [.. specs.SelectMany(spec => spec.Nodes)]);
}

/// <summary>
/// Folia lineart/lineArt.ts: optical line art — polylines drawn out over time with sparkles on their nodes. Each path is
/// stroked twice (a 3.2× wide faint pass under the line); its brightness follows the light at its midpoint, so a
/// beam sweeping over the art lights it up.
/// </summary>
internal sealed class LumiereLineArtLayer(float height, LumiereLineArtSpec spec, EffectTexture star)
{
    private readonly Built[] _paths = [.. spec.Paths.Where(path => path.Points.Length > 1).Select(path => new Built(path))];
    private readonly PolylineNode _node = new() { TailAlpha = 1, HeadAlpha = 1 };
    private readonly List<Vector2> _points = new(128);

    private sealed class Built
    {
        public Built(LumiereLinePath spec)
        {
            Spec = spec;
            Lengths = new double[spec.Points.Length];
            for (var i = 1; i < spec.Points.Length; i++)
            {
                var a = spec.Points[i - 1];
                var b = spec.Points[i];
                Lengths[i] = Lengths[i - 1] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }
            Total = Lengths[^1];
            Midpoint = spec.Points[spec.Points.Length / 2];
        }

        public LumiereLinePath Spec { get; }
        public double[] Lengths { get; }
        public double Total { get; }
        public LumierePoint Midpoint { get; }
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    /// <param name="draw">Group draw progress 0..1.</param>
    /// <param name="fade">Overall brightness (enter / exit).</param>
    public void Draw(EffectPrimitiveRenderer primitives, Matrix3x2 root, double time, double draw, double fade,
        IReadOnlyList<LumiereResolvedBeam> beams, Vector3 color)
    {
        var rootScale = MathF.Sqrt(MathF.Abs(root.GetDeterminant()));
        foreach (var path in _paths)
        {
            var local = Clamp01((draw - path.Spec.Delay) / Math.Max(path.Spec.Span, 1e-3));
            var amount = (1 - Math.Pow(1 - local, 3)) * path.Total;
            if (amount <= 0) continue;
            var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, path.Midpoint.X, path.Midpoint.Y));
            // Lines outside beams stay visible (the frame and leaves sit in the dark); lit parts go brighter.
            var alpha = (float)Math.Min(1, path.Spec.Alpha * fade * (0.55 + 0.9 * lit));
            if (alpha <= 0.002f) continue;
            var px = (float)(path.Spec.Width * height) * rootScale;
            Stroke(primitives, root, path, amount, px * 3.2f, alpha * 0.12f, color);
            Stroke(primitives, root, path, amount, px, alpha, color);
        }
        foreach (var node in spec.Nodes)
        {
            var appear = Clamp01((draw - node.Delay) / 0.08);
            var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, node.At.X, node.At.Y));
            var twinkle = 0.55 + 0.45 * Math.Sin(time * 2.3 + node.TwinklePhase);
            var alpha = appear * fade * (0.25 + 0.75 * lit) * twinkle;
            if (alpha <= 0.004) continue;
            var pop = 1 + (1 - appear) * 1.5;
            var size = (float)(node.Size * height * pop * (0.8 + 0.2 * twinkle));
            LumiereDraw.Sprite(primitives, star, root, (float)(node.At.X * height), (float)(node.At.Y * height), size, size, 0,
                (float)alpha, LumiereColor.Tint(color));
        }
    }

    /// <summary>Strokes the first <paramref name="amount"/> of a path (dashes respected); width already in target px.</summary>
    private void Stroke(EffectPrimitiveRenderer primitives, Matrix3x2 root, Built path, double amount, float width, float alpha,
        Vector3 color)
    {
        var points = path.Spec.Points;
        var limit = Math.Min(amount, path.Total);
        _node.TailWidth = _node.HeadWidth = width;
        _node.Color = new EffectColor(color.X, color.Y, color.Z, alpha);
        Vector2 At(int index, double distance)
        {
            var a = points[index - 1];
            var b = points[index];
            var segment = path.Lengths[index] - path.Lengths[index - 1];
            var f = segment > 0 ? (distance - path.Lengths[index - 1]) / segment : 0;
            return Vector2.Transform(new Vector2((float)((a.X + (b.X - a.X) * f) * height), (float)((a.Y + (b.Y - a.Y) * f) * height)), root);
        }
        void Flush()
        {
            if (_points.Count >= 2)
            {
                _node.Points = _points;
                _node.StartPointIndex = 0;
                _node.EndPointIndex = int.MaxValue;
                primitives.DrawPolyline(_node);
            }
            _points.Clear();
        }
        _points.Clear();
        if (path.Spec.Dash is not { } dash)
        {
            _points.Add(Vector2.Transform(new Vector2((float)(points[0].X * height), (float)(points[0].Y * height)), root));
            for (var i = 1; i < points.Length; i++)
            {
                var start = path.Lengths[i - 1];
                if (start >= limit) break;
                _points.Add(At(i, Math.Min(path.Lengths[i], limit)));
            }
            Flush();
            return;
        }
        // Dashes: each on-piece inside each segment by integer period index (no float cursor stuck on boundaries).
        var period = dash.On + dash.Off;
        for (var i = 1; i < points.Length; i++)
        {
            var start = path.Lengths[i - 1];
            var end = Math.Min(path.Lengths[i], limit);
            if (start >= limit) break;
            for (var k = (long)Math.Floor(start / period); k * period < end; k++)
            {
                var from = Math.Max(start, k * period);
                var to = Math.Min(end, k * period + dash.On);
                if (to <= from) continue;
                _points.Add(At(i, from));
                _points.Add(At(i, to));
                Flush();
            }
        }
    }
}
