using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

public enum SonnetPathVerb { MoveTo, LineTo, QuadraticCurveTo, BezierCurveTo, Arc, Circle, Rectangle }
public enum SonnetPaintKind { Stroke, Fill }

public sealed record SonnetPathCommand(
    SonnetPathVerb Verb,
    double A, double B, double C = 0, double D = 0, double E = 0, double F = 0,
    bool Anticlockwise = false,
    double Length = 0,
    double LastX = 0,
    double LastY = 0);

public sealed record SonnetPaintCommand(
    SonnetPaintKind Kind,
    IReadOnlyList<SonnetPathCommand> Path,
    uint Color,
    double Alpha,
    double Width,
    double Length,
    double StaggerDelay,
    double StaggerSpan);

/// <summary>Serializable equivalent of Folia's AnimatedGraphics command recorder.</summary>
public sealed class SonnetDrawList
{
    private const double Golden = 0.6180339887498949;
    private readonly List<SonnetPaintCommand> _commands = [];
    private readonly List<SonnetPathCommand> _path = [];
    private double _length;
    private double _lastX;
    private double _lastY;
    private int _strokeIndex;
    private int _fillIndex;

    public IReadOnlyList<SonnetPaintCommand> Commands => _commands;

    public SonnetDrawList MoveTo(double x, double y)
    {
        _path.Add(new(SonnetPathVerb.MoveTo, x, y));
        _lastX = x; _lastY = y;
        return this;
    }

    public SonnetDrawList LineTo(double x, double y)
    {
        var length = Math.Sqrt((x - _lastX) * (x - _lastX) + (y - _lastY) * (y - _lastY));
        _path.Add(new(SonnetPathVerb.LineTo, x, y, Length: length, LastX: _lastX, LastY: _lastY));
        _length += length; _lastX = x; _lastY = y;
        return this;
    }

    public SonnetDrawList QuadraticCurveTo(double cx, double cy, double tx, double ty)
    {
        var length = Distance(_lastX, _lastY, cx, cy) + Distance(cx, cy, tx, ty);
        _path.Add(new(SonnetPathVerb.QuadraticCurveTo, cx, cy, tx, ty,
            Length: length, LastX: _lastX, LastY: _lastY));
        _length += length; _lastX = tx; _lastY = ty;
        return this;
    }

    public SonnetDrawList BezierCurveTo(double c1x, double c1y, double c2x, double c2y, double tx, double ty)
    {
        var length = Distance(_lastX, _lastY, c1x, c1y)
            + Distance(c1x, c1y, c2x, c2y) + Distance(c2x, c2y, tx, ty);
        _path.Add(new(SonnetPathVerb.BezierCurveTo, c1x, c1y, c2x, c2y, tx, ty,
            Length: length, LastX: _lastX, LastY: _lastY));
        _length += length; _lastX = tx; _lastY = ty;
        return this;
    }

    public SonnetDrawList Arc(double cx, double cy, double radius, double start, double end, bool anticlockwise = false)
    {
        var difference = end - start;
        if (anticlockwise && difference > 0) difference -= Math.Tau;
        else if (!anticlockwise && difference < 0) difference += Math.Tau;
        var length = Math.Abs(difference) * radius;
        _path.Add(new(SonnetPathVerb.Arc, cx, cy, radius, start, end, difference,
            anticlockwise, length, _lastX, _lastY));
        _length += length;
        _lastX = cx + Math.Cos(end) * radius;
        _lastY = cy + Math.Sin(end) * radius;
        return this;
    }

    public SonnetDrawList Circle(double x, double y, double radius)
    {
        var length = Math.Tau * radius;
        _path.Add(new(SonnetPathVerb.Circle, x, y, radius, Length: length, LastX: _lastX, LastY: _lastY));
        _length += length;
        _lastX = x + radius; _lastY = y;
        return this;
    }

    public SonnetDrawList Rectangle(double x, double y, double width, double height)
    {
        _path.Add(new(SonnetPathVerb.Rectangle, x, y, width, height,
            Length: 2 * (Math.Abs(width) + Math.Abs(height)), LastX: _lastX, LastY: _lastY));
        _length += 2 * (Math.Abs(width) + Math.Abs(height));
        _lastX = x; _lastY = y;
        return this;
    }

    public SonnetDrawList Stroke(uint color, double width, double alpha) => Paint(SonnetPaintKind.Stroke, color, alpha, width);
    public SonnetDrawList Fill(uint color, double alpha) => Paint(SonnetPaintKind.Fill, color, alpha, 0);

    public EffectContainer Replay(int curveSegments = 24)
    {
        var root = new EffectContainer();
        foreach (var command in _commands)
        {
            var paths = Tessellate(command.Path, curveSegments);
            var color = FromRgb(command.Color, command.Alpha);
            foreach (var points in paths.Where(points => points.Count >= (command.Kind == SonnetPaintKind.Fill ? 3 : 2)))
            {
                if (command.Kind == SonnetPaintKind.Fill)
                    root.Add(new PolygonNode { Points = points, Color = color, BlendMode = EffectBlendMode.Screen });
                else
                    root.Add(new PolylineNode
                    {
                        Points = points, TailWidth = (float)command.Width, HeadWidth = (float)command.Width,
                        TailAlpha = 1, HeadAlpha = 1, Color = color, BlendMode = EffectBlendMode.Screen,
                    });
            }
        }
        return root;
    }

    private SonnetDrawList Paint(SonnetPaintKind kind, uint color, double alpha, double width)
    {
        if (_path.Count == 0) return this;
        var index = kind == SonnetPaintKind.Stroke ? _strokeIndex++ : _fillIndex++;
        var slot = index * Golden % 1;
        var jitter = unchecked((uint)(index * 2654435761L)) / 4294967296d;
        var delay = slot * (kind == SonnetPaintKind.Stroke ? 0.5 : 0.45);
        var span = (kind == SonnetPaintKind.Stroke ? 0.32 + jitter * 0.26 : 0.4 + jitter * 0.25);
        _commands.Add(new(kind, _path.ToArray(), color, alpha, width, _length, delay, Math.Min(span, 1 - delay)));
        _path.Clear(); _length = 0;
        return this;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> Tessellate(IReadOnlyList<SonnetPathCommand> path, int segments)
    {
        var result = new List<IReadOnlyList<Vector2>>();
        List<Vector2>? points = null;
        Vector2 current = default;
        foreach (var command in path)
        {
            if (command.Verb == SonnetPathVerb.MoveTo)
            {
                if (points is { Count: > 0 }) result.Add(points);
                points = [new((float)command.A, (float)command.B)];
                current = points[0];
                continue;
            }
            points ??= [current];
            switch (command.Verb)
            {
                case SonnetPathVerb.LineTo:
                    current = new((float)command.A, (float)command.B); points.Add(current); break;
                case SonnetPathVerb.QuadraticCurveTo:
                    AddQuadratic(points, current, new((float)command.A, (float)command.B), new((float)command.C, (float)command.D), segments);
                    current = points[^1]; break;
                case SonnetPathVerb.BezierCurveTo:
                    AddCubic(points, current, new((float)command.A, (float)command.B), new((float)command.C, (float)command.D), new((float)command.E, (float)command.F), segments);
                    current = points[^1]; break;
                case SonnetPathVerb.Arc:
                    for (var index = 1; index <= segments; index++)
                    {
                        var angle = command.D + command.F * index / segments;
                        points.Add(new((float)(command.A + Math.Cos(angle) * command.C), (float)(command.B + Math.Sin(angle) * command.C)));
                    }
                    current = points[^1]; break;
                case SonnetPathVerb.Circle:
                    if (points.Count > 0) result.Add(points);
                    points = Enumerable.Range(0, segments + 1).Select(index => new Vector2(
                        (float)(command.A + Math.Cos(Math.Tau * index / segments) * command.C),
                        (float)(command.B + Math.Sin(Math.Tau * index / segments) * command.C))).ToList();
                    current = points[^1]; break;
                case SonnetPathVerb.Rectangle:
                    if (points.Count > 0) result.Add(points);
                    points = [new((float)command.A, (float)command.B), new((float)(command.A + command.C), (float)command.B),
                        new((float)(command.A + command.C), (float)(command.B + command.D)), new((float)command.A, (float)(command.B + command.D))];
                    current = points[^1]; break;
            }
        }
        if (points is { Count: > 0 }) result.Add(points);
        return result;
    }

    private static void AddQuadratic(List<Vector2> points, Vector2 p0, Vector2 p1, Vector2 p2, int segments)
    {
        for (var index = 1; index <= segments; index++)
        {
            var t = index / (float)segments; var u = 1 - t;
            points.Add(u * u * p0 + 2 * u * t * p1 + t * t * p2);
        }
    }
    private static void AddCubic(List<Vector2> points, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segments)
    {
        for (var index = 1; index <= segments; index++)
        {
            var t = index / (float)segments; var u = 1 - t;
            points.Add(u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3);
        }
    }
    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    private static EffectColor FromRgb(uint color, double alpha) => new(
        ((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, (float)alpha);
}
