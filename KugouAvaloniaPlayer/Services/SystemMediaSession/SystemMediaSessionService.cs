#if !KUGOU_WINDOWS
using System.Threading.Tasks;
using Avalonia.Controls;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public sealed class SystemMediaSessionService : ISystemMediaSessionService
{
    public bool IsSupported => false;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
    }

    public Task UpdateSongAsync(SongItem? song)
    {
        return Task.CompletedTask;
    }

    public void UpdatePlaybackState(bool isPlaying)
    {
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
        Shutdown();
    }
}
#endif
