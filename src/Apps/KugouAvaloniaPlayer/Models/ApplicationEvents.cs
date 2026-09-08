namespace KugouAvaloniaPlayer.Models;

public sealed record AuthStateChangedEvent(bool IsLoggedIn);

public sealed record PlaylistCollectionChangedEvent(
    PlaylistChangeKind Kind,
    string? PlaylistId = null);

public sealed record SongLocateRequest(long LocalTrackId, long Sequence);

/// <summary>
///     请求跳转到搜索页并执行搜索（用于歌曲右键"搜索"等入口）。
/// </summary>
/// <param name="Keyword">搜索关键词。</param>
/// <param name="Type">搜索分类，null 表示保持当前分类。</param>
public sealed record SearchRequestedEvent(string Keyword, SearchType? Type = null);

public enum PlaylistChangeKind
{
    Created,
    Deleted,
    Renamed,
    SongsChanged,
    FullRefreshRequired
}
