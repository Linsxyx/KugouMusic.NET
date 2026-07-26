namespace KugouAvaloniaPlayer.Models;

public sealed record AuthStateChangedEvent(bool IsLoggedIn);

public sealed record PlaylistCollectionChangedEvent(
    PlaylistChangeKind Kind,
    string? PlaylistId = null);

public sealed record SongLocateRequest(long LocalTrackId, long Sequence);

public enum PlaylistChangeKind
{
    Created,
    Deleted,
    Renamed,
    SongsChanged,
    FullRefreshRequired
}
