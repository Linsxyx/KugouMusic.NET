#if !KUGOU_WINDOWS
namespace KugouAvaloniaPlayer.Services;

public sealed class TaskbarLyricsService : ITaskbarLyricsService
{
    public bool IsSupported => false;
    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }

    public void Refresh()
    {
    }

    public void Dispose()
    {
    }
}
#endif
