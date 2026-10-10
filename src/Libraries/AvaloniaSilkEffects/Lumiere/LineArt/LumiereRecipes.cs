using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere.LineArt;

/// <summary>Folia lineart/recipes.ts: geometric primitives → line art. Height units; aspect is width / height.</summary>
public static class LumiereRecipes
{
    public static LumierePoint P(double x, double y) => new(x, y);

    public static LumierePoint[] ArcPoints(double cx, double cy, double r, double from, double to, int steps = 64)
    {
        var points = new LumierePoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = from + (to - from) * i / steps;
            points[i] = new LumierePoint(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
        }
        return points;
    }

    public static LumierePoint[] CubicPoints(LumierePoint p0, LumierePoint p1, LumierePoint p2, LumierePoint p3, int steps = 40)
    {
        var points = new LumierePoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            points[i] = new LumierePoint(
                a * p0.X + b * p1.X + c * p2.X + d * p3.X,
                a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
        }
        return points;
    }

    private static LumierePoint[] Line(LumierePoint a, LumierePoint b) => [a, b];

    /// <summary>A protractor-like halo around the source: lower outer + inner arcs, spokes, fine ticks on the outer arc.</summary>
    public static LumiereLineArtSpec ProtractorHalo(double cx, double cy, double radius, double delay = 0, double alpha = 0.5,
        double? spokeStep = null)
    {
        var from = Math.PI * 0.04;
        var to = Math.PI * 0.96;
        var inner = radius * 0.62;
        var paths = new List<LumiereLinePath>
        {
            new(ArcPoints(cx, cy, radius, to, from, 96), 0.0016, alpha, delay, 0.45),
            new(ArcPoints(cx, cy, inner, from, to, 72), 0.0013, alpha * 0.8, delay + 0.08, 0.4),
            new(ArcPoints(cx, cy, radius * 1.18, from + 0.1, to - 0.1, 80), 0.0011, alpha * 0.45, delay + 0.2, 0.4, (0.004, 0.009)),
        };
        var step = spokeStep ?? Math.PI / 12;
        var nodes = new List<LumiereLineNode>();
        var index = 0;
        for (var a = Math.PI / 2 - step * 5; a <= Math.PI / 2 + step * 5 + 1e-6; a += step)
        {
            var isLong = index % 2 == 0;
            var r0 = inner * 0.2;
            var r1 = radius * (isLong ? 1.12 : 1.02);
            paths.Add(new LumiereLinePath(Line(P(cx + Math.Cos(a) * r0, cy + Math.Sin(a) * r0), P(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1)),
                isLong ? 0.0013 : 0.0009, alpha * (isLong ? 0.7 : 0.45), delay + 0.12 + Math.Abs(a - Math.PI / 2) * 0.12, 0.3));
            if (isLong) nodes.Add(new LumiereLineNode(P(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius), 0.022, delay + 0.45, index * 1.7));
            index++;
        }
        for (var a = from; a <= to; a += Math.PI / 72)
            paths.Add(new LumiereLinePath(Line(P(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius),
                P(cx + Math.Cos(a) * radius * 0.975, cy + Math.Sin(a) * radius * 0.975)), 0.0008, alpha * 0.4, delay + 0.25 + (a - from) * 0.05, 0.15));
        return new LumiereLineArtSpec(paths, nodes);
    }

    /// <summary>Viewfinder: a big rectangle, heavy corner brackets, edge ticks and two dotted verticals.</summary>
    public static LumiereLineArtSpec ViewfinderFrame(double aspect, double left, double top, double right, double bottom,
        double delay = 0, double alpha = 0.3)
    {
        left *= aspect;
        right *= aspect;
        const double corner = 0.03;
        var paths = new List<LumiereLinePath>
        {
            new([P(left, top), P(right, top), P(right, bottom), P(left, bottom), P(left, top)], 0.0009, alpha * 0.55, delay, 0.6),
        };
        LumierePoint[][] corners =
        [
            [P(left, top + corner), P(left, top), P(left + corner, top)],
            [P(right - corner, top), P(right, top), P(right, top + corner)],
            [P(right, bottom - corner), P(right, bottom), P(right - corner, bottom)],
            [P(left + corner, bottom), P(left, bottom), P(left, bottom - corner)],
        ];
        for (var i = 0; i < corners.Length; i++) paths.Add(new LumiereLinePath(corners[i], 0.0018, alpha, delay + 0.1 + i * 0.05, 0.2));
        const int ticks = 12;
        for (var i = 1; i < ticks; i++)
        {
            var x = left + (right - left) * i / ticks;
            var y = top + (bottom - top) * i / ticks;
            var len = i % 3 == 0 ? 0.014 : 0.007;
            var d = delay + 0.3 + i * 0.01;
            paths.Add(new LumiereLinePath(Line(P(x, top), P(x, top + len)), 0.0008, alpha * 0.6, d, 0.1));
            paths.Add(new LumiereLinePath(Line(P(x, bottom), P(x, bottom - len)), 0.0008, alpha * 0.6, d, 0.1));
            paths.Add(new LumiereLinePath(Line(P(left, y), P(left + len, y)), 0.0008, alpha * 0.6, d, 0.1));
            paths.Add(new LumiereLinePath(Line(P(right, y), P(right - len, y)), 0.0008, alpha * 0.6, d, 0.1));
        }
        double[] guideX = [left + (right - left) * 0.14, right - (right - left) * 0.14];
        for (var i = 0; i < guideX.Length; i++)
            paths.Add(new LumiereLinePath(Line(P(guideX[i], top + 0.06), P(guideX[i], bottom - 0.06)), 0.001, alpha * 0.5,
                delay + 0.35 + i * 0.05, 0.4, (0.003, 0.012)));
        return new LumiereLineArtSpec(paths, []);
    }

    /// <summary>A sprout rising from the bottom, two leaves at the top (outline + midrib + side veins), sparkles on tips.</summary>
    public static LumiereLineArtSpec Sprout(double cx, double baseY, double size, double delay = 0, double alpha = 0.55, double open = 1)
    {
        var paths = new List<LumiereLinePath>
        {
            new(CubicPoints(P(cx, 1.04), P(cx + 0.01, 0.94), P(cx - 0.008, baseY + 0.08), P(cx, baseY)), 0.0016, alpha, delay, 0.3),
        };
        var nodes = new List<LumiereLineNode>();
        void Leaf(int side, int index)
        {
            var lean = (0.2 + 0.2 * open) * Math.PI;
            var d = P(Math.Sin(lean) * side, -Math.Cos(lean));
            var n = P(-d.Y, d.X);
            var basis = P(cx, baseY);
            var tip = P(basis.X + d.X * size, basis.Y + d.Y * size - size * 0.12);
            var bulge = size * 0.34;
            LumierePoint At(double along, double across) => P(
                basis.X + d.X * size * along + n.X * across,
                basis.Y + d.Y * size * along + n.Y * across - size * 0.12 * along * along);
            var leafDelay = delay + 0.22 + index * 0.08;
            paths.Add(new LumiereLinePath(CubicPoints(basis, At(0.3, bulge), At(0.78, bulge * 0.9), tip, 48), 0.0026, alpha, leafDelay, 0.35));
            paths.Add(new LumiereLinePath(CubicPoints(basis, At(0.3, -bulge * 0.95), At(0.8, -bulge * 0.6), tip, 48), 0.0026, alpha, leafDelay + 0.04, 0.35));
            paths.Add(new LumiereLinePath(CubicPoints(basis, At(0.35, bulge * 0.08), At(0.7, bulge * 0.05), tip, 32), 0.0011, alpha * 0.7, leafDelay + 0.12, 0.3));
            for (var v = 1; v <= 4; v++)
            {
                var f = v / 5.2;
                var start = At(f, bulge * 0.04);
                foreach (var s in new[] { 1, -1 })
                {
                    var end = At(f + 0.14, s * bulge * (0.72 - f * 0.35));
                    paths.Add(new LumiereLinePath(CubicPoints(start, At(f + 0.04, s * bulge * 0.25), At(f + 0.1, s * bulge * 0.5), end, 16),
                        0.0008, alpha * 0.45, leafDelay + 0.2 + v * 0.03, 0.18));
                }
            }
            nodes.Add(new LumiereLineNode(tip, 0.03, leafDelay + 0.3, index * 2.1));
            nodes.Add(new LumiereLineNode(At(0.45, bulge * 0.86), 0.014, leafDelay + 0.33, index * 2.1 + 1));
            nodes.Add(new LumiereLineNode(At(0.6, -bulge * 0.8), 0.012, leafDelay + 0.36, index * 2.1 + 2));
        }
        Leaf(-1, 0);
        Leaf(1, 1);
        nodes.Add(new LumiereLineNode(P(cx, baseY), 0.02, delay + 0.3, 0.5));
        return new LumiereLineArtSpec(paths, nodes);
    }

    /// <summary>Small sparkles scattered over a region (frame fractions x0, y0, x1, y1).</summary>
    public static LumiereLineArtSpec ScatteredSparks(double aspect, int count, LumiereRng random, double delay = 0,
        (double X0, double Y0, double X1, double Y1)? region = null)
    {
        var (x0, y0, x1, y1) = region ?? (0.05, 0.08, 0.95, 0.92);
        var nodes = new LumiereLineNode[count];
        for (var i = 0; i < count; i++)
        {
            var x = (x0 + (x1 - x0) * random.Next()) * aspect;
            var y = y0 + (y1 - y0) * random.Next();
            var size = 0.006 + random.Next() * 0.012;
            nodes[i] = new LumiereLineNode(P(x, y), size, delay + random.Next() * 0.5, i * 2.399);
        }
        return new LumiereLineArtSpec([], nodes);
    }

    /// <summary>Venetian blinds: window frame + a row of horizontal slats.</summary>
    public static LumiereLineArtSpec BlindsWindow(double x, double y, double w, double h, int slats, double delay = 0, double alpha = 0.5)
    {
        var paths = new List<LumiereLinePath> { new([P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h), P(x, y)], 0.0018, alpha, delay, 0.5) };
        for (var i = 1; i < slats; i++)
        {
            var sy = y + h * i / slats;
            paths.Add(new LumiereLinePath(Line(P(x, sy), P(x + w, sy)), 0.001, alpha * 0.6, delay + 0.1 + i * 0.03, 0.2));
        }
        return new LumiereLineArtSpec(paths, [new LumiereLineNode(P(x + w, y + h), 0.018, delay + 0.4, 1)]);
    }

    /// <summary>A triangular prism outline: center and side length (height units).</summary>
    public static LumiereLineArtSpec PrismTriangle(double cx, double cy, double size, double delay = 0, double alpha = 0.7)
    {
        var h = size * Math.Sqrt(3) / 2;
        var top = P(cx, cy - h * 2 / 3);
        var left = P(cx - size / 2, cy + h / 3);
        var right = P(cx + size / 2, cy + h / 3);
        return new LumiereLineArtSpec(
            [
                new LumiereLinePath([top, right, left, top], 0.0022, alpha, delay, 0.5),
                new LumiereLinePath([top, P(cx, cy + h / 3)], 0.0008, alpha * 0.4, delay + 0.3, 0.3, (0.004, 0.008)),
            ],
            [.. new[] { top, left, right }.Select((at, i) => new LumiereLineNode(at, 0.02, delay + 0.4 + i * 0.05, i * 1.3))]);
    }

    /// <summary>A double-slit barrier: a line with two gaps.</summary>
    public static LumiereLineArtSpec SlitBarrier(double cx, double y, double width, double gap, double separation, double delay = 0,
        double alpha = 0.6)
    {
        var a = cx - separation / 2;
        var b = cx + separation / 2;
        (double From, double To)[] segments = [(cx - width / 2, a - gap / 2), (a + gap / 2, b - gap / 2), (b + gap / 2, cx + width / 2)];
        return new LumiereLineArtSpec(
            [.. segments.Select((s, i) => new LumiereLinePath(Line(P(s.From, y), P(s.To, y)), 0.0026, alpha, delay + i * 0.08, 0.3))],
            [.. new[] { a, b }.Select((x, i) => new LumiereLineNode(P(x, y), 0.022, delay + 0.35 + i * 0.05, i * 2))]);
    }
}
