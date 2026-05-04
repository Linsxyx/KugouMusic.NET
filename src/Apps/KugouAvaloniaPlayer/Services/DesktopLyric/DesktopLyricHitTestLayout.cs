using System.Collections.Generic;
using Avalonia;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public enum DesktopLyricHitTestMode
{
    FullWindow,
    Transparent,
    Region
}

public sealed record DesktopLyricHitTestLayout(
    DesktopLyricHitTestMode Mode,
    IReadOnlyList<PixelRect>? InteractiveRegions = null)
{
    public static DesktopLyricHitTestLayout FullWindow { get; } =
        new(DesktopLyricHitTestMode.FullWindow);

    public static DesktopLyricHitTestLayout Transparent { get; } =
        new(DesktopLyricHitTestMode.Transparent);

    public static DesktopLyricHitTestLayout ForRegions(params PixelRect[] regions)
    {
        return new DesktopLyricHitTestLayout(DesktopLyricHitTestMode.Region, regions);
    }
}
