using AvaloniaSilkEffects.Sonnet;
using System.Text.Json;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetDrawListTests
{
    [Fact]
    public void DrawList_ReplaysCurvesAsContinuousSceneNodes()
    {
        var list = new SonnetDrawList()
            .MoveTo(0, 0).QuadraticCurveTo(30, -40, 60, 0).BezierCurveTo(80, 30, 100, -30, 120, 0)
            .Stroke(0xff4081, 3, 0.7)
            .MoveTo(-20, -20).LineTo(20, -20).LineTo(20, 20).LineTo(-20, 20).LineTo(-20, -20)
            .Fill(0x55ccff, 0.4);

        var root = list.Replay(16);

        Assert.Equal(2, list.Commands.Count);
        Assert.Contains(root.Children, node => node is PolylineNode polyline && polyline.Points.Count > 20);
        Assert.Contains(root.Children, node => node is PolygonNode polygon && polygon.Points.Count == 5);
    }

    [Fact]
    public void DrawList_StaggerScheduleIsDeterministicAndSeparatedByPaintKind()
    {
        static SonnetDrawList Build() => new SonnetDrawList()
            .MoveTo(0, 0).LineTo(10, 0).Stroke(0xffffff, 1, 1)
            .MoveTo(0, 0).LineTo(0, 10).Fill(0xffffff, 0.5)
            .MoveTo(1, 1).LineTo(2, 2).Stroke(0xffffff, 2, 0.8);

        Assert.Equal(JsonSerializer.Serialize(Build().Commands), JsonSerializer.Serialize(Build().Commands));
        Assert.Equal(0, Build().Commands[0].StaggerDelay);
        Assert.Equal(0, Build().Commands[1].StaggerDelay);
        Assert.True(Build().Commands[2].StaggerDelay > 0);
    }
}
