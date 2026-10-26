#if KUGOU_ANDROID
using System.Threading.Tasks;
using Avalonia.Controls;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

// TODO(android): back this with a MediaSession + foreground playback service (lock screen, notification, BT keys).
public sealed class SystemMediaSessionService : ISystemMediaSessionService
{
    public bool IsSupported => false;

    public void Initialize(Window mainWindow, PlayerViewModel playerViewModel)
    {
    }

    public Task UpdateSongAsync(SongItem? song) => Task.CompletedTask;

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
    }
}
#endif
