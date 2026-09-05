using System.Numerics;
using Xunit;

namespace AvaloniaSilkEffects.Tests;

public sealed class PolygonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcaveContourPreservesAreaAndEmptyNotch(bool reverse)
    {
        Vector2[] points = [new(0, 0), new(4, 0), new(4, 4), new(3, 4),
            new(3, 1), new(1, 1), new(1, 4), new(0, 4), new(0, 0)];
        if (reverse) Array.Reverse(points);
        var indices = PolygonTriangulator.Triangulate(points);
        double area = 0;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = points[indices[i]]; var b = points[indices[i + 1]]; var c = points[indices[i + 2]];
            area += Math.Abs(Cross(b - a, c - a)) / 2;
            var center = (a + b + c) / 3;
            Assert.False(center.X > 1 && center.X < 3 && center.Y > 1);
        }
        Assert.Equal(10, area, 6);
        Assert.Equal(18, indices.Length);
    }

    [Fact]
    public void PolygonOwnsGeometryAndRebuildsIndicesOnlyWhenAssigned()
    {
        Vector2[] points = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var node = new PolygonNode { Points = points };
        var cached = node.TriangleIndices;
        points[0] = new(99, 99);
        Assert.Equal(Vector2.Zero, node.Points[0]);
        Assert.Same(cached, node.TriangleIndices);
        node.Points = [new(0, 0), new(2, 0), new(0, 2)];
        Assert.Equal(3, node.TriangleIndices.Length);
        Assert.NotSame(cached, node.TriangleIndices);
    }

    [Fact]
    public void DuplicateAndCollinearVerticesDoNotProduceExtraCoverage()
    {
        var indices = PolygonTriangulator.Triangulate(
            [new(0, 0), new(1, 0), new(1, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0)]);
        Assert.Equal(6, indices.Length);
    }

    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
}
