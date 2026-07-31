using System.Collections.Generic;

namespace KugouAvaloniaPlayer.Models;

// Defines the discoverable playback-page themes shown by the Now Playing surface.
public enum NowPlayingThemePreset
{
    Standard,
    Pendolo,
    Fume
}

public sealed record NowPlayingThemePresetOption(
    NowPlayingThemePreset Preset,
    string DisplayName);

public static class NowPlayingThemePresetRegistry
{
    public static IReadOnlyList<NowPlayingThemePresetOption> Presets { get; } =
    [
        new(
            NowPlayingThemePreset.Pendolo,
            "摆钟"),
        new(
            NowPlayingThemePreset.Fume,
            "浮名"),
        new(
            NowPlayingThemePreset.Standard,
            "经典")
    ];

    public static NowPlayingThemePreset Normalize(NowPlayingThemePreset preset)
    {
        return preset is NowPlayingThemePreset.Standard or
            NowPlayingThemePreset.Pendolo or
            NowPlayingThemePreset.Fume
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
