using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>
/// Folia overlay.ts: the frame decoration, like marks on a viewfinder or an optical bench — four corner brackets, two
/// registration crosses on the side midlines, two short dashed runs on the top edge. Static, faint champagne hairlines.
/// </summary>
internal sealed class LumiereOverlay
{
    private readonly PolylineNode _line = new() { TailAlpha = 1, HeadAlpha = 1 };
    private readonly List<Vector2> _points = [];

    /// <param name="width">Logical width; <paramref name="pixelScale"/> converts to target pixels.</param>
    public void Draw(EffectPrimitiveRenderer primitives, float width, float height, float pixelScale, Vector3 color)
    {
        var unit = MathF.Min(width, height);
        var (padX, padY) = LumiereLineWrap.FrameInsets(width, height);
        var arm = unit * 0.045f;
        var stroke = MathF.Max(1.2f, unit / 600) * pixelScale;
        const float alpha = 0.5f;
        void Stroke(float a, params Vector2[] points)
        {
            _points.Clear();
            foreach (var point in points) _points.Add(point * pixelScale);
            _line.Points = _points;
            _line.StartPointIndex = 0;
            _line.EndPointIndex = int.MaxValue;
            _line.TailWidth = _line.HeadWidth = stroke;
            _line.Color = new EffectColor(color.X, color.Y, color.Z, a);
            primitives.DrawPolyline(_line);
        }

        foreach (var (x, y, sx, sy) in new[]
                 {
                     ((float)padX, (float)padY, 1f, 1f), (width - (float)padX, (float)padY, -1f, 1f),
                     (width - (float)padX, height - (float)padY, -1f, -1f), ((float)padX, height - (float)padY, 1f, -1f),
                 })
            Stroke(alpha, new Vector2(x, y + sy * arm), new Vector2(x, y), new Vector2(x + sx * arm, y));

        var mark = unit * 0.012f;
        foreach (var x in new[] { (float)padX, width - (float)padX })
        {
            var y = height / 2;
            Stroke(alpha * 0.8f, [.. Enumerable.Range(0, 49).Select(i =>
                new Vector2(x + MathF.Cos(i / 48f * MathF.Tau) * mark * 0.6f, y + MathF.Sin(i / 48f * MathF.Tau) * mark * 0.6f))]);
            Stroke(alpha * 0.8f, new Vector2(x - mark, y), new Vector2(x + mark, y));
            Stroke(alpha * 0.8f, new Vector2(x, y - mark), new Vector2(x, y + mark));
        }

        foreach (var side in new[] { -1f, 1f })
            for (var k = 0; k < 4; k++)
            {
                var x = width / 2 + side * (unit * 0.1f + k * unit * 0.018f);
                Stroke(alpha * 0.55f, new Vector2(x, (float)padY), new Vector2(x + side * unit * 0.009f, (float)padY));
            }
        primitives.Flush();
    }
}
