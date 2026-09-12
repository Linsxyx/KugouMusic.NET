using KugouAvaloniaPlayer.Controls;

namespace AvaloniaSilkEffects.Tests;

public sealed class FumeBackgroundTests
{
    [Theory]
    [InlineData(1280, 720, 8, 12)]
    [InlineData(390, 844, 7, 9)]
    public void SceneIsStableLayeredAndSeparatesHaloFromAudioSparks(
        int width, int height, int haloCount, int sparkCount)
    {
        var shapes = FumeBackgroundScene.Build(null, width, height);
        Assert.Equal(shapes, FumeBackgroundScene.Build(null, width, height));
        Assert.Equal(haloCount, shapes.Count(s => s.Kind != FumeShapeKind.Spark));
        Assert.Equal(sparkCount, shapes.Count(s => s.Kind == FumeShapeKind.Spark));
        Assert.Equal(shapes.OrderBy(s => s.Depth), shapes);
        foreach (var shape in shapes)
        {
            Assert.InRange(shape.X, 0, width * 1.8);
            Assert.InRange(shape.Y, 0, height * 1.8);
            Assert.True(shape.StrokeWidth > 0);
            if (shape.Kind == FumeShapeKind.Spark)
            {
                Assert.InRange(shape.AudioBand, 1, 4);
                Assert.InRange(shape.X, width * 1.8 * 0.2, width * 1.8 * 0.8);
                Assert.InRange(shape.Y, height * 1.8 * 0.18, height * 1.8 * 0.82);
            }
            else
                Assert.Equal(-1, shape.AudioBand);
        }
    }

    [Fact]
    public void HaloGeometryHasSpatialGradientAndOpenRingGaps()
    {
        foreach (var shape in FumeBackgroundScene.Build(null, 1280, 720))
        {
            var node = FumeEffectScene.BuildGeometry(shape);
            if (shape.Kind == FumeShapeKind.Spark)
            {
                Assert.Null(node.PointColors);
                Assert.Equal(node.Points[0], node.Points[^1]);
                continue;
            }
            Assert.NotNull(node.PointColors);
            Assert.Equal(node.Points.Count, node.PointColors.Count);
            Assert.True(node.PointColors.Max(c => c.A) - node.PointColors.Min(c => c.A) > 0.2);
            Assert.True(node.PointColors.Max(c => c.R) - node.PointColors.Min(c => c.R) > 0.1);
            if (shape.Kind == FumeShapeKind.Ring)
                Assert.NotEqual(node.Points[0], node.Points[^1]);
        }
    }
}
