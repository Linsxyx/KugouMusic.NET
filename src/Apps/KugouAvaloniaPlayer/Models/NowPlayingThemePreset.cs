using System.Collections.Generic;

namespace KugouAvaloniaPlayer.Models;

// Defines the discoverable playback-page themes shown by the Now Playing surface.
public enum NowPlayingThemePreset
{
    Standard,
    Pendolo
}

public sealed record NowPlayingThemePresetOption(
    NowPlayingThemePreset Preset,
    string DisplayName,
    string Description);

public static class NowPlayingThemePresetRegistry
{
    public static IReadOnlyList<NowPlayingThemePresetOption> Presets { get; } =
    [
        new(
            NowPlayingThemePreset.Pendolo,
            "Pendolo",
            "机械擒纵轮、弧形歌词与音频响应"),
        new(
            NowPlayingThemePreset.Standard,
            "Standard",
            "经典封面与滚动歌词布局")
    ];

    public static NowPlayingThemePreset Normalize(NowPlayingThemePreset preset)
    {
        return preset is NowPlayingThemePreset.Standard or NowPlayingThemePreset.Pendolo
            ? preset
            : NowPlayingThemePreset.Standard;
    }

    public static NowPlayingThemePresetOption Get(NowPlayingThemePreset preset)
    {
        var normalized = Normalize(preset);
        foreach (var option in Presets)
        {
            if (option.Preset == normalized)
                return option;
        }

        return Presets[^1];
    }
}
