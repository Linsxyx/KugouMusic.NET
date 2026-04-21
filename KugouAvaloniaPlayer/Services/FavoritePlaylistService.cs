using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

public partial class FavoritePlaylistService(
    UserClient userClient,
    PlaylistClient playlistClient,
    KgSessionManager sessionManager,
    ISukiToastManager toastManager,
    ISukiDialogManager dialogManager,
    ILogger<FavoritePlaylistService> logger)
{
    private const string LikeListIdForAction = "2";
    private const int CacheSchemaVersion = 2;
    private static readonly TimeSpan LoadPlaylistDialogTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AddSongToPlaylistTimeout = TimeSpan.FromSeconds(12);
    private const string DefaultPlaylistCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string LikePlaylistCover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg";

    private readonly Dictionary<string, int> _hashToFileId = new();
    private readonly SemaphoreSlim _addToPlaylistDialogLock = new(1, 1);
    private readonly SemaphoreSlim _likeCacheLoadLock = new(1, 1);
    private readonly HashSet<string> _likedHashes = new();
    private bool _hasLoggedFirstLikeCacheSuccess;

    private LikeCacheFileModel? _latestCache;
    private int _likeCacheLoadAttemptCount;
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
                ApplyCacheToMemory(localCache!, "local");
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
                logger.LogWarning(
                    "我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=playlist_list_error remote_err_code={ErrorCode}",
                    _latestCache != null,
                    playlists?.ErrorCode);
                return;
            }

            if (playlists.Playlists.Count < 1)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=no_playlists",
                    _latestCache != null);
                return;
            }

            var likePlaylist = ResolveLikePlaylist(playlists.Playlists);
            if (likePlaylist == null || string.IsNullOrWhiteSpace(likePlaylist.ListCreateId))
            {
                logger.LogWarning(
                    "我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=like_playlist_not_found",
                    _latestCache != null);
                return;
            }

            var data = await playlistClient.GetSongsAsync(likePlaylist.ListCreateId, pageSize: 1000);
            if (data is null)
            {
                logger.LogWarning("我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=response_null",
                    _latestCache != null);
                return;
            }

            if (data.Status != 1)
            {
                logger.LogWarning(
                    "我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=remote_error remote_err_code={ErrorCode} status={Status}",
                    _latestCache != null,
                    data.ErrorCode,
                    data.Status);
                return;
            }

            var songs = data.Songs ?? new List<PlaylistSong>();
            var remoteCache = BuildCacheModelFromRemote(likePlaylist, songs);
            ApplyCacheToMemory(remoteCache, "remote");
            SaveLikeCacheToDisk(remoteCache);

            if (isFirstAttempt || !_hasLoggedFirstLikeCacheSuccess)
            {
                _hasLoggedFirstLikeCacheSuccess = true;
                logger.LogInformation(
                    "我喜欢远端刷新成功: source=remote songs={SongCount} hashes={HashCount} fileIds={FileIdCount}",
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
            ApplyCacheToMemory(diskCache!, "local");
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
        if (!await _addToPlaylistDialogLock.WaitAsync(0))
        {
            ShowToast(NotificationType.Information, "请稍候", "歌单列表正在加载中...");
            return;
        }

        ShowProgressDialog("加载歌单", "正在获取你的歌单列表...");

        try
        {
            var playlists = await WaitWithTimeoutAsync(
                userClient.GetPlaylistsAsync(),
                LoadPlaylistDialogTimeout,
                "加载歌单超时，请检查网络后重试。");

            DismissDialog();

            if (playlists is null || playlists.Status != 1)
            {
                logger.LogError("获取歌单列表失败 err_code{ErrorCode}", playlists?.ErrorCode);
                ShowToast(NotificationType.Error, "加载失败", "歌单列表获取失败，请稍后再试。");
                return;
            }

            var onlinePlaylists = playlists.Playlists.Where(p => !string.IsNullOrEmpty(p.ListCreateId)).ToList();
            if (onlinePlaylists.Count == 0)
            {
                ShowToast(NotificationType.Warning, "提示", "请先创建歌单");
                return;
            }

            var dialogViewModel = new AddToPlaylistDialogViewModel(
                song.Name,
                song.Singer,
                song.Cover,
                onlinePlaylists.Select(ToPlaylistDialogItem),
                async selectedPlaylist =>
                {
                    DismissDialog();
                    await AddSongToPlaylistInnerAsync(song, selectedPlaylist.ListId, selectedPlaylist.Name);
                },
                DismissDialog);

            var dialogView = new AddToPlaylistDialog
            {
                DataContext = dialogViewModel
            };

            ShowDialog(dialogView);
        }
        catch (TimeoutException ex)
        {
            DismissDialog();
            logger.LogWarning(ex, "获取歌单列表超时");
            ShowToast(NotificationType.Error, "加载超时", ex.Message);
        }
        catch (Exception ex)
        {
            DismissDialog();
            logger.LogError(ex, "获取歌单列表失败");
            ShowToast(NotificationType.Error, "加载失败", ex.Message);
        }
        finally
        {
            _addToPlaylistDialogLock.Release();
        }
    }

    private void UpsertSongInCache(SongItem song)
    {
        var cache = EnsureCacheForCurrentUser();
        var existing =
            cache.Items.FirstOrDefault(x => string.Equals(x.Hash, song.Hash, StringComparison.OrdinalIgnoreCase));
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
                if (!index.ContainsKey(hash))
                    cache.Items.Add(new LikeSongCacheItem
                    {
                        Hash = hash,
                        FileId = _hashToFileId.GetValueOrDefault(hash)
                    });

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
            ShowProgressDialog("正在添加", $"准备将「{song.Name}」加入「{playlistName}」...");

            var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
                { (song.Name, song.Hash, song.AlbumId, "0") };

            var result = await WaitWithTimeoutAsync(
                playlistClient.AddSongsAsync(playlistId, songList),
                AddSongToPlaylistTimeout,
                "添加歌曲超时，请检查网络后重试。");

            DismissDialog();

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

                ShowToast(NotificationType.Success, "添加成功", $"已添加到「{playlistName}」");
            }
            else
            {
                ShowToast(NotificationType.Error, "添加失败", $"未能添加到「{playlistName}」");
            }
        }
        catch (TimeoutException ex)
        {
            DismissDialog();
            logger.LogWarning(ex, "添加歌曲到歌单超时");
            ShowToast(NotificationType.Error, "添加超时", ex.Message);
        }
        catch (Exception ex)
        {
            DismissDialog();
            logger.LogError(ex, "添加歌曲到歌单失败");
            ShowToast(NotificationType.Error, "添加失败", ex.Message);
        }
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
            Type = PlaylistType.Online,
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
        cache.UpdatedAt = string.IsNullOrWhiteSpace(cache.UpdatedAt)
            ? DateTimeOffset.Now.ToString("O")
            : cache.UpdatedAt;
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

partial class FavoritePlaylistService
{
    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, string message)
    {
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(message, ex);
        }
    }

    private PlaylistDialogPlaylistItemViewModel ToPlaylistDialogItem(UserPlaylistItem item)
    {
        return new PlaylistDialogPlaylistItemViewModel
        {
            Name = item.Name,
            ListId = item.ListId.ToString(),
            SongCount = item.Count,
            IsLikedPlaylist = item.ListId == 2,
            Cover = string.IsNullOrWhiteSpace(item.Pic)
                ? item.ListId == 2 ? LikePlaylistCover : DefaultPlaylistCover
                : item.Pic
        };
    }

    private void ShowDialog(Control content)
    {
        void Show()
        {
            dialogManager.CreateDialog()
                .WithContent(content)
                .TryShow();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
    }

    private void ShowProgressDialog(string title, string message)
    {
        var content = new Border
        {
            Padding = new Thickness(20, 18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new SukiUI.Controls.Loading
                            {
                                Width = 22,
                                Height = 22
                            },
                            new TextBlock
                            {
                                Text = message,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            }
                        }
                    }
                }
            }
        };

        ShowDialog(content);
    }

    private void DismissDialog()
    {
        void Dismiss()
        {
            dialogManager.DismissDialog();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Dismiss();
        else
            Dispatcher.UIThread.Post(Dismiss);
    }

    private void ShowToast(NotificationType type, string title, string content)
    {
        toastManager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss().ByClicking()
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
    }
}
