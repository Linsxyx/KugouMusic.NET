using AvaloniaSilkEffects.Lumiere.Text;
using static AvaloniaSilkEffects.Lumiere.LineArt.LumiereRecipes;

namespace AvaloniaSilkEffects.Lumiere.LineArt;

/// <summary>
/// A JS style object: unset fields fall through. <c>{ a, ...style, b }</c> becomes
/// <c>new LumiereStyle(a).Over(style).Over(new LumiereStyle(b))</c>, so every call site maps one to one.
/// </summary>
public readonly record struct LumiereStyle(double? Width = null, double? Alpha = null, double? Delay = null, double? Span = null,
    (double On, double Off)? Dash = null)
{
    /// <summary><c>style.delay ?? 0</c>.</summary>
    public double D => Delay ?? 0;

    public LumiereStyle Over(LumiereStyle top) =>
        new(top.Width ?? Width, top.Alpha ?? Alpha, top.Delay ?? Delay, top.Span ?? Span, top.Dash ?? Dash);
}

/// <summary>
/// Folia lineart/diagrams.ts: line-art diagrams per family — windows, prisms, caustics, optical paths, interference,
/// plants, sky, stage. Height units (x in 0..aspect); strokes 0.0016–0.0024 main, 0.0008–0.0012 helpers, dashes for
/// axes / normals / virtual images; delays stagger the drawing.
/// </summary>
public static class LumiereDiagrams
{
    private const double Tau = Math.PI * 2;

    private static LumiereStyle S(double? width = null, double? alpha = null, double? delay = null, double? span = null,
        (double, double)? dash = null) => new(width, alpha, delay, span, dash);

    private static LumiereLinePath Path(LumierePoint[] points, LumiereStyle style = default) =>
        new(points, style.Width ?? 0.0016, style.Alpha ?? 0.55, style.Delay ?? 0, style.Span ?? 0.4, style.Dash);

    private static LumiereLineNode Node(LumierePoint at, double size = 0.018, double delay = 0.4, double twinklePhase = 0) =>
        new(at, size, delay, twinklePhase);

    public static LumiereLinePath Segment(LumierePoint a, LumierePoint b, LumiereStyle style = default) => Path([a, b], style);

    /// <summary>Circle / ellipse, optionally rotated.</summary>
    public static LumierePoint[] EllipsePath(double cx, double cy, double rx, double ry, double rotation = 0, double from = 0,
        double to = Tau, int steps = 96)
    {
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        return [.. ArcPoints(0, 0, 1, from, to, steps).Select(p => P(cx + p.X * rx * cos - p.Y * ry * sin, cy + p.X * rx * sin + p.Y * ry * cos))];
    }

    public static LumiereLinePath Circle(double cx, double cy, double r, LumiereStyle style = default) => Path(EllipsePath(cx, cy, r, r), style);

    public static LumiereLineArtSpec Merge(params LumiereLineArtSpec[] specs) => LumiereLineArtSpec.Merge(specs);

    private static LumiereLineArtSpec Spec(IEnumerable<LumiereLinePath> paths, IEnumerable<LumiereLineNode>? nodes = null) =>
        new([.. paths], [.. nodes ?? []]);

    /// <summary>Rays from r0 to r1, count of them evenly between from..to.</summary>
    public static LumiereLineArtSpec Rays(double cx, double cy, double r0, double r1, int count, double from = 0, double to = Tau,
        LumiereStyle style = default) => Spec(Enumerable.Range(0, count).Select(i =>
    {
        var a = from + (to - from) * (i + (to - from >= Tau - 1e-6 ? 0 : 0.5)) / count;
        return Segment(P(cx + Math.Cos(a) * r0, cy + Math.Sin(a) * r0), P(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1),
            S(0.001, 0.4).Over(style).Over(S(delay: style.D + (double)i / count * 0.3, span: style.Span ?? 0.2)));
    }));

    private static LumiereLineArtSpec Concentric(double cx, double cy, double[] radii, LumiereStyle style = default) =>
        Spec(radii.Select((r, i) => Circle(cx, cy, r, S(0.0011, 0.45).Over(style).Over(S(delay: style.D + i * 0.06)))));

    // ---- 窗隙 lattice -----------------------------------------------------------------------------------------

    public static LumiereLineArtSpec CrossWindow(double x, double y, double w, double h, LumiereStyle style = default) => Spec(
        [
            Path([P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h), P(x, y)], S(0.002, 0.55).Over(style)),
            Segment(P(x + w / 2, y), P(x + w / 2, y + h), S(0.0014, 0.45, style.D + 0.15)),
            Segment(P(x, y + h / 2), P(x + w, y + h / 2), S(0.0014, 0.45, style.D + 0.2)),
        ],
        [Node(P(x + w / 2, y + h / 2), 0.02, style.D + 0.4)]);

    public static LumiereLineArtSpec ArchWindow(double x, double y, double w, double h, LumiereStyle style = default)
    {
        var r = w / 2;
        var top = y + r;
        return Spec(
            [
                Path([P(x, y + h), P(x, top), .. ArcPoints(x + r, top, r, Math.PI, Tau, 40), P(x + w, y + h), P(x, y + h)], S(0.0018, 0.55).Over(style)),
                Segment(P(x + r, y), P(x + r, y + h), S(0.001, 0.35, style.D + 0.2)),
            ],
            [Node(P(x + r, y + 0.01), 0.018, style.D + 0.4)]);
    }

    public static LumiereLineArtSpec RoseWindow(double cx, double cy, double r, LumiereStyle style = default) => Merge(
        Concentric(cx, cy, [r, r * 0.62, r * 0.22], S(0.0016, 0.55).Over(style)),
        Rays(cx, cy, r * 0.22, r, 12, 0, Tau, S(delay: style.D + 0.2)),
        Spec(Enumerable.Range(0, 12).Select(i =>
        {
            var a = i / 12.0 * Tau;
            return Circle(cx + Math.Cos(a) * r * 0.8, cy + Math.Sin(a) * r * 0.8, r * 0.14, S(0.0009, 0.35, style.D + 0.35 + i * 0.02));
        }), [Node(P(cx, cy), 0.028, style.D + 0.5)]));

    public static LumiereLineArtSpec DoorSlit(double cx, double top, double bottom, double gap, LumiereStyle style = default) => Spec(
        [
            Segment(P(cx - gap / 2, top), P(cx - gap / 2, bottom), S(0.0018, 0.55).Over(style)),
            Segment(P(cx + gap / 2, top), P(cx + gap / 2, bottom), S(0.0018, 0.55).Over(style).Over(S(delay: style.D + 0.05))),
            Segment(P(cx - gap / 2 - 0.25, bottom), P(cx + gap / 2 + 0.25, bottom), S(0.001, 0.35, style.D + 0.2)),
        ],
        [Node(P(cx, top + 0.02), 0.02, style.D + 0.4)]);

    public static LumiereLineArtSpec GridPanel(double x, double y, double w, double h, int cols, int rows, bool diagonal = false,
        LumiereStyle style = default)
    {
        var paths = new List<LumiereLinePath> { Path([P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h), P(x, y)], S(0.0018, 0.5).Over(style)) };
        var inner = S(0.0009, 0.32, style.D + 0.15, 0.25);
        if (diagonal)
        {
            var n = cols + rows;
            for (var i = 1; i < n; i++)
            {
                var t = (double)i / n;
                paths.Add(Segment(P(x + w * Math.Min(1, t * 2), y + h * Math.Max(0, t * 2 - 1)), P(x + w * Math.Max(0, t * 2 - 1), y + h * Math.Min(1, t * 2)), inner));
                paths.Add(Segment(P(x + w * (1 - Math.Min(1, t * 2)), y + h * Math.Max(0, t * 2 - 1)), P(x + w * (1 - Math.Max(0, t * 2 - 1)), y + h * Math.Min(1, t * 2)), inner));
            }
        }
        else
        {
            for (var i = 1; i < cols; i++) paths.Add(Segment(P(x + w * i / cols, y), P(x + w * i / cols, y + h), inner));
            for (var j = 1; j < rows; j++) paths.Add(Segment(P(x, y + h * j / rows), P(x + w, y + h * j / rows), inner));
        }
        return Spec(paths);
    }

    public static LumiereLineArtSpec TallWindows(double x0, double x1, double y, double h, int count, LumiereStyle style = default) => Merge(
        [.. Enumerable.Range(0, count).Select(i =>
        {
            var w = (x1 - x0) / count * 0.45;
            var x = x0 + (x1 - x0) * (i + 0.5) / count - w / 2;
            return ArchWindow(x, y, w, h, S(alpha: 0.45).Over(style).Over(S(delay: style.D + i * 0.07)));
        })]);

    // ---- 棱镜 prism -------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec Shards(double aspect, LumiereRng random, int count, LumiereStyle style = default) =>
        Spec(Enumerable.Range(0, count).Select(i =>
        {
            var cx = (0.1 + random.Next() * 0.8) * aspect;
            var cy = 0.12 + random.Next() * 0.76;
            var r = 0.025 + random.Next() * 0.05;
            var a = random.Next() * Tau;
            LumierePoint[] points = [.. new[] { 0, 1, 2, 0 }.Select(k =>
                P(cx + Math.Cos(a + k * Tau / 3) * r, cy + Math.Sin(a + k * Tau / 3) * r * (0.7 + random.Next() * 0.6)))];
            return Path(points, S(0.0014, 0.5).Over(style).Over(S(delay: style.D + i * 0.04, span: 0.25)));
        }).ToList());

    public static LumiereLineArtSpec SpectrumLines(double x0, double x1, double y, double h, LumiereRng random, int count,
        LumiereStyle style = default)
    {
        var paths = new List<LumiereLinePath> { Segment(P(x0, y + h / 2), P(x1, y + h / 2), S(0.001, 0.35).Over(style)) };
        for (var i = 0; i < count; i++)
        {
            var x = x0 + (x1 - x0) * (0.05 + 0.9 * random.Next());
            var k = 0.3 + random.Next() * 0.7;
            paths.Add(Segment(P(x, y + h / 2), P(x, y + h / 2 - h * k), S(0.0012 + random.Next() * 0.0012, 0.4 + k * 0.3, style.D + 0.1 + i * 0.03, 0.2)));
        }
        return Spec(paths);
    }

    public static LumiereLineArtSpec CubeSplitter(double cx, double cy, double size, LumiereStyle style = default) => Spec(
        [
            Path([P(cx - size / 2, cy - size / 2), P(cx + size / 2, cy - size / 2), P(cx + size / 2, cy + size / 2), P(cx - size / 2, cy + size / 2), P(cx - size / 2, cy - size / 2)],
                S(0.002, 0.6).Over(style)),
            Segment(P(cx - size / 2, cy + size / 2), P(cx + size / 2, cy - size / 2), S(0.0012, 0.45, style.D + 0.2)),
        ],
        [Node(P(cx, cy), 0.022, style.D + 0.35)]);

    public static LumiereLineArtSpec RainbowArcs(double cx, double cy, double r, LumiereStyle style = default) => Spec(
    [
        Path(ArcPoints(cx, cy, r, Math.PI * 1.08, Math.PI * 1.92, 72), S(0.0018, 0.5).Over(style)),
        Path(ArcPoints(cx, cy, r * 1.18, Math.PI * 1.1, Math.PI * 1.9, 72), S(0.001, 0.3, style.D + 0.15, dash: (0.006, 0.01))),
        Circle(cx, cy - r * 0.4, 0.03, S(0.0012, 0.45, style.D + 0.3)),
    ]);

    // ---- 焦散 caustic -----------------------------------------------------------------------------------------

    public static LumiereLineArtSpec GlassCup(double cx, double top, double w, double h, LumiereStyle style = default) => Spec(
        [
            Path(EllipsePath(cx, top, w / 2, w * 0.12), S(0.0016, 0.5).Over(style)),
            Path([P(cx - w / 2, top), P(cx - w * 0.4, top + h), P(cx + w * 0.4, top + h), P(cx + w / 2, top)], S(0.0016, 0.5, style.D + 0.1)),
            Path(EllipsePath(cx, top + h, w * 0.4, w * 0.09, 0, 0, Math.PI), S(0.001, 0.35, style.D + 0.2)),
        ],
        [Node(P(cx - w * 0.3, top + h * 0.3), 0.016, style.D + 0.35)]);

    public static LumiereLineArtSpec Ripples(double cx, double cy, double r, int count, LumiereStyle style = default) => Spec(
        Enumerable.Range(0, count).Select(i => Path(EllipsePath(cx, cy, r * (i + 1) / count, r * 0.3 * (i + 1) / count),
            S(0.0012, 0.5 - i * 0.07).Over(style).Over(S(delay: style.D + i * 0.08)))),
        [Node(P(cx, cy), 0.02, style.D + 0.3)]);

    public static LumiereLineArtSpec Waterline(double aspect, double y, double amplitude, double waves, LumiereStyle style = default) => Spec(
        [Path([.. Enumerable.Range(0, 121).Select(i => P(i / 120.0 * aspect, y + Math.Sin(i / 120.0 * waves * Tau) * amplitude))],
            S(0.0014, 0.45).Over(style).Over(S(span: 0.6)))]);

    public static LumiereLineArtSpec Magnifier(double cx, double cy, double r, LumiereStyle style = default) => Spec(
    [
        Circle(cx, cy, r, S(0.0022, 0.6).Over(style)),
        Circle(cx, cy, r * 0.9, S(0.0009, 0.3, style.D + 0.1)),
        Segment(P(cx + r * 0.7, cy + r * 0.7), P(cx + r * 1.6, cy + r * 1.6), S(0.004, 0.5, style.D + 0.2)),
    ]);

    public static LumiereLineArtSpec Crystal(double cx, double cy, double r, LumiereStyle style = default)
    {
        LumierePoint[] outer = [.. Enumerable.Range(0, 7).Select(i => P(cx + Math.Cos(i * Tau / 6) * r, cy + Math.Sin(i * Tau / 6) * r * 1.3))];
        return Spec(
            [Path(outer, S(0.002, 0.55).Over(style)), .. new[] { 0, 1, 2 }.Select(i => Segment(outer[i], outer[i + 3], S(0.0009, 0.3, style.D + 0.15 + i * 0.05)))],
            outer.Take(6).Where((_, i) => i % 2 == 0).Select((at, i) => Node(at, 0.016, style.D + 0.4, i)));
    }

    // ---- 光路 optics ------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec Lens(double cx, double cy, double h, bool convex, double aspect, LumiereStyle style = default)
    {
        var bulge = h * 0.18;
        var top = P(cx, cy - h / 2);
        var bottom = P(cx, cy + h / 2);
        LumierePoint[] Side(int sign)
        {
            var x = cx + sign * (convex ? bulge : -bulge * 0.2) + (convex ? 0 : sign * bulge * 0.6);
            return CubicPoints(top, P(x, cy - h / 4), P(x, cy + h / 4), bottom, 32);
        }
        LumierePoint[] outline = convex
            ? [.. Side(1), .. Side(-1).Reverse()]
            :
            [
                P(cx - bulge * 0.7, cy - h / 2), P(cx + bulge * 0.7, cy - h / 2),
                .. CubicPoints(P(cx + bulge * 0.7, cy - h / 2), P(cx + bulge * 0.1, cy - h / 4), P(cx + bulge * 0.1, cy + h / 4), P(cx + bulge * 0.7, cy + h / 2), 24),
                P(cx - bulge * 0.7, cy + h / 2),
                .. CubicPoints(P(cx - bulge * 0.7, cy + h / 2), P(cx - bulge * 0.1, cy + h / 4), P(cx - bulge * 0.1, cy - h / 4), P(cx - bulge * 0.7, cy - h / 2), 24),
            ];
        return Spec(
        [
            Path(outline, S(0.002, 0.6).Over(style)),
            Segment(P(aspect * 0.05, cy), P(aspect * 0.95, cy), S(0.0009, 0.3, style.D + 0.1, 0.5, (0.008, 0.008))),
        ]);
    }

    public static LumiereLineArtSpec RayPath(LumierePoint[] points, LumiereStyle style = default) => Spec(
        [Path(points, S(0.0014, 0.6, span: 0.5).Over(style))],
        [Node(points[^1], 0.016, style.D + (style.Span ?? 0.5), points.Length)]);

    public static LumiereLineArtSpec FocusMark(double x, double y, LumiereStyle style = default) => Spec(
        [
            Segment(P(x - 0.012, y), P(x + 0.012, y), S(0.0012, 0.55).Over(style)),
            Segment(P(x, y - 0.012), P(x, y + 0.012), S(0.0012, 0.55).Over(style)),
        ],
        [Node(P(x, y), 0.024, style.D + 0.3)]);

    public static LumiereLineArtSpec Mirror(double cx, double cy, double length, double angle, LumiereStyle style = default)
    {
        var dx = Math.Cos(angle) * length / 2;
        var dy = Math.Sin(angle) * length / 2;
        var nx = -Math.Sin(angle);
        var ny = Math.Cos(angle);
        var hatches = Enumerable.Range(0, 10).Select(i =>
        {
            var t = -0.45 + i / 9.0 * 0.9;
            var bx = cx + dx * 2 * t;
            var by = cy + dy * 2 * t;
            return Segment(P(bx, by), P(bx + nx * 0.015 - dx * 0.04, by + ny * 0.015 - dy * 0.04), S(0.0008, 0.3, style.D + 0.2 + i * 0.01, 0.1));
        });
        var normal = Math.Atan2(-ny, -nx);
        return Spec(
        [
            Segment(P(cx - dx, cy - dy), P(cx + dx, cy + dy), S(0.003, 0.6).Over(style)),
            .. hatches,
            Segment(P(cx, cy), P(cx - nx * length * 0.5, cy - ny * length * 0.5), S(0.0009, 0.35, style.D + 0.25, dash: (0.006, 0.006))),
            Path(ArcPoints(cx, cy, length * 0.12, normal - 0.5, normal + 0.5, 20), S(0.0009, 0.4, style.D + 0.35)),
        ]);
    }

    public static LumiereLineArtSpec InterfaceLine(double aspect, double y, LumiereStyle style = default) => Spec(
    [
        Segment(P(aspect * 0.06, y), P(aspect * 0.94, y), S(0.002, 0.55).Over(style).Over(S(span: 0.5))),
        .. Enumerable.Range(0, 24).Select(i =>
        {
            var x = aspect * (0.08 + i / 23.0 * 0.84);
            return Segment(P(x, y + 0.01), P(x - 0.02, y + 0.04), S(0.0008, 0.22, style.D + 0.2 + i * 0.01, 0.1));
        }),
    ]);

    public static LumiereLineArtSpec Ruler(double x0, double x1, double y, int ticks, LumiereStyle style = default) => Spec(
    [
        Segment(P(x0, y), P(x1, y), S(0.0016, 0.5).Over(style).Over(S(span: 0.5))),
        .. Enumerable.Range(0, ticks + 1).Select(i =>
        {
            var x = x0 + (x1 - x0) * i / ticks;
            return Segment(P(x, y), P(x, y + (i % 5 == 0 ? 0.02 : 0.01)), S(0.0008, 0.4, style.D + 0.2 + i * 0.005, 0.08));
        }),
    ]);

    public static LumiereLineArtSpec Fiber(LumierePoint p0, LumierePoint p1, LumierePoint p2, LumierePoint p3, double thickness,
        LumiereStyle style = default)
    {
        var center = CubicPoints(p0, p1, p2, p3, 60);
        LumierePoint[] Offset(int sign) => [.. center.Select((c, i) =>
        {
            var a = center[Math.Max(0, i - 1)];
            var b = center[Math.Min(center.Length - 1, i + 1)];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len == 0) len = 1;
            return P(c.X - dy / len * thickness * sign, c.Y + dx / len * thickness * sign);
        })];
        return Spec(
            [
                Path(Offset(1), S(0.0014, 0.5).Over(style).Over(S(span: 0.6))),
                Path(Offset(-1), S(0.0014, 0.5).Over(style).Over(S(delay: style.D + 0.05, span: 0.6))),
            ],
            [Node(center[^1], 0.024, style.D + 0.6)]);
    }

    public static LumiereLineArtSpec PinholeBox(double cx, double cy, double w, double h, LumiereStyle style = default) => Spec(
        [
            Path([P(cx - w / 2, cy - h / 2), P(cx + w / 2, cy - h / 2), P(cx + w / 2, cy + h / 2), P(cx - w / 2, cy + h / 2), P(cx - w / 2, cy - h / 2)], S(0.0018, 0.5).Over(style)),
            Segment(P(cx - w / 2 - 0.25, cy - h * 0.35), P(cx - w / 2 - 0.25, cy + h * 0.35), S(0.0016, 0.45, style.D + 0.1)),
            Segment(P(cx + w / 2 - 0.02, cy + h * 0.28), P(cx + w / 2 - 0.02, cy - h * 0.28), S(0.0014, 0.4, style.D + 0.3, dash: (0.005, 0.005))),
            Segment(P(cx - w / 2 - 0.25, cy - h * 0.35), P(cx + w / 2 - 0.02, cy + h * 0.28), S(0.0008, 0.3, style.D + 0.2)),
            Segment(P(cx - w / 2 - 0.25, cy + h * 0.35), P(cx + w / 2 - 0.02, cy - h * 0.28), S(0.0008, 0.3, style.D + 0.2)),
        ],
        [Node(P(cx - w / 2, cy), 0.02, style.D + 0.3)]);

    // ---- 衍射 wave --------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec Rings(double cx, double cy, double r, int count, LumiereStyle style = default) =>
        Concentric(cx, cy, [.. Enumerable.Range(0, count).Select(i => r * Math.Sqrt((i + 1.0) / count))], style);

    public static LumiereLineArtSpec Grating(double cx, double y, double width, int slits, LumiereStyle style = default) => Spec(
    [
        Segment(P(cx - width / 2, y), P(cx + width / 2, y), S(0.0022, 0.55).Over(style)),
        .. Enumerable.Range(0, slits).Select(i =>
        {
            var x = cx - width * 0.3 + width * 0.6 * i / Math.Max(1, slits - 1);
            return Segment(P(x, y - 0.01), P(x, y + 0.01), S(0.0008, 0.4, style.D + 0.15 + i * 0.01, 0.1));
        }),
    ]);

    public static LumiereLineArtSpec TwoSources(double cx, double cy, double separation, double r, LumiereStyle style = default) => Merge(
        Concentric(cx - separation / 2, cy, [r * 0.25, r * 0.5, r * 0.75, r], S(alpha: 0.3).Over(style)),
        Concentric(cx + separation / 2, cy, [r * 0.25, r * 0.5, r * 0.75, r], S(alpha: 0.3).Over(style).Over(S(delay: style.D + 0.1))),
        Spec([], [Node(P(cx - separation / 2, cy), 0.024, 0.3), Node(P(cx + separation / 2, cy), 0.024, 0.35, 1)]));

    public static LumiereLineArtSpec Moire(double cx, double cy, double size, int count, double rotation, LumiereStyle style = default)
    {
        IEnumerable<LumiereLinePath> Set(double angle, double delay) => Enumerable.Range(0, count).Select(i =>
        {
            var t = -0.5 + (double)i / (count - 1);
            var ox = Math.Cos(angle + Math.PI / 2) * t * size;
            var oy = Math.Sin(angle + Math.PI / 2) * t * size;
            return Segment(P(cx + ox - Math.Cos(angle) * size / 2, cy + oy - Math.Sin(angle) * size / 2),
                P(cx + ox + Math.Cos(angle) * size / 2, cy + oy + Math.Sin(angle) * size / 2),
                S(0.0008, 0.3).Over(style).Over(S(delay: delay + i * 0.008, span: 0.15)));
        });
        return Spec([.. Set(0, style.D), .. Set(rotation, style.D + 0.2)]);
    }

    public static LumiereLineArtSpec StandingWave(double x0, double x1, double y, double amplitude, int loops, LumiereStyle style = default) => Spec(
        new[] { -1, -0.5, 0.5, 1 }.Select((k, j) => Path(
            [.. Enumerable.Range(0, 121).Select(i => P(x0 + (x1 - x0) * (i / 120.0), y + Math.Sin(i / 120.0 * loops * Math.PI) * amplitude * k))],
            S(0.0012, 0.35 + Math.Abs(k) * 0.2).Over(style).Over(S(delay: style.D + j * 0.06, span: 0.5)))),
        Enumerable.Range(0, loops + 1).Select(i => Node(P(x0 + (x1 - x0) * i / loops, y), 0.014, style.D + 0.5, i)));

    public static LumiereLineArtSpec Polarizers(double cx, double cy, double r, double gap, double rotation, LumiereStyle style = default)
    {
        IEnumerable<LumiereLinePath> Disc(double x, double angle, double delay) =>
        [
            Circle(x, cy, r, S(0.0016, 0.5).Over(style).Over(S(delay: delay))),
            .. Enumerable.Range(0, 7).Select(i =>
            {
                var t = -0.75 + i / 6.0 * 1.5;
                var half = Math.Sqrt(Math.Max(0, 1 - t * t)) * r;
                var ox = -Math.Sin(angle) * t * r;
                var oy = Math.Cos(angle) * t * r;
                return Segment(P(x + ox - Math.Cos(angle) * half, cy + oy - Math.Sin(angle) * half),
                    P(x + ox + Math.Cos(angle) * half, cy + oy + Math.Sin(angle) * half), S(0.0008, 0.3, delay + 0.15 + i * 0.02, 0.15));
            }),
        ];
        return Spec([.. Disc(cx - gap / 2, 0, style.D), .. Disc(cx + gap / 2, rotation, style.D + 0.2)]);
    }

    // ---- 叶脉 botany ------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec BigLeaf(double cx, double cy, double length, double angle, LumiereStyle style = default)
    {
        var d = P(Math.Cos(angle), Math.Sin(angle));
        var n = P(-d.Y, d.X);
        LumierePoint At(double along, double across) =>
            P(cx + d.X * length * (along - 0.5) + n.X * across, cy + d.Y * length * (along - 0.5) + n.Y * across);
        var bulge = length * 0.3;
        var paths = new List<LumiereLinePath>
        {
            Path(CubicPoints(At(0, 0), At(0.3, bulge), At(0.75, bulge * 0.8), At(1, 0), 48), S(0.002, 0.6).Over(style).Over(S(span: 0.4))),
            Path(CubicPoints(At(0, 0), At(0.3, -bulge), At(0.75, -bulge * 0.8), At(1, 0), 48), S(0.002, 0.6).Over(style).Over(S(delay: style.D + 0.05, span: 0.4))),
            Path([At(-0.12, 0), At(1, 0)], S(0.0014, 0.5, style.D + 0.15, 0.4)),
        };
        for (var v = 1; v <= 6; v++)
        {
            var f = v / 7.5;
            foreach (var s in new[] { 1, -1 })
                paths.Add(Path(CubicPoints(At(f, 0), At(f + 0.05, s * bulge * 0.3), At(f + 0.1, s * bulge * 0.55), At(f + 0.15, s * bulge * (0.8 - f * 0.35)), 16),
                    S(0.0009, 0.4, style.D + 0.25 + v * 0.05, 0.18)));
        }
        return Spec(paths, [Node(At(1, 0), 0.026, style.D + 0.6)]);
    }

    public static LumiereLineArtSpec Canopy(double aspect, LumiereRng random, LumiereStyle style = default)
    {
        var paths = new List<LumiereLinePath>();
        void Grow(double x, double y, double angle, double length, int depth, double delay)
        {
            var x1 = x + Math.Cos(angle) * length;
            var y1 = y + Math.Sin(angle) * length;
            paths.Add(Segment(P(x, y), P(x1, y1), S(0.0008 + depth * 0.0005, 0.3 + depth * 0.08, delay, 0.2)));
            if (depth <= 0) return;
            Grow(x1, y1, angle - 0.35 - random.Next() * 0.3, length * 0.72, depth - 1, delay + 0.08);
            Grow(x1, y1, angle + 0.35 + random.Next() * 0.3, length * 0.72, depth - 1, delay + 0.08);
        }
        double[] roots = [0.08, 0.35, 0.65, 0.92];
        for (var i = 0; i < roots.Length; i++) Grow(roots[i] * aspect, -0.02, Math.PI / 2 + (random.Next() - 0.5) * 0.6, 0.14, 4, style.D + i * 0.05);
        return Spec(paths);
    }

    public static LumiereLineArtSpec FernCurl(double cx, double cy, double r, LumiereStyle style = default)
    {
        LumierePoint[] spiral = [.. Enumerable.Range(0, 140).Select(i =>
        {
            var t = i / 139.0;
            var a = t * Tau * 2.2;
            var rr = r * Math.Exp(-t * 2.4);
            return P(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr);
        })];
        LumierePoint[] stem = [P(cx + r, cy), P(cx + r, cy + r * 2.2)];
        var leaflets = Enumerable.Range(0, 9).Select(i =>
        {
            var at = spiral[(int)Math.Floor(i / 9.0 * 90)];
            return Circle(at.X, at.Y, 0.008 + (1 - i / 9.0) * 0.01, S(0.0008, 0.35, style.D + 0.4 + i * 0.03, 0.1));
        });
        return Spec([Path(stem, S(0.0016, 0.5).Over(style)), Path(spiral, S(0.0016, 0.55).Over(style).Over(S(delay: style.D + 0.1, span: 0.5))), .. leaflets]);
    }

    public static LumiereLineArtSpec SeedRoots(double cx, double cy, LumiereRng random, LumiereStyle style = default) => Spec(
        [
            Path(EllipsePath(cx, cy, 0.025, 0.035, 0.3), S(0.002, 0.6).Over(style)),
            .. Enumerable.Range(0, 6).Select(i =>
            {
                var spread = (i - 2.5) * 0.05;
                var end = P(cx + spread * 1.4 + (random.Next() - 0.5) * 0.04, cy + 0.2 + random.Next() * 0.06);
                return Path(CubicPoints(P(cx, cy + 0.03), P(cx + spread * 0.4, cy + 0.08), P(cx + spread, cy + 0.12), end, 24),
                    S(0.0009, 0.4, style.D + 0.2 + i * 0.05, 0.35));
            }).ToList(),
        ],
        [Node(P(cx, cy - 0.04), 0.03, style.D + 0.2)]);

    public static LumiereLineArtSpec Bloom(double cx, double cy, double r, int petals, LumiereStyle style = default) => Spec(
        [
            .. Enumerable.Range(0, petals).Select(i =>
            {
                var a = (double)i / petals * Tau;
                var d = P(Math.Cos(a), Math.Sin(a));
                var n = P(-d.Y, d.X);
                var tip = P(cx + d.X * r, cy + d.Y * r);
                var w = r * 0.35;
                return Path(
                [
                    .. CubicPoints(P(cx, cy), P(cx + d.X * r * 0.4 + n.X * w, cy + d.Y * r * 0.4 + n.Y * w), P(cx + d.X * r * 0.9 + n.X * w * 0.6, cy + d.Y * r * 0.9 + n.Y * w * 0.6), tip, 20),
                    .. CubicPoints(tip, P(cx + d.X * r * 0.9 - n.X * w * 0.6, cy + d.Y * r * 0.9 - n.Y * w * 0.6), P(cx + d.X * r * 0.4 - n.X * w, cy + d.Y * r * 0.4 - n.Y * w), P(cx, cy), 20),
                ], S(0.0016, 0.5).Over(style).Over(S(delay: style.D + i * 0.05, span: 0.3)));
            }),
            Circle(cx, cy, r * 0.12, S(0.0012, 0.5, style.D + 0.4)),
        ],
        [Node(P(cx, cy), 0.03, style.D + 0.5)]);

    public static LumiereLineArtSpec Vine(double x, double bottom, double top, double amplitude, double turns, LumiereStyle style = default) => Spec(
        [
            Path([.. Enumerable.Range(0, 161).Select(i =>
            {
                var t = i / 160.0;
                return P(x + Math.Sin(t * turns * Tau) * amplitude * (1 - t * 0.4), bottom + (top - bottom) * t);
            })], S(0.0016, 0.55).Over(style).Over(S(span: 0.7))),
            Segment(P(x, bottom), P(x, top), S(0.0008, 0.25, style.D + 0.1, 0.5, (0.005, 0.008))),
        ],
        [Node(P(x, top), 0.024, style.D + 0.7)]);

    public static LumiereLineArtSpec Cells(double cx, double cy, double r, LumiereRng random, int count, LumiereStyle style = default) => Spec(
    [
        Circle(cx, cy, r, S(0.0022, 0.5).Over(style)),
        .. Enumerable.Range(0, count).Select(i =>
        {
            var a = random.Next() * Tau;
            var d = Math.Sqrt(random.Next()) * r * 0.8;
            var rx = 0.012 + random.Next() * 0.012;
            var ry = 0.008 + random.Next() * 0.008;
            var rotation = random.Next() * Math.PI;
            return Path(EllipsePath(cx + Math.Cos(a) * d, cy + Math.Sin(a) * d, rx, ry, rotation), S(0.0009, 0.4, style.D + 0.15 + i * 0.02, 0.15));
        }).ToList(),
    ]);

    public static LumiereLineArtSpec Molecule(double cx, double cy, double r, LumiereStyle style = default)
    {
        LumierePoint[] hex = [.. Enumerable.Range(0, 7).Select(i => P(cx + Math.Cos(i * Tau / 6 + Math.PI / 6) * r, cy + Math.Sin(i * Tau / 6 + Math.PI / 6) * r))];
        LumierePoint Out(int k) => P(hex[k].X + (hex[k].X - cx) * 0.8, hex[k].Y + (hex[k].Y - cy) * 0.8);
        int[] bonds = [0, 2, 4];
        return Spec(
            [
                Path(hex, S(0.0018, 0.55).Over(style)),
                Circle(cx, cy, r * 0.55, S(0.001, 0.35, style.D + 0.2)),
                .. bonds.Select((k, i) => Segment(hex[k], Out(k), S(0.0012, 0.45, style.D + 0.3 + i * 0.05))),
            ],
            bonds.Select((k, i) => Node(Out(k), 0.02, style.D + 0.45, i)));
    }

    // ---- 星象 astral ------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec Orbits(double cx, double cy, double[] radii, double tilt, double flatten, LumiereRng random,
        LumiereStyle style = default)
    {
        var paths = radii.Select((r, i) => Path(EllipsePath(cx, cy, r, r * flatten, tilt), S(0.0012, 0.45).Over(style).Over(S(delay: style.D + i * 0.07, span: 0.5)))).ToList();
        var nodes = new List<LumiereLineNode> { Node(P(cx, cy), 0.04, style.D + 0.2) };
        for (var i = 0; i < radii.Length; i++)
        {
            var a = random.Next() * Tau;
            var at = EllipsePath(cx, cy, radii[i], radii[i] * flatten, tilt, a, a, 1)[0];
            nodes.Add(Node(at, 0.018 + random.Next() * 0.012, style.D + 0.5 + i * 0.05, i));
        }
        return Spec(paths, nodes);
    }

    public static LumiereLineArtSpec StarTrails(double cx, double cy, LumiereRng random, int count, double maxR, LumiereStyle style = default) => Spec(
        Enumerable.Range(0, count).Select(i =>
        {
            var r = maxR * (0.1 + random.Next() * 0.9);
            var a = random.Next() * Tau;
            var span = 0.4 + random.Next() * 0.8;
            var width = 0.0008 + random.Next() * 0.0008;
            var alpha = 0.25 + random.Next() * 0.3;
            return Path(ArcPoints(cx, cy, r, a, a + span, 24), S(width, alpha).Over(style).Over(S(delay: style.D + (double)i / count * 0.4, span: 0.3)));
        }).ToList(),
        [Node(P(cx, cy), 0.03, style.D + 0.1)]);

    public static LumiereLineArtSpec Sextant(double cx, double cy, double r, LumiereStyle style = default) => Merge(
        Spec(
            [
                Path(ArcPoints(cx, cy, r, Math.PI * 0.33, Math.PI * 0.67, 48), S(0.002, 0.6).Over(style)),
                Segment(P(cx, cy), P(cx + Math.Cos(Math.PI * 0.33) * r, cy + Math.Sin(Math.PI * 0.33) * r), S(0.0014, 0.5, style.D + 0.1)),
                Segment(P(cx, cy), P(cx + Math.Cos(Math.PI * 0.67) * r, cy + Math.Sin(Math.PI * 0.67) * r), S(0.0014, 0.5, style.D + 0.12)),
                Segment(P(cx, cy), P(cx + Math.Cos(Math.PI * 0.45) * r * 1.1, cy + Math.Sin(Math.PI * 0.45) * r * 1.1), S(0.001, 0.45, style.D + 0.3, dash: (0.006, 0.006))),
                Segment(P(cx - r * 0.9, cy - r * 0.2), P(cx + r * 0.2, cy - r * 0.05), S(0.0009, 0.35, style.D + 0.35, dash: (0.004, 0.006))),
            ],
            [Node(P(cx, cy), 0.022, style.D + 0.3)]),
        Rays(cx, cy, r * 0.94, r, 30, Math.PI * 0.33, Math.PI * 0.67, S(alpha: 0.4, delay: style.D + 0.2)));

    public static LumiereLineArtSpec Constellation(double aspect, LumiereRng random, int count, (double X0, double Y0, double X1, double Y1) region,
        LumiereStyle style = default)
    {
        var (x0, y0, x1, y1) = region;
        var stars = new List<LumierePoint>();
        for (var i = 0; i < count; i++)
        {
            var x = (x0 + random.Next() * (x1 - x0)) * aspect;
            stars.Add(P(x, y0 + random.Next() * (y1 - y0)));
        }
        stars = [.. stars.OrderBy(star => star.X)];
        return Spec(
            stars.Skip(1).Select((star, i) => Segment(stars[i], star, S(0.001, 0.4).Over(style).Over(S(delay: style.D + 0.2 + i * 0.06, span: 0.2)))).ToList(),
            stars.Select((at, i) => Node(at, 0.018 + random.Next() * 0.016, style.D + i * 0.05, i)).ToList());
    }

    public static LumiereLineArtSpec Corona(double cx, double cy, double r, LumiereRng random, LumiereStyle style = default) => Spec(
        [
            Circle(cx, cy, r, S(0.0024, 0.65).Over(style)),
            .. Enumerable.Range(0, 40).Select(i =>
            {
                var a = i / 40.0 * Tau + random.Next() * 0.05;
                var len = r * (0.25 + random.Next() * 0.9);
                var alpha = 0.25 + random.Next() * 0.3;
                return Segment(P(cx + Math.Cos(a) * r * 1.05, cy + Math.Sin(a) * r * 1.05), P(cx + Math.Cos(a) * (r + len), cy + Math.Sin(a) * (r + len)),
                    S(0.0008, alpha, style.D + 0.2 + i / 40.0 * 0.3, 0.15));
            }).ToList(),
        ],
        [Node(P(cx + r * 0.7, cy - r * 0.7), 0.03, style.D + 0.5)]);

    public static LumiereLineArtSpec Crescent(double cx, double cy, double r, LumiereStyle style = default) => Spec(
    [
        Path(ArcPoints(cx, cy, r, Math.PI * 0.35, Math.PI * 1.65, 60), S(0.002, 0.6).Over(style)),
        Path(ArcPoints(cx + r * 0.45, cy, r * 0.85, Math.PI * 0.58, Math.PI * 1.42, 60), S(0.0012, 0.4, style.D + 0.15)),
    ]);

    public static LumiereLineArtSpec Armillary(double cx, double cy, double r, LumiereStyle style = default) => Spec(
        [
            Circle(cx, cy, r, S(0.002, 0.55).Over(style)),
            Path(EllipsePath(cx, cy, r, r * 0.3, 0), S(0.0014, 0.45, style.D + 0.1)),
            Path(EllipsePath(cx, cy, r, r * 0.3, 0.41), S(0.0014, 0.45, style.D + 0.15)),
            Path(EllipsePath(cx, cy, r * 0.3, r, 0), S(0.0012, 0.4, style.D + 0.2)),
            Segment(P(cx - Math.Sin(0.41) * r * 1.25, cy - Math.Cos(0.41) * r * 1.25), P(cx + Math.Sin(0.41) * r * 1.25, cy + Math.Cos(0.41) * r * 1.25), S(0.0012, 0.45, style.D + 0.3)),
        ],
        [Node(P(cx, cy), 0.03, style.D + 0.3)]);

    public static LumiereLineArtSpec Meteors(double aspect, LumiereRng random, int count, LumiereStyle style = default)
    {
        var paths = new List<LumiereLinePath>();
        var nodes = new List<LumiereLineNode>();
        for (var i = 0; i < count; i++)
        {
            var x = (0.2 + random.Next() * 0.8) * aspect;
            var y = random.Next() * 0.5;
            var len = 0.12 + random.Next() * 0.25;
            var head = P(x - len * 0.8, y + len * 0.6);
            paths.Add(Segment(P(x, y), head, S(0.0012 + random.Next() * 0.001, 0.45).Over(style).Over(S(delay: style.D + i * 0.08, span: 0.2))));
            nodes.Add(Node(head, 0.02, style.D + i * 0.08 + 0.2, i));
        }
        return Spec(paths, nodes);
    }

    // ---- 追光 stage -------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec LampHead(double x, double y, double angle, double size, LumiereStyle style = default)
    {
        var d = P(Math.Cos(angle), Math.Sin(angle));
        var n = P(-d.Y, d.X);
        var back = P(x - d.X * size, y - d.Y * size);
        LumierePoint[] corners =
        [
            P(back.X + n.X * size * 0.35, back.Y + n.Y * size * 0.35),
            P(x + n.X * size * 0.6, y + n.Y * size * 0.6),
            P(x - n.X * size * 0.6, y - n.Y * size * 0.6),
            P(back.X - n.X * size * 0.35, back.Y - n.Y * size * 0.35),
        ];
        return Spec([Path([.. corners, corners[0]], S(0.0018, 0.55, span: 0.3).Over(style))]);
    }

    public static LumiereLineArtSpec Curtains(double aspect, double gap, LumiereStyle style = default) => Spec(
        new[] { -1, 1 }.SelectMany(side => Enumerable.Range(0, 6).Select(i =>
        {
            var basis = aspect / 2 + side * (gap / 2 + i * 0.05);
            return Path([.. Enumerable.Range(0, 41).Select(k =>
            {
                var t = k / 40.0;
                return P(basis + Math.Sin(t * 5 + i) * 0.008 * side, -0.02 + t * 1.04);
            })], S(0.001 + (5 - i) * 0.0002, 0.2 + (5 - i) * 0.05).Over(style).Over(S(delay: style.D + i * 0.05, span: 0.5)));
        })));

    public static LumiereLineArtSpec Projector(double x, double y, double size, LumiereStyle style = default) => Spec(
        [
            Path([P(x - size, y - size * 0.3), P(x, y - size * 0.3), P(x, y + size * 0.3), P(x - size, y + size * 0.3), P(x - size, y - size * 0.3)], S(0.0018, 0.55).Over(style)),
            Circle(x - size * 0.75, y - size * 0.65, size * 0.33, S(0.0014, 0.5, style.D + 0.1)),
            Circle(x - size * 0.25, y - size * 0.65, size * 0.33, S(0.0014, 0.5, style.D + 0.15)),
            Path([P(x, y - size * 0.12), P(x + size * 0.18, y - size * 0.18), P(x + size * 0.18, y + size * 0.18), P(x, y + size * 0.12)], S(0.0014, 0.5, style.D + 0.2)),
        ],
        [Node(P(x + size * 0.18, y), 0.024, style.D + 0.3)]);

    // ---- 通用 -------------------------------------------------------------------------------------------------

    public static LumiereLineArtSpec Horizon(double aspect, double y, LumiereStyle style = default) => Spec(
    [
        Segment(P(0, y), P(aspect, y), S(0.0014, 0.4).Over(style).Over(S(span: 0.6))),
        .. Enumerable.Range(0, 16).Select(i => Segment(P(aspect * (i + 0.5) / 16, y), P(aspect * (i + 0.5) / 16, y + 0.012), S(0.0008, 0.3, style.D + 0.3 + i * 0.01, 0.08))),
    ]);
}
