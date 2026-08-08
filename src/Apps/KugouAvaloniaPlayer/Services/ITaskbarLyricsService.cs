using System;

namespace KugouAvaloniaPlayer.Services;

public interface ITaskbarLyricsService : IDisposable
{
    bool IsSupported { get; }
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
    void Refresh();
}
