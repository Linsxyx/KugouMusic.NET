using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

public interface ISystemMediaSessionService : IDisposable
{
    bool IsSupported { get; }
    void Initialize(Window mainWindow, PlayerViewModel playerViewModel);
    Task UpdateSongAsync(SongItem? song);
    void UpdatePlaybackState(bool isPlaying);
    void UpdateTimeline(double positionSeconds, double durationSeconds);
    void Shutdown();
}
