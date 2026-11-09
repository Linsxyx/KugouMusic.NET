using System.Globalization;
using KuGou.Net.Abstractions.Models;
using KaMusic.Cli.Models;

namespace KaMusic.Cli.Services;

internal sealed class TerminalDataService(TerminalKugouClients clients, TerminalLocalMusicService? localMusic = null)
{
    private static readonly (int CardId, string Title, string Badge)[] TopCardDefinitions =
    [
        (1, "私人专属好歌", "个性推荐"),
        (2, "经典怀旧金曲", "岁月留声"),
        (3, "热门好歌精选", "全网热播"),
        (4, "小众宝藏佳作", "独立宝藏"),
        (5, "潮流尝鲜", "前沿新声"),
        (6, "VIP 专属推荐", "臻享曲库")
    ];

    public TerminalLocalMusicService LocalMusic { get; } = localMusic ?? new TerminalLocalMusicService();
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

    // 并行加载探索发现页顶部的 6 张官方推荐歌曲卡片。
    public async Task<IReadOnlyList<TerminalExploreCard>> GetTopCardsAsync()
    {
        var tasks = TopCardDefinitions.Select(async def =>
        {
            try
            {
                var response = await clients.Recommend.GetTopCardAsync(def.CardId);
                if (response?.Songs == null || response.Songs.Count == 0)
                {
                    return null;
                }

                var songs = response.Songs.Select(s => MapTopCardSong(s, def.Title)).ToList();
                var previews = songs
                    .Take(3)
                    .Select((s, idx) => $"{idx + 1}. {s.Name} - {s.Singer}")
                    .ToList();
                var cover = !string.IsNullOrWhiteSpace(response.Cover)
                    ? response.Cover
                    : songs.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Cover))?.Cover;
                var subtitle = !string.IsNullOrWhiteSpace(response.RecommendDescription)
                    ? response.RecommendDescription
                    : songs.Count > 0 ? $"{songs[0].Name} · {songs[0].Singer}" : $"{songs.Count} 首推荐好歌";

                var playlist = new TerminalPlaylistItem
                {
                    Id = $"topcard:{def.CardId}",
                    ListId = def.CardId.ToString(CultureInfo.InvariantCulture),
                    Name = def.Title,
                    Subtitle = $"{def.Badge} · {songs.Count} 首",
                    Cover = cover,
                    Count = songs.Count,
                    Kind = TerminalPlaylistKind.TopCard,
                    Previews = previews,
                    PreloadedSongs = songs
                };

                return new TerminalExploreCard
                {
                    Id = playlist.Id,
                    Title = def.Title,
                    Subtitle = subtitle,
                    Badge = $"{def.Badge} · {songs.Count}首",
                    SectionTitle = "官方推荐卡",
                    Cover = cover,
                    Kind = TerminalExploreCardKind.TopCard,
                    PreviewLines = previews,
                    Playlist = playlist,
                    PreloadedSongs = songs
                };
            }
            catch
            {
                return null;
            }
        });

        var loaded = await Task.WhenAll(tasks);
        return loaded.Where(x => x != null).Select(x => x!).ToList();
    }

    // 加载探索发现页的推荐歌单卡片（支持按分类 tagId 筛选）。
    public async Task<IReadOnlyList<TerminalExploreCard>> GetDiscoverPlaylistCardsAsync(int categoryId = 0, string categoryName = "推荐歌单")
    {
        var response = await clients.Recommend.GetRecommendedPlaylistsAsync(categoryId, page: 1, pageSize: 30);
        if (response?.Playlists == null)
        {
            return [];
        }

        return response.Playlists.Select(x =>
        {
            var playText = x.PlayCount > 0 ? $"{FormatCount(x.PlayCount)}播放" : categoryName;
            var creator = string.IsNullOrWhiteSpace(x.CreatorName) ? "酷狗歌单" : x.CreatorName;
            var playlist = new TerminalPlaylistItem
            {
                Id = x.GlobalId,
                ListId = x.ListId.ToString(CultureInfo.InvariantCulture),
                Name = x.Name,
                Subtitle = $"{creator} · {playText}",
                Cover = x.Cover,
                Count = 0,
                Kind = TerminalPlaylistKind.Discover
            };

            var previews = new List<string>
            {
                $"创建者：{creator}",
                $"热  度：{playText}"
            };

            return new TerminalExploreCard
            {
                Id = $"playlist:{x.GlobalId}",
                Title = x.Name,
                Subtitle = creator,
                Badge = playText,
                SectionTitle = categoryName,
                Cover = x.Cover,
                Kind = TerminalExploreCardKind.Playlist,
                PreviewLines = previews,
                Playlist = playlist
            };
        }).Where(c => !string.IsNullOrWhiteSpace(c.Playlist?.Id)).ToList();
    }

    // 加载歌单分类标签列表，供探索发现页歌单广场切换主题。
    public async Task<IReadOnlyList<TerminalPlaylistTagItem>> GetPlaylistTagsAsync()
    {
        var list = new List<TerminalPlaylistTagItem>
        {
            new() { Id = 0, CategoryId = 0, Name = "全部推荐", GroupName = "精选" }
        };

        try
        {
            var groups = await clients.Playlist.GetTagsAsync();
            if (groups == null)
            {
                return list;
            }

            foreach (var group in groups)
            {
                var groupName = string.IsNullOrWhiteSpace(group.TagName) ? "分类" : group.TagName;
                foreach (var child in group.Children.OrderBy(c => c.Sort).Take(8))
                {
                    if (child.TagId <= 0 || string.IsNullOrWhiteSpace(child.TagName))
                    {
                        continue;
                    }

                    list.Add(new TerminalPlaylistTagItem
                    {
                        Id = child.TagId,
                        CategoryId = child.TagId,
                        Name = child.TagName,
                        GroupName = groupName
                    });
                }
            }
        }
        catch
        {
            // 标签加载失败时保留默认"全部推荐"。
        }

        return list;
    }

    // 并行加载推荐榜单与完整分类榜单，合并前三首预览曲目与榜单分类信息。
    public async Task<(IReadOnlyList<TerminalExploreCard> FeaturedRanks, IReadOnlyList<TerminalExploreCard> AllRanks)> GetRankExploreCardsAsync()
    {
        var recommendedTask = clients.Rank.GetRecommendedRanksAsync();
        var allRanksTask = clients.Rank.GetAllRanksAsync();
        await Task.WhenAll(recommendedTask, allRanksTask);

        var recommended = recommendedTask.Result?.Info ?? [];
        var allRanks = allRanksTask.Result?.Info ?? [];

        var rankById = allRanks
            .Where(r => r.FileId > 0)
            .GroupBy(r => r.FileId)
            .ToDictionary(g => g.Key, g => g.First());

        var featuredCards = recommended
            .Select(r => rankById.GetValueOrDefault(r.FileId, r))
            .Where(r => r.FileId > 0 && !string.IsNullOrWhiteSpace(r.Name))
            .Take(6)
            .Select(r => MapRankExploreCard(r, "榜单精选"))
            .ToList();

        var allCards = allRanks
            .Where(r => r.FileId > 0 && !string.IsNullOrWhiteSpace(r.Name))
            .OrderBy(r => GetRankGroupOrder(r.Classify))
            .Select(r => MapRankExploreCard(r, GetRankGroupTitle(r.Classify)))
            .ToList();

        if (featuredCards.Count == 0 && allCards.Count > 0)
        {
            featuredCards = allCards.Take(6).ToList();
        }

        return (featuredCards, allCards);
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
            Subtitle = GetRankGroupTitle(x.Classify),
            Cover = x.Cover,
            Kind = TerminalPlaylistKind.Rank
        }).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TerminalSongItem>> GetPlaylistSongsAsync(TerminalPlaylistItem playlist, int page = 1)
    {
        if (playlist.Kind == TerminalPlaylistKind.TopCard && playlist.PreloadedSongs.Count > 0)
        {
            return playlist.PreloadedSongs;
        }

        if (playlist.Kind == TerminalPlaylistKind.Local)
        {
            return await LocalMusic.GetPlaylistTracksAsync(playlist.LocalPlaylistId, playlist.Name);
        }

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
        var profile = await GetUserProfileAsync();
        return profile.Name;
    }

    public async Task<TerminalUserProfile> GetUserProfileAsync()
    {
        var session = clients.SessionManager.Session;
        if (!clients.User.IsLoggedIn())
        {
            return new TerminalUserProfile { IsLoggedIn = false, Name = "未登录" };
        }

        try
        {
            var userTask = clients.User.GetUserInfoAsync();
            var vipTask = clients.User.GetVipInfoAsync();
            await Task.WhenAll(userTask, vipTask);
            var user = await userTask;
            var vip = await vipTask;
            var name = string.IsNullOrWhiteSpace(user?.Name) ? $"用户 {session.UserId}" : user.Name;
            var vipLabel = vip switch
            {
                { IsSuperVip: true } => "豪华 VIP",
                { IsConceptVip: true } => "概念 VIP",
                { IsVip: 1 } => "VIP 会员",
                _ => session.VipType is "0" or "" ? "普通用户" : $"VIP ({session.VipType})"
            };

            return new TerminalUserProfile
            {
                IsLoggedIn = true,
                UserId = session.UserId,
                Name = name,
                AvatarUrl = user?.Pic,
                VipType = vipLabel
            };
        }
        catch
        {
            return new TerminalUserProfile
            {
                IsLoggedIn = true,
                UserId = session.UserId,
                Name = $"用户 {session.UserId}",
                VipType = session.VipType
            };
        }
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
            MixSongId = song.MixSongId.ToString(),
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

    private static TerminalSongItem MapTopCardSong(TopCardSong item, string cardTitle)
    {
        var singers = item.Singers
            .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name)
            .ToList();
        var singerText = singers.Count > 0
            ? string.Join("、", singers)
            : string.IsNullOrWhiteSpace(item.SingerName) ? "未知歌手" : item.SingerName;

        return new TerminalSongItem
        {
            Name = NormalizeName(item.Name),
            Singer = singerText,
            AlbumName = item.AlbumName,
            Hash = item.Hash,
            AlbumId = item.AlbumId.ToString(CultureInfo.InvariantCulture),
            MixSongId = item.MixSongId,
            AudioId = item.AudioId,
            Cover = item.SizableCover,
            DurationSeconds = item.Duration,
            Source = cardTitle
        };
    }

    private static TerminalExploreCard MapRankExploreCard(RankListItem item, string sectionTitle)
    {
        var firstSong = item.SongPreviews.FirstOrDefault();
        var cover = !string.IsNullOrWhiteSpace(firstSong?.TransParam?.UnionCover)
            ? firstSong.TransParam.UnionCover
            : item.Cover;

        var previews = item.SongPreviews
            .Select(s => string.IsNullOrWhiteSpace(s.Name) ? s.SongName : s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(3)
            .Select((name, idx) => $"{idx + 1}. {name}")
            .ToList();

        var badge = previews.Count > 0 ? $"TOP {previews.Count}" : sectionTitle;
        var playlist = new TerminalPlaylistItem
        {
            Id = item.FileId.ToString(CultureInfo.InvariantCulture),
            RankId = item.FileId,
            Name = item.Name,
            Subtitle = $"{sectionTitle} · {badge}",
            Cover = cover,
            Kind = TerminalPlaylistKind.Rank,
            Previews = previews
        };

        return new TerminalExploreCard
        {
            Id = $"rank:{item.FileId}",
            Title = item.Name,
            Subtitle = sectionTitle,
            Badge = badge,
            SectionTitle = sectionTitle,
            Cover = cover,
            Kind = TerminalExploreCardKind.Rank,
            PreviewLines = previews.Count > 0 ? previews : [$"分类：{sectionTitle}", "点击查看完整榜单曲目"],
            Playlist = playlist
        };
    }

    private static int GetRankGroupOrder(long classify) => classify switch
    {
        1 => 0,
        2 => 1,
        5 => 2,
        3 => 3,
        4 => 4,
        _ => 5
    };

    private static string GetRankGroupTitle(long classify) => classify switch
    {
        1 => "星耀榜",
        2 => "地区榜",
        5 => "特色榜",
        3 => "全球榜",
        4 => "曲风榜",
        _ => "其他榜单"
    };

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
