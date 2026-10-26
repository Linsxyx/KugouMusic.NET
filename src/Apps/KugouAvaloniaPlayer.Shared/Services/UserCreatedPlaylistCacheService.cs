using System.Collections.Generic;
using KuGou.Net.Abstractions.Models;

namespace KugouAvaloniaPlayer.Services;

public sealed class UserCreatedPlaylistCacheService
{
    private readonly object _gate = new();
    private List<UserPlaylistItem> _playlists = [];

    public IReadOnlyList<UserPlaylistItem> GetSnapshot()
    {
        lock (_gate)
            return _playlists.ToArray();
    }

    public void Update(IEnumerable<UserPlaylistItem> playlists)
    {
        lock (_gate)
            _playlists = new List<UserPlaylistItem>(playlists);
    }

    public void Clear()
    {
        lock (_gate)
            _playlists.Clear();
    }
}
