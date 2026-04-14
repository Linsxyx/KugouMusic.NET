using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Templates;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

public class FavoritePlaylistService(
    UserClient userClient,
    PlaylistClient playlistClient,
    KgSessionManager sessionManager,
    ISukiToastManager toastManager,
    ISukiDialogManager dialogManager,
    ILogger<FavoritePlaylistService> logger)
{
    private const string LikeListIdForAction = "2";
    private const int CacheSchemaVersion = 2;

    private readonly Dictionary<string, int> _hashToFileId = new();
    private readonly SemaphoreSlim _likeCacheLoadLock = new(1, 1);
    private readonly HashSet<string> _likedHashes = new();

    private LikeCacheFileModel? _latestCache;
    private int _likeCacheLoadAttemptCount;
    private bool _hasLoggedFirstLikeCacheSuccess;
    private bool _loadedFromLocalCache;

    public async Task LoadLikeListAsync()
    {
        var attempt = Interlocked.Increment(ref _likeCacheLoadAttemptCount);
        var isFirstAttempt = attempt == 1;
        if (isFirstAttempt)
            logger.LogInformation("开始首次加载“我喜欢”缓存。");

        await _likeCacheLoadLock.WaitAsync();
        try
        {
            // 本地优先：先让红心和列表可用，不阻塞后续远端刷新。
            if (!_loadedFromLocalCache && TryLoadLikeCacheFromDisk(out var localCache))
            {
                ApplyCacheToMemory(localCache!, source: "local");
                _loadedFromLocalCache = true;
                if (isFirstAttempt)
                {
                    _hasLoggedFirstLikeCacheSuccess = true;
                    logger.LogInformation(
                        "我喜欢缓存本地命中秒开: source=local cache_hit=true songs={SongCount} hashes={HashCount} fileIds={FileIdCount} updatedAt={UpdatedAt}",
                        localCache!.Items.Count,
                        _likedHashes.Count,
                        _hashToFileId.Count,
                        localCache.UpdatedAt);
                }
            }

            var playlists = await userClient.GetPlaylistsAsync();
            if (playlists is null || playlists.Status != 1)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=playlist_list_error remote_err_code={ErrorCode}",
                    _latestCache != null,
                    playlists?.ErrorCode);
                return;
            }

            if (playlists.Playlists.Count < 1)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=no_playlists", _latestCache != null);
                return;
            }

            var likePlaylist = ResolveLikePlaylist(playlists.Playlists);
            if (likePlaylist == null || string.IsNullOrWhiteSpace(likePlaylist.ListCreateId))
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=like_playlist_not_found", _latestCache != null);
                return;
            }

            var data = await playlistClient.GetSongsAsync(likePlaylist.ListCreateId, pageSize: 1000);
            if (data is null)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=response_null", _latestCache != null);
                return;
            }

            if (data.Status != 1)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=remote_error remote_err_code={ErrorCode} status={Status}",
                    _latestCache != null,
                    data.ErrorCode,
                    data.Status);
                return;
            }

            var songs = data.Songs ?? new List<PlaylistSong>();
            var remoteCache = BuildCacheModelFromRemote(likePlaylist, songs);
            ApplyCacheToMemory(remoteCache, source: "remote");
            SaveLikeCacheToDisk(remoteCache);

            if (isFirstAttempt || !_hasLoggedFirstLikeCacheSuccess)
            {
                _hasLoggedFirstLikeCacheSuccess = true;
                logger.LogInformation("我喜欢远端刷新成功: source=remote songs={SongCount} hashes={HashCount} fileIds={FileIdCount}",
                    songs.Count, _likedHashes.Count, _hashToFileId.Count);
            }
            else
            {
                logger.LogDebug("我喜欢远端刷新成功: source=remote songs={SongCount} hashes={HashCount} fileIds={FileIdCount}",
                    songs.Count, _likedHashes.Count, _hashToFileId.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载我喜欢缓存异常。");
        }
        finally
        {
            _likeCacheLoadLock.Release();
        }
    }

    public bool TryGetLikePlaylistCache(out LikePlaylistCacheSnapshot snapshot)
    {
        if (_latestCache != null)
        {
            snapshot = ToSnapshot(_latestCache);
            return snapshot.Songs.Count > 0;
        }

        if (TryLoadLikeCacheFromDisk(out var diskCache))
        {
            ApplyCacheToMemory(diskCache!, source: "local");
            snapshot = ToSnapshot(diskCache!);
            return snapshot.Songs.Count > 0;
        }

        snapshot = new LikePlaylistCacheSnapshot();
        return false;
    }

    public bool IsLiked(string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return false;

        lock (_likedHashes)
        {
            return _likedHashes.Contains(hash.ToLowerInvariant());
        }
    }

    public async Task<bool> ToggleLikeAsync(SongItem song, bool currentIsLiked)
    {
        var hash = song.Hash.ToLowerInvariant();
        try
        {
            if (currentIsLiked)
            {
                if (_hashToFileId.TryGetValue(hash, out var fileId))
                {
                    var result = await playlistClient.RemoveSongsAsync(LikeListIdForAction, new List<long> { fileId });
                    if (result?.Status == 1)
                    {
                        lock (_likedHashes)
                        {
                            _likedHashes.Remove(hash);
                            _hashToFileId.Remove(hash);
                        }

                        PersistCurrentLikeCacheSnapshot();
                        return false;
                    }
                }
                else
                {
                    await LoadLikeListAsync();
                }
            }
            else
            {
                var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
                {
                    (song.Name, song.Hash, song.AlbumId, "0")
                };
                var result = await playlistClient.AddSongsAsync(LikeListIdForAction, songList);
                if (result?.Status == 1)
                {
                    lock (_likedHashes)
                    {
                        _likedHashes.Add(hash);
                    }

                    UpsertSongInCache(song);
                    PersistCurrentLikeCacheSnapshot();

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        await LoadLikeListAsync();
                    });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("操作收藏失败: {Message}", ex.Message);
        }

        return currentIsLiked;
    }

    public async Task ShowAddToPlaylistDialogAsync(SongItem song)
    {
        var playlists = await userClient.GetPlaylistsAsync();
        if (playlists is not null && playlists.Status == 1)
        {
            var onlinePlaylists = playlists.Playlists.Where(p => !string.IsNullOrEmpty(p.ListCreateId)).ToList();

            if (onlinePlaylists.Count == 0)
            {
                toastManager.CreateToast().OfType(NotificationType.Warning).WithTitle("提示").WithContent("请先创建歌单")
                    .Dismiss().ByClicking()
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
                return;
            }

            var listBox = new ListBox
            {
                Width = 300, MaxHeight = 400, ItemsSource = onlinePlaylists, SelectionMode = SelectionMode.Single,
                ItemTemplate = new FuncDataTemplate<UserPlaylistItem>((item, _) => new TextBlock { Text = item.Name })
            };

            dialogManager.CreateDialog()
                .WithTitle("添加到歌单")
                .WithContent(listBox)
                .WithActionButton("取消", _ => { }, true, "Standard")
                .WithActionButton("添加", _ => HandleAddToPlaylistClick(song, listBox), true, "Standard")
                .TryShow();
        }
        else
        {
            logger.LogError("获取歌单列表失败 err_code{ErrorCode}", playlists?.ErrorCode);
        }
    }

    private void UpsertSongInCache(SongItem song)
    {
        var cache = EnsureCacheForCurrentUser();
        var existing = cache.Items.FirstOrDefault(x => string.Equals(x.Hash, song.Hash, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.FileId = song.FileId == 0 ? existing.FileId : (int)song.FileId;
            existing.Name = string.IsNullOrWhiteSpace(song.Name) ? existing.Name : song.Name;
            existing.Singer = string.IsNullOrWhiteSpace(song.Singer) ? existing.Singer : song.Singer;
            existing.AlbumId = string.IsNullOrWhiteSpace(song.AlbumId) ? existing.AlbumId : song.AlbumId;
            existing.Cover = string.IsNullOrWhiteSpace(song.Cover) ? existing.Cover : song.Cover;
            existing.DurationSeconds = song.DurationSeconds > 0 ? song.DurationSeconds : existing.DurationSeconds;
            existing.Singers = song.Singers?.ToList() ?? existing.Singers;
            return;
        }

        cache.Items.Add(new LikeSongCacheItem
        {
            Hash = song.Hash,
            FileId = (int)song.FileId,
            Name = song.Name,
            Singer = song.Singer,
            AlbumId = song.AlbumId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            Singers = song.Singers?.ToList() ?? new List<SingerLite>()
        });
    }

    private void PersistCurrentLikeCacheSnapshot()
    {
        var cache = EnsureCacheForCurrentUser();
        cache.UpdatedAt = DateTimeOffset.Now.ToString("O");

        lock (_likedHashes)
        {
            var index = cache.Items.ToDictionary(x => x.Hash.ToLowerInvariant(), x => x);
            foreach (var hash in _likedHashes)
            {
                if (!index.ContainsKey(hash))
                    cache.Items.Add(new LikeSongCacheItem
                    {
                        Hash = hash,
                        FileId = _hashToFileId.GetValueOrDefault(hash)
                    });
            }

            cache.Items = cache.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Hash) && _likedHashes.Contains(x.Hash.ToLowerInvariant()))
                .ToList();
        }

        _latestCache = cache;
        SaveLikeCacheToDisk(cache);
    }

    private async Task AddSongToPlaylistInnerAsync(SongItem song, string playlistId, string playlistName)
    {
        try
        {
            var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
                { (song.Name, song.Hash, song.AlbumId, "0") };

            var result = await playlistClient.AddSongsAsync(playlistId, songList);

            if (result?.Status == 1)
            {
                if (playlistId == LikeListIdForAction)
                {
                    lock (_likedHashes)
                    {
                        _likedHashes.Add(song.Hash.ToLowerInvariant());
                    }

                    UpsertSongInCache(song);
                    PersistCurrentLikeCacheSnapshot();

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        await LoadLikeListAsync();
                    });
                }

                toastManager.CreateToast()
                    .OfType(NotificationType.Success).WithTitle("添加成功")
                    .WithContent($"已添加到「{playlistName}」")
                    .Dismiss().ByClicking()
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
            }
            else
            {
                toastManager.CreateToast().OfType(NotificationType.Error).WithTitle("添加失败")
                    .Dismiss().ByClicking()
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "添加歌曲到歌单失败");
        }
    }

    private async Task AddSongToPlaylistSafelyAsync(SongItem song, UserPlaylistItem selectedPlaylist)
    {
        try
        {
            await AddSongToPlaylistInnerAsync(song, selectedPlaylist.ListId.ToString(), selectedPlaylist.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "添加歌曲到歌单失败");
        }
    }

    private void HandleAddToPlaylistClick(SongItem song, ListBox listBox)
    {
        if (listBox.SelectedItem is UserPlaylistItem selectedPlaylist)
            _ = AddSongToPlaylistSafelyAsync(song, selectedPlaylist);
    }

    private LikeCacheFileModel BuildCacheModelFromRemote(UserPlaylistItem likePlaylist, List<PlaylistSong> songs)
    {
        return new LikeCacheFileModel
        {
            SchemaVersion = CacheSchemaVersion,
            UserId = GetCurrentUserId(),
            UpdatedAt = DateTimeOffset.Now.ToString("O"),
            Source = "remote",
            PlaylistName = likePlaylist.Name,
            PlaylistListId = likePlaylist.ListId,
            PlaylistIsDefault = likePlaylist.IsDefault,
            PlaylistCreateId = likePlaylist.ListCreateId,
            PlaylistCount = likePlaylist.Count,
            Items = songs.Where(s => !string.IsNullOrWhiteSpace(s.Hash))
                .Select(s => new LikeSongCacheItem
                {
                    Hash = s.Hash,
                    FileId = s.FileId,
                    Name = s.Name,
                    Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                    Singers = s.Singers,
                    AlbumId = s.AlbumId,
                    Cover = s.Cover,
                    DurationSeconds = s.DurationMs / 1000.0,
                    Privilege = s.Privilege
                })
                .ToList()
        };
    }

    private void ApplyCacheToMemory(LikeCacheFileModel cache, string source)
    {
        lock (_likedHashes)
        {
            _likedHashes.Clear();
            _hashToFileId.Clear();

            foreach (var item in cache.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Hash))
                    continue;

                var normalized = item.Hash.ToLowerInvariant();
                _likedHashes.Add(normalized);
                if (item.FileId != 0)
                    _hashToFileId[normalized] = item.FileId;
            }
        }

        cache.Source = source;
        _latestCache = cache;
    }

    private LikeCacheFileModel EnsureCacheForCurrentUser()
    {
        if (_latestCache != null)
            return _latestCache;

        if (TryLoadLikeCacheFromDisk(out var cache))
        {
            _latestCache = cache;
            return cache!;
        }

        return _latestCache = new LikeCacheFileModel
        {
            SchemaVersion = CacheSchemaVersion,
            UserId = GetCurrentUserId(),
            UpdatedAt = DateTimeOffset.Now.ToString("O"),
            Source = "local",
            PlaylistName = "我喜欢",
            PlaylistListId = 2,
            PlaylistIsDefault = 2,
            PlaylistCreateId = "",
            PlaylistCount = 0,
            Items = new List<LikeSongCacheItem>()
        };
    }

    private LikePlaylistCacheSnapshot ToSnapshot(LikeCacheFileModel cache)
    {
        var playlist = new PlaylistItem
        {
            Name = string.IsNullOrWhiteSpace(cache.PlaylistName) ? "我喜欢" : cache.PlaylistName,
            Id = cache.PlaylistCreateId ?? "",
            ListId = cache.PlaylistListId == 0 ? 2 : cache.PlaylistListId,
            Count = cache.PlaylistCount > 0 ? cache.PlaylistCount : cache.Items.Count,
            Type = Models.PlaylistType.Online,
            Cover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg"
        };

        var songs = cache.Items.Where(x => !string.IsNullOrWhiteSpace(x.Hash)).Select(x => new SongItem
        {
            Name = string.IsNullOrWhiteSpace(x.Name) ? "未知" : x.Name,
            Singer = string.IsNullOrWhiteSpace(x.Singer) ? "未知" : x.Singer,
            Hash = x.Hash,
            AlbumId = x.AlbumId ?? "",
            FileId = x.FileId,
            Singers = x.Singers ?? new List<SingerLite>(),
            Cover = string.IsNullOrWhiteSpace(x.Cover)
                ? "avares://KugouAvaloniaPlayer/Assets/default_song.png"
                : x.Cover,
            DurationSeconds = x.DurationSeconds > 0 ? x.DurationSeconds : 0
        }).ToList();

        return new LikePlaylistCacheSnapshot
        {
            Playlist = playlist,
            Songs = songs,
            UpdatedAt = cache.UpdatedAt,
            Source = cache.Source,
            IsCompactCache = cache.Items.Any(x => string.IsNullOrWhiteSpace(x.Name)),
            UserId = cache.UserId
        };
    }

    private bool TryLoadLikeCacheFromDisk(out LikeCacheFileModel? cache)
    {
        cache = null;
        try
        {
            var filePath = GetLikeCacheFilePath();
            if (!File.Exists(filePath))
                return TryLoadLegacyCacheFile(out cache);

            var json = File.ReadAllText(filePath);
            var model = JsonSerializer.Deserialize(json, LikeCacheJsonContext.Default.LikeCacheFileModel);
            if (model?.Items == null || model.Items.Count == 0)
                return false;

            cache = NormalizeCacheModel(model, "local");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取本地“我喜欢”缓存失败。 path={Path}", GetLikeCacheFilePath());
            return false;
        }
    }

    private bool TryLoadLegacyCacheFile(out LikeCacheFileModel? cache)
    {
        cache = null;
        try
        {
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "kugou",
                "favorite_like_cache.json");
            if (!File.Exists(legacyPath))
                return false;

            var json = File.ReadAllText(legacyPath);
            var legacy = JsonSerializer.Deserialize(json, LikeCacheJsonContext.Default.LikeCacheFileModel);
            if (legacy?.Items == null || legacy.Items.Count == 0)
                return false;

            cache = NormalizeCacheModel(legacy, "local");
            logger.LogInformation("已读取旧版我喜欢缓存: source=local legacy=true items={ItemCount}", cache.Items.Count);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取旧版“我喜欢”缓存失败。");
            return false;
        }
    }

    private static LikeCacheFileModel NormalizeCacheModel(LikeCacheFileModel cache, string source)
    {
        cache.SchemaVersion = cache.SchemaVersion <= 0 ? 1 : cache.SchemaVersion;
        cache.PlaylistName = string.IsNullOrWhiteSpace(cache.PlaylistName) ? "我喜欢" : cache.PlaylistName;
        cache.PlaylistListId = cache.PlaylistListId == 0 ? 2 : cache.PlaylistListId;
        cache.PlaylistIsDefault = cache.PlaylistIsDefault == 0 ? 2 : cache.PlaylistIsDefault;
        cache.Items ??= new List<LikeSongCacheItem>();
        cache.UpdatedAt = string.IsNullOrWhiteSpace(cache.UpdatedAt) ? DateTimeOffset.Now.ToString("O") : cache.UpdatedAt;
        cache.Source = source;
        return cache;
    }

    private void SaveLikeCacheToDisk(LikeCacheFileModel cache)
    {
        try
        {
            var filePath = GetLikeCacheFilePath();
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(cache, LikeCacheJsonContext.Default.LikeCacheFileModel);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "写入本地“我喜欢”缓存失败。 path={Path}", GetLikeCacheFilePath());
        }
    }

    private string GetLikeCacheFilePath()
    {
        var uid = GetCurrentUserId();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kugou",
            $"favorite_like_cache_{uid}.json");
    }

    private string GetCurrentUserId()
    {
        var uid = sessionManager.Session.UserId;
        return string.IsNullOrWhiteSpace(uid) ? "0" : uid;
    }

    private static UserPlaylistItem? ResolveLikePlaylist(List<UserPlaylistItem> playlists)
    {
        return playlists.FirstOrDefault(x => x.ListId == 2)
               ?? playlists.FirstOrDefault(x => x.IsDefault == 2)
               ?? playlists.FirstOrDefault(x => x.Name.Contains("喜欢", StringComparison.OrdinalIgnoreCase))
               ?? playlists.FirstOrDefault(x => x.Name.Contains("我喜欢", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LikePlaylistCacheSnapshot
{
    public PlaylistItem Playlist { get; set; } = new();
    public List<SongItem> Songs { get; set; } = new();
    public string UpdatedAt { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsCompactCache { get; set; }
    public string UserId { get; set; } = "";
}

public sealed class LikeCacheFileModel
{
    public int SchemaVersion { get; set; } = 1;
    public string UserId { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string Source { get; set; } = "";

    public string PlaylistName { get; set; } = "";
    public long PlaylistListId { get; set; }
    public int PlaylistIsDefault { get; set; }
    public string PlaylistCreateId { get; set; } = "";
    public int PlaylistCount { get; set; }

    public List<LikeSongCacheItem> Items { get; set; } = new();
}

public sealed class LikeSongCacheItem
{
    public string Hash { get; set; } = "";
    public int FileId { get; set; }

    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public List<SingerLite> Singers { get; set; } = new();
    public string AlbumId { get; set; } = "";
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public int Privilege { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(LikeCacheFileModel))]
internal partial class LikeCacheJsonContext : JsonSerializerContext
{
}
