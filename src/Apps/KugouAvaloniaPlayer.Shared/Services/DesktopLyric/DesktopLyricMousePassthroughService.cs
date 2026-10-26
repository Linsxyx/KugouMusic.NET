using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public interface IDesktopLyricMousePassthroughService
{
    bool IsSupported { get; }
    bool SupportsSelectiveHitTesting { get; }
    void Apply(Window window, DesktopLyricHitTestLayout layout);
}
