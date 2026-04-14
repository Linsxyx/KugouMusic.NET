#if KUGOU_NON_WINDOWS
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services;

public sealed class DesktopLyricMousePassthroughService : IDesktopLyricMousePassthroughService
{
    public bool IsSupported => false;

    public void Apply(Window window, bool enabled)
    {
        // Non-Windows platforms are intentionally no-op for real click-through.
    }
}
#endif
