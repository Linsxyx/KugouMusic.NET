#if KUGOU_ANDROID
using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

// Desktop lyrics need floating windows; Android would need an overlay view instead.
public sealed class DesktopLyricMousePassthroughService : IDesktopLyricMousePassthroughService
{
    public bool IsSupported => false;
    public bool SupportsSelectiveHitTesting => false;

    public void Apply(Window window, DesktopLyricHitTestLayout layout)
    {
    }
}
#endif
