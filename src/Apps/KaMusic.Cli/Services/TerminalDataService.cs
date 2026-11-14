using System.Globalization;
using KuGou.Net.Abstractions.Models;
using KaMusic.Cli.Models;

namespace KaMusic.Cli.Services;

internal sealed class TerminalDataService(TerminalKugouClients clients, TerminalLocalMusicService? localMusic = null)
{
    private readonly HashSet<string> _likedHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _likeHashToFileId = new(StringComparer.OrdinalIgnoreCase);
    private string _likeListId = "2";
    private string _likeListCreateId = "";
    private bool _likeListLoaded;
    private readonly SemaphoreSlim _likeLock = new(1, 1);

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

    // 检查指定 Hash 是否已在“我喜欢”歌单中。
    public bool IsLiked(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return false;
        lock (_likedHashes)
        {
            return _likedHashes.Contains(hash.Trim());
        }
    }

    // 异步加载用户的“我喜欢”歌单曲目，构建本地 Hash 与 FileId 映射表。
    public async Task EnsureLikeListLoadedAsync(bool force = false)
    {
        if (!clients.User.IsLoggedIn())
            return;

        if (_likeListLoaded && !force)
            return;

        await _likeLock.WaitAsync();
        try
        {
            if (_likeListLoaded && !force)
                return;

            var playlists = await clients.User.GetPlaylistsAsync(pageSize: 60);
            if (playlists?.Playlists == null || playlists.Playlists.Count == 0)
                return;

            var likePlaylist = playlists.Playlists.FirstOrDefault(p => p.IsDefault == 2)
                ?? playlists.Playlists.FirstOrDefault(p => p.IsDefault == 1)
                ?? playlists.Playlists.FirstOrDefault(p => p.Name == "我喜欢")
                ?? playlists.Playlists.FirstOrDefault();

            if (likePlaylist == null)
                return;

            _likeListId = likePlaylist.ListId.ToString();
            _likeListCreateId = likePlaylist.ListCreateId;

            var likedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var likeHashToFileId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var total = 0;
            for (var page = 1; page <= 200; page++)
            {
                var songsResponse = await clients.Playlist.GetSongsAsync(_likeListCreateId, page, PlaylistSongPageSize);
                var pageSongs = songsResponse?.Songs;
                if (pageSongs == null || pageSongs.Count == 0)
                {
                    break;
                }

                foreach (var s in pageSongs)
                {
                    if (string.IsNullOrWhiteSpace(s.Hash))
                    {
                        continue;
                    }

                    var h = s.Hash.Trim();
                    likedHashes.Add(h);
                    if (s.FileId > 0)
                    {
                        likeHashToFileId[h] = s.FileId;
                    }
                }

                if (songsResponse!.Count > 0)
                {
                    total = songsResponse.Count;
                }

                // “我喜欢”可能有上千首，必须翻到底，否则后续歌曲的喜欢状态会全部显示为未收藏。
                if (!TerminalPlaylistPaging.HasMoreSongs(page, PlaylistSongPageSize, pageSongs.Count, total))
                {
                    break;
                }
            }

            lock (_likedHashes)
            {
                _likedHashes.Clear();
                _likeHashToFileId.Clear();
                foreach (var hash in likedHashes)
                {
                    _likedHashes.Add(hash);
                }

                foreach (var pair in likeHashToFileId)
                {
                    _likeHashToFileId[pair.Key] = pair.Value;
                }
            }

            _likeListLoaded = true;
        }
        catch
        {
            // Ignore background like list loading error
        }
        finally
        {
            _likeLock.Release();
        }
    }

    // 获取用户自建及默认在线歌单（用于添加到歌单弹窗）。
    public async Task<IReadOnlyList<TerminalPlaylistItem>> GetUserCreatedPlaylistsAsync()
    {
        if (!clients.User.IsLoggedIn())
            return [];

        try
        {
            await EnsureLikeListLoadedAsync();
            var response = await clients.User.GetPlaylistsAsync(pageSize: 100);
            if (response?.Playlists == null)
                return [];

            return response.Playlists
                .Where(p => !p.IsCollectedAlbum && p.Type == 0)
                .OrderBy(p => p.IsDefault == 2 ? 0 : 1)
                .Select(p => new TerminalPlaylistItem
                {
                    Id = p.ListCreateId,
                    ListId = p.ListId.ToString(),
                    Name = p.IsDefault == 2 ? "我喜欢" : p.Name,
                    Subtitle = p.IsDefault switch
                    {
                        1 => "默认收藏",
                        2 => "我喜欢",
                        _ => $"{p.Count} 首歌曲"
                    },
                    Count = p.Count,
                    Kind = TerminalPlaylistKind.Online
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // 将单曲添加到指定的自建/在线歌单中，若目标为「我喜欢」则同步刷新红心收藏状态。
    public async Task<(bool Success, string Message)> AddSongToPlaylistAsync(TerminalSongItem song, TerminalPlaylistItem playlist)
    {
        if (!clients.User.IsLoggedIn())
        {
            return (false, "请先登录酷狗账号（按 F5 扫码登录 / F6 短信登录）");
        }

        if (string.IsNullOrWhiteSpace(song.Hash))
        {
            return (false, "该歌曲没有在线音频标识，无法添加到歌单");
        }

        var normalizedHash = song.Hash.Trim();
        var isLikePlaylist = playlist.ListId == _likeListId || playlist.Name == "我喜欢" || playlist.Subtitle == "我喜欢";

        try
        {
            var mixSongId = string.IsNullOrWhiteSpace(song.MixSongId) ? "0" : song.MixSongId;
            var addResp = await clients.Playlist.AddSongsAsync(playlist.ListId, [(song.Name, song.Hash, song.AlbumId ?? "", mixSongId)]);
            if (addResp?.Status == 1)
            {
                if (isLikePlaylist)
                {
                    lock (_likedHashes)
                    {
                        _likedHashes.Add(normalizedHash);
                    }
                    song.IsFavorite = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2000);
                            await EnsureLikeListLoadedAsync(force: true);
                        }
                        catch
                        {
                            // 忽略后台刷新异常
                        }
                    });
                }

                return (true, $"已将《{song.Name}》添加到歌单「{playlist.Name}」");
            }

            var errorText = addResp?.GetExtraString("error") ?? addResp?.GetExtraString("msg");
            var errMsg = !string.IsNullOrWhiteSpace(errorText)
                ? errorText
                : (addResp?.ErrorCode > 0 ? $"添加到歌单失败 (错误码 {addResp.ErrorCode})" : "添加到歌单失败");
            return (false, errMsg);
        }
        catch (Exception ex)
        {
            return (false, $"添加到歌单失败：{ex.Message}");
        }
    }

    public async Task<(bool Success, bool IsLiked, string Message)> ToggleFavoriteAsync(TerminalSongItem song)
    {
        if (!clients.User.IsLoggedIn())
        {
            return (false, false, "请先登录酷狗账号（按 F5 扫码登录 / F6 短信登录）");
        }

        if (string.IsNullOrWhiteSpace(song.Hash))
        {
            return (false, false, "该歌曲没有在线音频标识，无法添加到收藏");
        }

        var normalizedHash = song.Hash.Trim();
        await EnsureLikeListLoadedAsync();

        bool isCurrentlyLiked;
        long fileId = 0;
        lock (_likedHashes)
        {
            isCurrentlyLiked = _likedHashes.Contains(normalizedHash);
            _likeHashToFileId.TryGetValue(normalizedHash, out fileId);
        }

        try
        {
            if (isCurrentlyLiked)
            {
                if (fileId > 0)
                {
                    var removeResp = await clients.Playlist.RemoveSongsAsync(_likeListId, [fileId]);
                    if (removeResp?.Status == 1)
                    {
                        lock (_likedHashes)
                        {
                            _likedHashes.Remove(normalizedHash);
                            _likeHashToFileId.Remove(normalizedHash);
                        }
                        song.IsFavorite = false;
                        return (true, false, $"已从「我喜欢」移除：《{song.Name}》");
                    }
                }
                else
                {
                    await EnsureLikeListLoadedAsync(force: true);
                    lock (_likedHashes)
                    {
                        _likeHashToFileId.TryGetValue(normalizedHash, out fileId);
                    }
                    if (fileId > 0)
                    {
                        var removeResp = await clients.Playlist.RemoveSongsAsync(_likeListId, [fileId]);
                        if (removeResp?.Status == 1)
                        {
                            lock (_likedHashes)
                            {
                                _likedHashes.Remove(normalizedHash);
                                _likeHashToFileId.Remove(normalizedHash);
                            }
                            song.IsFavorite = false;
                            return (true, false, $"已从「我喜欢」移除：《{song.Name}》");
                        }
                    }
                }

                return (false, true, "移除收藏失败，请稍后重试");
            }
            else
            {
                var mixSongId = string.IsNullOrWhiteSpace(song.MixSongId) ? "0" : song.MixSongId;
                var addResp = await clients.Playlist.AddSongsAsync(_likeListId, [(song.Name, song.Hash, song.AlbumId ?? "", mixSongId)]);
                if (addResp?.Status == 1)
                {
                    lock (_likedHashes)
                    {
                        _likedHashes.Add(normalizedHash);
                    }
                    song.IsFavorite = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2000);
                            await EnsureLikeListLoadedAsync(force: true);
                        }
                        catch
                        {
                            // Ignore refresh error
                        }
                    });
                    return (true, true, $"已添加到「我喜欢」：《{song.Name}》");
                }

                return (false, false, "添加到收藏失败");
            }
        }
        catch (Exception ex)
        {
            return (false, isCurrentlyLiked, $"收藏操作失败：{ex.Message}");
        }
    }

    public async Task<TerminalSingerDetail?> GetSingerDetailAsync(long authorId, string? fallbackName = null)
    {
        if (authorId <= 0)
            return null;

        var name = fallbackName ?? "歌手";
        string? avatar = null;
        try
        {
            var detailResp = await clients.Artist.GetDetailAsync(authorId.ToString());
            if (detailResp != null && !string.IsNullOrWhiteSpace(detailResp.Name))
            {
                name = detailResp.Name;
                avatar = detailResp.Cover;
            }
        }
        catch
        {
            // Ignore detail fetch error
        }

        return new TerminalSingerDetail
        {
            AuthorId = authorId,
            Name = name,
            Avatar = avatar
        };
    }

    public async Task<IReadOnlyList<TerminalSongItem>> GetSingerSongsAsync(long authorId, string sort = "hot", int page = 1, int pageSize = 100)
    {
        if (authorId <= 0)
            return [];

        try
        {
            var resp = await clients.Artist.GetAudiosAsync(authorId.ToString(), page, pageSize, sort);
            if (resp?.Songs == null)
                return [];

            return resp.Songs
                .Where(s => !string.IsNullOrWhiteSpace(s.Hash))
                .Select(MapSingerSong)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<TerminalSingerAlbumItem>> GetSingerAlbumsAsync(long authorId, int page = 1, int pageSize = 50)
    {
        if (authorId <= 0)
            return [];

        try
        {
            var resp = await clients.Artist.GetAlbumsAsync(authorId.ToString(), page, pageSize, "new");
            if (resp?.Albums == null)
                return [];

            return resp.Albums
                .Where(a => a.AlbumId > 0 && !string.IsNullOrWhiteSpace(a.AlbumName))
                .Select(a => new TerminalSingerAlbumItem
                {
                    AlbumId = a.AlbumId,
                    Name = a.AlbumName,
                    PublishDate = a.PublishDate,
                    Cover = string.IsNullOrWhiteSpace(a.SizableCover) ? a.Cover : a.SizableCover,
                    Subtitle = string.IsNullOrWhiteSpace(a.PublishDate) ? a.AuthorName : $"{a.PublishDate} · {a.AuthorName}"
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<SearchAuthorItem>> SearchSingersAsync(string singerName)
    {
        if (string.IsNullOrWhiteSpace(singerName))
            return [];

        try
        {
            var results = await clients.Search.SearchAuthorAsync(singerName.Trim(), pageSize: 15);
            return results ?? [];
        }
        catch
        {
            return [];
        }
    }
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

    public async Task<IReadOnlyList<TerminalSongItem>> GetPlaylistSongsAsync(TerminalPlaylistItem playlist, int page = 1) =>
        (await GetPlaylistSongsPageAsync(playlist, page)).Songs;

    // 拉取歌单的一页歌曲，并返回服务端声明的歌曲总数，调用方据此决定是否继续翻页。
    public async Task<TerminalPlaylistSongsPage> GetPlaylistSongsPageAsync(TerminalPlaylistItem playlist, int page = 1)
    {
        // 本地歌单与推荐卡片不是分页接口：一次性返回全部，第 2 页开始为空，避免重复追加。
        if (playlist.Kind == TerminalPlaylistKind.TopCard && playlist.PreloadedSongs.Count > 0)
        {
            return page == 1
                ? new TerminalPlaylistSongsPage(playlist.PreloadedSongs, playlist.PreloadedSongs.Count)
                : new TerminalPlaylistSongsPage([], 0);
        }

        if (playlist.Kind == TerminalPlaylistKind.Local)
        {
            var tracks = await LocalMusic.GetPlaylistTracksAsync(playlist.LocalPlaylistId, playlist.Name);
            return page == 1
                ? new TerminalPlaylistSongsPage(tracks, tracks.Count)
                : new TerminalPlaylistSongsPage([], 0);
        }

        if (playlist.Kind == TerminalPlaylistKind.Rank)
        {
            var rank = await clients.Rank.GetRankSongsAsync((int)playlist.RankId, page, PlaylistSongPageSize);
            var rankSongs = rank?.RankSongLists.Select(MapRankSong).ToList() ?? [];
            return new TerminalPlaylistSongsPage(rankSongs, rank?.Total ?? 0);
        }

        if (playlist.Kind == TerminalPlaylistKind.Album)
        {
            var albumSongs = await clients.Album.GetAlbumSongsPageAsync(playlist.Id, page, PlaylistSongPageSize);
            var songs = albumSongs.Songs?.Select(MapAlbumSong).ToList() ?? [];
            return new TerminalPlaylistSongsPage(songs, albumSongs.Total);
        }

        var response = await clients.Playlist.GetSongsAsync(playlist.Id, page, PlaylistSongPageSize);
        var pageSongs = response?.Songs.Select(MapPlaylistSong).ToList() ?? [];
        return new TerminalPlaylistSongsPage(pageSongs, response?.Count ?? 0);
    }

    // 歌单每页条数：服务端单页上限 300，取 300 可让 1598 首的歌单在 6 页内拉完。
    public const int PlaylistSongPageSize = 300;

    // 一次性拉取歌单全部歌曲（按歌单原始顺序），最多翻 MaxPlaylistSongPages 页，避免异常数据无限循环。
    public async Task<IReadOnlyList<TerminalSongItem>> GetAllPlaylistSongsAsync(
        TerminalPlaylistItem playlist,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const int maxPages = 200;

        var all = new List<TerminalSongItem>();
        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await GetPlaylistSongsPageAsync(playlist, page);
            if (result.Songs.Count == 0)
            {
                break;
            }

            all.AddRange(result.Songs);
            progress?.Report(all.Count);

            if (!TerminalPlaylistPaging.HasMoreSongs(page, PlaylistSongPageSize, result.Songs.Count, result.Total))
            {
                break;
            }
        }

        return all;
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

    private TerminalSongItem MapSongInfo(SongInfo song)
    {
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = string.IsNullOrWhiteSpace(song.Singer) ? JoinSingers(song.Singers) : song.Singer,
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            AlbumName = song.AlbumName,
            Cover = song.Cover,
            DurationSeconds = song.Duration,
            Source = "搜索",
            IsFavorite = IsLiked(song.Hash)
        };
    }

    private TerminalSongItem MapDailySong(DailyRecommendSong song)
    {
        var hash = PreferQualityHash(song.Hash, song.Hash320, song.HashFlac, song.HashHiRes);
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.SingerName,
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            Hash = hash,
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId.ToString(),
            AudioId = song.AudioId,
            Cover = song.GetCoverUrl(),
            DurationSeconds = song.Duration,
            Source = "每日推荐",
            Privilege = song.Privilege,
            IsFavorite = IsLiked(hash)
        };
    }

    private TerminalSongItem MapPersonalFmSong(PersonalFmSong song)
    {
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.SingerName,
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            MixSongId = song.MixSongId,
            AudioId = song.AudioId,
            Cover = song.TransParam?.UnionCover,
            DurationSeconds = song.DurationSeconds,
            Source = "私人 FM",
            Privilege = song.Privilege,
            IsFavorite = IsLiked(song.Hash)
        };
    }

    private TerminalSongItem MapPlaylistSong(PlaylistSong song)
    {
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.Singers.Count > 0 ? JoinSingers(song.Singers) : GuessSinger(song.Name),
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            FileId = song.FileId,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            Cover = song.Cover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "歌单",
            Privilege = song.Privilege,
            IsFavorite = IsLiked(song.Hash)
        };
    }

    private TerminalSongItem MapRankSong(RankSongItem song)
    {
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.Singers.Count > 0 ? JoinSingers(song.Singers) : "未知歌手",
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            Hash = song.Hash,
            AlbumId = song.AlbumId.ToString(),
            Cover = song.TransParam?.UnionCover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "排行榜",
            IsFavorite = IsLiked(song.Hash)
        };
    }

    private TerminalSongItem MapTopCardSong(TopCardSong item, string cardTitle)
    {
        var singers = item.Singers
            .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name)
            .ToList();
        var singerText = singers.Count > 0
            ? string.Join("、", singers)
            : string.IsNullOrWhiteSpace(item.SingerName) ? "未知歌手" : item.SingerName;
        var firstAuthor = item.Singers.FirstOrDefault(s => s.Id > 0);

        return new TerminalSongItem
        {
            Name = NormalizeName(item.Name),
            Singer = singerText,
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = item.Singers,
            AlbumName = item.AlbumName,
            Hash = item.Hash,
            AlbumId = item.AlbumId.ToString(CultureInfo.InvariantCulture),
            MixSongId = item.MixSongId,
            AudioId = item.AudioId,
            Cover = item.SizableCover,
            DurationSeconds = item.Duration,
            Source = cardTitle,
            IsFavorite = IsLiked(item.Hash)
        };
    }

    private TerminalSongItem MapSingerSong(SingerSongItem song)
    {
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = song.SingerName,
            Hash = song.Hash,
            AlbumId = song.AlbumId > 0 ? song.AlbumId.ToString() : "",
            AlbumName = song.AlbumName,
            Cover = song.TransParam?.UnionCover,
            DurationSeconds = song.Duration / 1000.0,
            Source = "歌手单曲",
            MixSongId = song.AlbumAudioId > 0 ? song.AlbumAudioId.ToString() : "",
            IsFavorite = IsLiked(song.Hash)
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

    private TerminalSongItem MapAlbumSong(AlbumSongItem song)
    {
        var firstAuthor = song.Singers.FirstOrDefault(s => s.Id > 0);
        return new TerminalSongItem
        {
            Name = NormalizeName(song.Name),
            Singer = string.IsNullOrWhiteSpace(song.Singer) ? JoinSingers(song.Singers) : song.Singer,
            AuthorId = firstAuthor?.Id ?? 0,
            Singers = song.Singers,
            Hash = song.Hash,
            AlbumId = song.AlbumId,
            AlbumName = song.AlbumInfo?.AlbumName ?? "",
            Cover = song.Cover,
            DurationSeconds = song.DurationMs / 1000.0,
            Source = "专辑",
            IsFavorite = IsLiked(song.Hash)
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
