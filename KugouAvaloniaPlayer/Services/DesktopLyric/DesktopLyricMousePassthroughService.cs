using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public interface IDesktopLyricMousePassthroughService
{
    bool IsSupported { get; }
    void Apply(Window window, bool enabled);
}