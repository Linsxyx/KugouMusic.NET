using System;
using System.Linq;
using System.Text.Json.Serialization;
using AvaloniaLyrics;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed class WaylandLyricPlacement
{
    public bool HasValue { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Monitor { get; set; } = string.Empty;
}

internal sealed class WaylandLyricState
{
    public int Version { get; init; } = 1;
    public double PositionMs { get; init; }
    public bool Playing { get; init; }
    public double Speed { get; init; }
    public int Index { get; init; }
    public WaylandLyricLine? Current { get; init; }
    public WaylandLyricLine? Next { get; init; }
    public string Font { get; init; } = "sans-serif";
    public double FontSize { get; init; }
    public string Color { get; init; } = "#FFFFFFFF";
    public string TranslationColor { get; init; } = "#CCFFFFFF";
    public bool Translation { get; init; }
    public bool DoubleLine { get; init; }
    public bool Vertical { get; init; }
    public string Alignment { get; init; } = "Center";
    public bool Locked { get; init; }
    public bool ControlsBelow { get; init; }
    public bool OverFullscreen { get; init; }
    public WaylandLyricPlacement Placement { get; init; } = new();
}

internal sealed class WaylandLyricLine
{
    public string Text { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public double StartMs { get; init; }
    public double DurationMs { get; init; }
    public WaylandLyricWord[] Words { get; init; } = [];

    public static WaylandLyricLine? FromLine(LyricLine? line) => line == null ? null : new()
    {
        Text = line.Text,
        Translation = line.Translation ?? string.Empty,
        StartMs = line.Start.TotalMilliseconds,
        DurationMs = line.Duration.TotalMilliseconds,
        Words = line.Words.Select(w => new WaylandLyricWord(w.Text, w.Start.TotalMilliseconds, w.Duration.TotalMilliseconds)).ToArray()
    };
}

internal sealed record WaylandLyricWord(string Text, double StartMs, double DurationMs);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WaylandLyricState))]
[JsonSerializable(typeof(WaylandLyricPlacement))]
internal partial class WaylandLyricsJsonContext : JsonSerializerContext;
