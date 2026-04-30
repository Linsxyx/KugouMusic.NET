using KuGou.Net.Abstractions.Models;
using KgTest.Models;

namespace KgTest.Services;

internal sealed class TerminalDataService(TerminalKugouClients clients)
{
    public async Task<IReadOnlyList<TerminalSongItem>> SearchSongsAsync(string keyword)
    {
        var songs = await clients.Search.SearchAsync(keyword);
        return songs.Select(MapSongInfo).ToList();
    }

    public async Task<IReadOnlyList<TerminalPlaylistItem>> SearchPlaylistsAsync(string keyword)
    {
        var playlists = await clients.Search.SearchSpecialAsync(keyword);
        return playlists?.Select(x => new TerminalPlaylistItem
        {
            Id = string.IsNullOrWhiteSpace(x.GlobalId) ? x.ListId.ToString() : x.GlobalId,
            ListId = x.ListId.ToString(),
            Name = x.Name,
            Subtitle = $"{x.CreatorName} · {FormatCount(x.PlayCount)}播放",
            Cover = x.Cover,
            Count = 0,
            Kind = TerminalPlaylistKind.Discover
        }).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TerminalSongItem>> GetDailySongsAsync()
    {
        var response = await clients.Recommend.GetRecommendedSongsAsync();
        return response?.Songs.Select(MapDailySong).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TerminalSongItem>> GetPersonalFmSongsAsync()
    {
        var response = await clients.Recommend.GetPersonalRecommendFMAsync(mode: "normal", songPoolId: 0);
        return response?.Songs.Select(MapPersonalFmSong).ToList() ?? [];
    }

    public async Task ReportPersonalFmAsync(TerminalSongItem? song, int playtimeSeconds, bool finished, string action = "play")
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Hash))
        {
            return;
        }

        try
        {
            await clients.Recommend.GetPersonalRecommendFMAsync(
                hash: song.Hash,
                songid: song.AudioId > 0 ? song.AudioId.ToString() : null,
                playtime: playtimeSeconds,
                action: action,
                mode: "normal",
                songPoolId: 0,
                isOverplay: finished,
                remainSongCount: 0);
        }
        catch
        {
            // FM feedback should not interrupt playback.
        }
    }

    public async Task<IReadOnlyList<TerminalPlaylistItem>> GetDiscoverPlaylistsAsync(int categoryId = 0)
    {
        var response = await clients.Recommend.GetRecommendedPlaylistsAsync(categoryId);
        return response?.Playlists.Select(x => new TerminalPlaylistItem
        {
            Id = x.GlobalId,
            ListId = x.ListId.ToString(),
            Name = x.Name,
            Subtitle = $"{x.CreatorName} · {FormatCount(x.PlayCount)}播放",
            Cover = x.Cover,
            Count = 0,
            Kind = TerminalPlaylistKind.Discover
        }).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TerminalPlaylistItem>> GetMyPlaylistsAsync()
    {
        var response = await clients.User.GetPlaylistsAsync(pageSize: 60);
        if (response?.Playlists == null)
        {
            return [];
        }

        return response.Playlists.Select(x => new TerminalPlaylistItem
        {
            Id = x.IsCollectedAlbum ? x.AlbumId.ToString() : x.ListCreateId,
            ListId = x.ListId.ToString(),
            Name = x.Name,
            Subtitle = x.IsDefault switch
            {
                1 => "默认收藏",
                2 => "我喜欢",
                _ => string.IsNullOrWhiteSpace(x.ListCreateUsername) ? "我的歌单" : x.ListCreateUsername
            },
            Cover = x.Pic,
            Count = x.Count,
            Kind = x.IsCollectedAlbum ? TerminalPlaylistKind.Album : TerminalPlaylistKind.Online
        }).Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
    }

    public async Task<IReadOnlyList<TerminalPlaylistItem>> GetRanksAsync()
    {
        var response = await clients.Rank.GetAllRanksAsync();
        return response?.Info.Select(x => new TerminalPlaylistItem
        {
            Id = x.FileId.ToString(),
            RankId = x.FileId,
            Name = x.Name,
            Subtitle = "排行榜",
            Cover = x.Cover,
            Kind = TerminalPlaylistKind.Rank
        }).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TerminalSongItem>> GetPlaylistSongsAsync(TerminalPlaylistItem playlist, int page = 1)
    {
        if (playlist.Kind == TerminalPlaylistKind.Rank)
        {
            var rank = await clients.Rank.GetRankSongsAsync((int)playlist.RankId, page, 100);
            return rank?.RankSongLists.Select(MapRankSong).ToList() ?? [];
        }

        if (playlist.Kind == TerminalPlaylistKind.Album)
        {
            var albumSongs = await clients.Album.GetSongsAsync(playlist.Id, page, 100);
            return albumSongs?.Select(MapAlbumSong).ToList() ?? [];
        }

        var response = await clients.Playlist.GetSongsAsync(playlist.Id, page, 100);
        return response?.Songs.Select(MapPlaylistSong).ToList() ?? [];
    }

    public async Task<string> GetUserDisplayNameAsync()
    {
        if (!clients.User.IsLoggedIn())
        {
            return "未登录";
        }

        var user = await clients.User.GetUserInfoAsync();
        return string.IsNullOrWhiteSpace(user?.Name) ? $"用户 {clients.SessionManager.Session.UserId}" : user.Name;
    }

    private static TerminalSongItem MapSongInfo(SongInfo song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = string.IsNullOrWhiteSpace(song.Singer) ? JoinSingers(song.Singers) : song.Singer,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            Cover = song.Cover,
            DurationSeconds = song.Duration,
            Source = "搜索"
        };
    }

    private static TerminalSongItem MapDailySong(DailyRecommendSong song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.SingerName,
            Hash = PreferQualityHash(song.Hash, song.Hash320, song.HashFlac, song.HashHiRes),
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId,
            AudioId = song.AudioId,
            Cover = song.GetCoverUrl(),
            DurationSeconds = song.Duration,
            Source = "每日推荐",
            Privilege = song.Privilege
        };
    }

    private static TerminalSongItem MapPersonalFmSong(PersonalFmSong song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.SingerName,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId,
            AudioId = song.AudioId,
            Cover = song.TransParam?.UnionCover,
            DurationSeconds = song.DurationSeconds,
            Source = "私人 FM",
            Privilege = song.Privilege
        };
    }

    private static TerminalSongItem MapPlaylistSong(PlaylistSong song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.Singers.Count > 0 ? JoinSingers(song.Singers) : GuessSinger(song.Name),
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            Cover = song.Cover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "歌单",
            Privilege = song.Privilege
        };
    }

    private static TerminalSongItem MapRankSong(RankSongItem song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.Singers.Count > 0 ? JoinSingers(song.Singers) : "未知歌手",
            Hash = song.Hash,
            AlbumId = song.AlbumId.ToString(),
            Cover = song.TransParam?.UnionCover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "排行榜"
        };
    }

    private static TerminalSongItem MapAlbumSong(AlbumSongItem song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = string.IsNullOrWhiteSpace(song.Singer) ? JoinSingers(song.Singers) : song.Singer,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            Cover = song.Cover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "专辑"
        };
    }

    private static string PreferQualityHash(params string[] hashes)
    {
        return hashes.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    }

    private static string JoinSingers(IEnumerable<SingerLite> singers)
    {
        var text = string.Join("、", singers.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(text) ? "未知歌手" : text;
    }

    private static string NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "未知歌曲" : name.Trim();
    }

    private static string GuessSinger(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "未知歌手";
        }

        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? name[..dash].Trim() : "未知歌手";
    }

    private static string FormatCount(long count)
    {
        if (count >= 100_000_000)
        {
            return $"{count / 100_000_000.0:0.#}亿";
        }

        return count >= 10_000 ? $"{count / 10_000.0:0.#}万" : count.ToString();
    }
}
