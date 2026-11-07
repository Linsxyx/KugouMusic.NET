using System.Text.Json;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using Xunit;

namespace KaMusic.Tests;

public class WaylandLyricProtocolTests
{
    [Fact]
    public void State_UsesNativeSnakeCaseAndAbsoluteWordTimes()
    {
        var line = new LyricLine
        {
            Text = "你\t好\n🎵",
            Translation = "hello",
            Start = TimeSpan.FromSeconds(10),
            Duration = TimeSpan.FromSeconds(2),
            Words = [new LyricWord { Text = "你", Start = TimeSpan.FromMilliseconds(10200), Duration = TimeSpan.FromMilliseconds(300) }]
        };
        var state = new WaylandLyricState
        {
            PositionMs = 10300,
            Current = WaylandLyricLine.FromLine(line),
            Placement = new WaylandLyricPlacement { HasValue = true, Monitor = "DP-1", X = 24, Y = 48 }
        };
        var wire = JsonSerializer.Serialize(state, WaylandLyricsJsonContext.Default.WaylandLyricState);
        using var json = JsonDocument.Parse(wire);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(10300, root.GetProperty("position_ms").GetDouble());
        Assert.Equal(line.Text, root.GetProperty("current").GetProperty("text").GetString());
        Assert.Equal(10200, root.GetProperty("current").GetProperty("words")[0].GetProperty("start_ms").GetDouble());
        Assert.True(root.GetProperty("placement").GetProperty("has_value").GetBoolean());
        Assert.Equal("DP-1", root.GetProperty("placement").GetProperty("monitor").GetString());
    }

    [Fact]
    public void PlacementEvent_RoundTripsOutputLocalCoordinates()
    {
        const string wire = """{"has_value":true,"x":240,"y":120,"monitor":"HDMI-A-1"}""";
        var placement = JsonSerializer.Deserialize(wire, WaylandLyricsJsonContext.Default.WaylandLyricPlacement)!;
        Assert.True(placement.HasValue);
        Assert.Equal(240, placement.X);
        Assert.Equal(120, placement.Y);
        Assert.Equal("HDMI-A-1", placement.Monitor);
    }

    [Fact]
    public void MissingLyric_ProducesNullInsteadOfStaleText()
    {
        Assert.Null(WaylandLyricLine.FromLine(null));
    }
}
