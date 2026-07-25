namespace KugouAvaloniaPlayer.Models;

public sealed record AuthStateChangedEvent(bool IsLoggedIn);

public sealed record PlaylistCollectionChangedEvent(
    PlaylistChangeKind Kind,
    string? PlaylistId = null);

public enum PlaylistChangeKind
{
    Created,
    Deleted,
    Renamed,
    SongsChanged,
    FullRefreshRequired
}
