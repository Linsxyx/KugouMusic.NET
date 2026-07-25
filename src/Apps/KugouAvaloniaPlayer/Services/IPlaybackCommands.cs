using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackCommands
{
    Task PlayAsync(
        SongItem song,
        IReadOnlyList<SongItem>? context = null,
        CancellationToken cancellationToken = default);

    void TogglePlayPause();

    Task PlayPreviousAsync(
        bool preservePlaybackState = false,
        CancellationToken cancellationToken = default);

    Task PlayNextAsync(
        bool preservePlaybackState = false,
        CancellationToken cancellationToken = default);

    void Stop();

    void AddToNext(SongItem song);

    void AddToQueue(IReadOnlyList<SongItem> songs);

    Task ReplaceQueueAsync(
        IReadOnlyList<SongItem> songs,
        SongItem? startSong = null,
        CancellationToken cancellationToken = default);
}
