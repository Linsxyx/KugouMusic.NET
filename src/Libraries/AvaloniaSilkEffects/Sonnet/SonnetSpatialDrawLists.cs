namespace AvaloniaSilkEffects.Sonnet;

// Exact port of the reusable prism recipes in sonnetSpatialMgGeometry.ts.
public static class SonnetSpatialDrawLists
{
    public static SonnetDrawList SolidCuboid(
        double x, double y, double width, double height, double depthX, double depthY,
        uint color, double alpha)
    {
        var target = new SonnetDrawList();
        var left = x - width / 2; var right = x + width / 2;
        var top = y - height / 2; var bottom = y + height / 2;
        DrawFace(target, [(left, top), (left + depthX, top + depthY), (right + depthX, top + depthY), (right, top)], color, alpha * 0.42);
        DrawFace(target, [(right, top), (right + depthX, top + depthY), (right + depthX, bottom + depthY), (right, bottom)], color, alpha * 0.68);
        DrawFace(target, [(left, top), (right, top), (right, bottom), (left, bottom)], color, alpha * 0.24);
        return target;
    }

    public static SonnetDrawList TriangularPrism(
        double x, double y, double width, double height, double depthX, double depthY,
        uint color, double alpha) => ExtrudedPolygon(
            [(x, y - height / 2), (x + width / 2, y + height / 2), (x - width / 2, y + height / 2)],
            depthX, depthY, color, alpha);

    public static SonnetDrawList HexagonalPrism(
        double x, double y, double width, double height, double depthX, double depthY,
        uint color, double alpha) => ExtrudedPolygon(
            [(x - width * 0.25, y - height / 2), (x + width * 0.25, y - height / 2),
             (x + width / 2, y), (x + width * 0.25, y + height / 2),
             (x - width * 0.25, y + height / 2), (x - width / 2, y)],
            depthX, depthY, color, alpha);

    public static SonnetDrawList TrapezoidPrism(
        double x, double y, double topWidth, double bottomWidth, double height,
        double depthX, double depthY, uint color, double alpha) => ExtrudedPolygon(
            [(x - topWidth / 2, y - height / 2), (x + topWidth / 2, y - height / 2),
             (x + bottomWidth / 2, y + height / 2), (x - bottomWidth / 2, y + height / 2)],
            depthX, depthY, color, alpha);

    private static SonnetDrawList ExtrudedPolygon(
        IReadOnlyList<(double X, double Y)> front, double depthX, double depthY,
        uint color, double alpha)
    {
        var target = new SonnetDrawList();
        var back = front.Select(point => (point.X + depthX, point.Y + depthY)).ToArray();
        for (var index = 0; index < front.Count; index++)
        {
            var next = (index + 1) % front.Count;
            DrawFace(target, [front[index], back[index], back[next], front[next]],
                color, alpha * (0.34 + index % 3 * 0.12));
        }
        DrawFace(target, front, color, alpha * 0.22);
        return target;
    }

    private static void DrawFace(
        SonnetDrawList target, IReadOnlyList<(double X, double Y)> points,
        uint color, double alpha)
    {
        target.MoveTo(points[0].X, points[0].Y);
        for (var index = 1; index < points.Count; index++)
            target.LineTo(points[index].X, points[index].Y);
        target.LineTo(points[0].X, points[0].Y).Fill(color, alpha);
    }
}
