using System.Collections.Generic;
using ZLinq;
using KuGou.Net.Abstractions.Models;

namespace KugouAvaloniaPlayer.Services;

public static class UserPlaylistDisplayHelper
{
    public static IEnumerable<UserPlaylistItem> OrderForDisplay(List<UserPlaylistItem> playlists)
    {
        if (playlists.Count <= 2)
            return playlists;

        return playlists.AsValueEnumerable().Take(2).Concat(playlists.AsValueEnumerable().Skip(2).Reverse()).ToArray();
    }
}
