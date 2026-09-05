using System.Numerics;

namespace AvaloniaSilkEffects;

/// <summary>Triangulates a simple contour, including concave and reversed contours.</summary>
internal static class PolygonTriangulator
{
    internal static int[] Triangulate(IReadOnlyList<Vector2> points)
    {
        var remaining = new List<int>();
        for (var i = 0; i < points.Count; i++)
        {
            if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                throw new ArgumentException("Polygon coordinates must be finite.", nameof(points));
            if (remaining.Count == 0 || points[i] != points[remaining[^1]]) remaining.Add(i);
        }
        if (remaining.Count > 1 && points[remaining[0]] == points[remaining[^1]]) remaining.RemoveAt(remaining.Count - 1);
        // Remove redundant straight-line vertices before ear clipping.
        var changed = true;
        while (changed && remaining.Count >= 3)
        {
            changed = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var a = points[remaining[(i + remaining.Count - 1) % remaining.Count]];
                var b = points[remaining[i]];
                var c = points[remaining[(i + 1) % remaining.Count]];
                if (Cross(a, b, c) != 0 || Vector2.Dot(b - a, b - c) > 0) continue;
                remaining.RemoveAt(i);
                changed = true;
                break;
            }
        }
        if (remaining.Count < 3) return [];
        double area = 0;
        for (var i = 0; i < remaining.Count; i++)
        {
            var a = points[remaining[i]];
            var b = points[remaining[(i + 1) % remaining.Count]];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        if (area == 0) return [];
        var winding = Math.Sign(area);
        var triangles = new List<int>((remaining.Count - 2) * 3);
        while (remaining.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var ai = remaining[(i + remaining.Count - 1) % remaining.Count];
                var bi = remaining[i];
                var ci = remaining[(i + 1) % remaining.Count];
                var a = points[ai]; var b = points[bi]; var c = points[ci];
                if (Cross(a, b, c) * winding <= 0) continue;
                var occupied = false;
                foreach (var index in remaining)
                {
                    if (index == ai || index == bi || index == ci) continue;
                    var p = points[index];
                    if (Cross(a, b, p) * winding >= 0 && Cross(b, c, p) * winding >= 0 && Cross(c, a, p) * winding >= 0)
                    { occupied = true; break; }
                }
                if (occupied) continue;
                triangles.Add(ai); triangles.Add(bi); triangles.Add(ci);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
                throw new ArgumentException("Polygon must be a simple contour without intersections or holes.", nameof(points));
        }
        triangles.AddRange(remaining);
        return triangles.ToArray();
    }

    private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
        ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
}
