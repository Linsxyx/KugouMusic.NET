using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.Messaging;
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
    UserCreatedPlaylistCacheService userCreatedPlaylistCacheService,
    ISukiToastManager toastManager,
    ISukiDialogManager dialogManager,
    IUiDispatcherService uiDispatcher,
    ILogger<FavoritePlaylistService> logger)
{
    private const string LikeListIdForAction = "2";
    private const int CacheSchemaVersion = 2;
    private const string StoreScope = "favorite_like_cache";
    private const int AddToPlaylistDialogPageSize = 100;
    private const int MaxUserPlaylistPages = 200;
    private static readonly TimeSpan LoadPlaylistDialogTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AddSongToPlaylistTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan AddSongsToMultiplePlaylistsTimeout = TimeSpan.FromSeconds(20);
    private readonly Dictionary<string, int> _hashToFileId = new();
    private readonly SemaphoreSlim _addToPlaylistDialogLock = new(1, 1);
    private readonly SemaphoreSlim _likeCacheLoadLock = new(1, 1);
    private readonly HashSet<string> _likedHashes = new();
    private bool _hasLoggedFirstLikeCacheSuccess;
    private string _likeListIdForAction = LikeListIdForAction;

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
            if (likePlaylist == null)
            {
                logger.LogWarning(
                    "我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=like_playlist_not_found",
                    _latestCache != null);
                return;
            }

            _likeListIdForAction = likePlaylist.ListId.ToString();

            if (string.IsNullOrWhiteSpace(likePlaylist.ListCreateId))
            {
                logger.LogWarning(
                    "我喜欢远端刷新失败: source=remote cache_hit={CacheHit} fallback_reason=like_playlist_missing_create_id",
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
                    var result = await playlistClient.RemoveSongsAsync(_likeListIdForAction, new List<long> { fileId });
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
                var result = await playlistClient.AddSongsAsync(_likeListIdForAction, songList);
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
                        try
                        {
                            await Task.Delay(3000);
                            await LoadLikeListAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "收藏成功后刷新喜欢列表失败");
                        }
                    });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "操作收藏失败");
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

        try
        {
            var onlinePlaylists = await EnsureOnlinePlaylistsLoadedAsync();

            if (onlinePlaylists.Count == 0)
            {
                ShowToast(NotificationType.Warning, "提示", "请先创建歌单");
                return;
            }

            var dialogViewModel = new AddToPlaylistDialogViewModel(
                song.Name,
                song.Singer,
                song.Cover,
                onlinePlaylists.AsValueEnumerable().Select(AddToPlaylistDialogViewModel.ToPlaylistDialogItem).ToArray(),
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

    public Task ShowSongBatchActionDialogAsync(IReadOnlyList<SongItem> songs, bool allowAddToPlaylist = true)
    {
        if (songs.Count == 0)
        {
            ShowToast(NotificationType.Warning, "没有可操作的歌曲", "当前详情页还没有已加载的歌曲。");
            return Task.CompletedTask;
        }

        var dialogViewModel = new SongBatchActionDialogViewModel(
            songs,
            allowAddToPlaylist,
            selectedSongs =>
            {
                DismissDialog();
                WeakReferenceMessenger.Default.Send(new AddLoadedSongsToQueueMessage(selectedSongs.AsValueEnumerable().ToList()));
                return Task.CompletedTask;
            },
            async selectedSongs =>
            {
                DismissDialog();
                await ShowMultiPlaylistSelectionDialogAsync(selectedSongs);
            },
            selectedSongs =>
            {
                DismissDialog();
                WeakReferenceMessenger.Default.Send(new ReplacePlaybackQueueMessage(
                    selectedSongs.AsValueEnumerable().ToList(),
                    selectedSongs[0]));
                return Task.CompletedTask;
            },
            DismissDialog);

        ShowDialog(new SongBatchActionDialog
        {
            DataContext = dialogViewModel
        });

        return Task.CompletedTask;
    }

    private async Task ShowMultiPlaylistSelectionDialogAsync(IReadOnlyList<SongItem> songs)
    {
        if (!await _addToPlaylistDialogLock.WaitAsync(0))
        {
            ShowToast(NotificationType.Information, "请稍候", "歌单列表正在加载中...");
            return;
        }

        try
        {
            var onlinePlaylists = await EnsureOnlinePlaylistsLoadedAsync();
            if (onlinePlaylists.Count == 0)
            {
                ShowToast(NotificationType.Warning, "提示", "请先创建歌单");
                return;
            }

            var dialogViewModel = new MultiPlaylistSelectionDialogViewModel(
                songs.Count,
                onlinePlaylists.AsValueEnumerable().Select(AddToPlaylistDialogViewModel.ToPlaylistDialogItem).ToArray(),
                async selectedPlaylists =>
                {
                    DismissDialog();
                    await AddSongsToMultiplePlaylistsInnerAsync(songs, selectedPlaylists);
                },
                DismissDialog);

            ShowDialog(new MultiPlaylistSelectionDialog
            {
                DataContext = dialogViewModel
            });
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

    private async Task<IReadOnlyList<UserPlaylistItem>> EnsureOnlinePlaylistsLoadedAsync()
    {
        var onlinePlaylists = userCreatedPlaylistCacheService.GetSnapshot();
        if (onlinePlaylists.Count > 0)
            return onlinePlaylists;

        ShowProgressDialog("加载歌单", "正在获取你的歌单列表...");

        try
        {
            var playlists = await WaitWithTimeoutAsync(
                LoadAllCreatedPlaylistsAsync(),
                LoadPlaylistDialogTimeout,
                "加载歌单超时，请检查网络后重试。");

            if (playlists is null)
            {
                logger.LogError("获取歌单列表失败");
                ShowToast(NotificationType.Error, "加载失败", "歌单列表获取失败，请稍后再试。");
                return [];
            }

            onlinePlaylists = playlists;
            userCreatedPlaylistCacheService.Update(onlinePlaylists);
            return onlinePlaylists;
        }
        finally
        {
            DismissDialog();
        }
    }

    private async Task<IReadOnlyList<UserPlaylistItem>?> LoadAllCreatedPlaylistsAsync()
    {
        var allPlaylists = new List<UserPlaylistItem>();

        for (var page = 1; page <= MaxUserPlaylistPages; page++)
        {
            var response = await userClient.GetPlaylistsAsync(page, AddToPlaylistDialogPageSize);
            if (response is null)
                return null;

            if (response.Status != 1)
            {
                logger.LogError("获取歌单列表失败 err_code={ErrorCode}, page={Page}", response.ErrorCode, page);
                return null;
            }

            if (response.Playlists.Count == 0)
                break;

            allPlaylists.AddRange(response.Playlists.AsValueEnumerable()
                .Where(p => !string.IsNullOrEmpty(p.ListCreateId) && p.Type == 0)
                .ToArray());

            if (response.ListCount > 0 && allPlaylists.Count >= response.ListCount)
                break;

            if (response.Playlists.Count < AddToPlaylistDialogPageSize)
                break;
        }

        return UserPlaylistDisplayHelper.OrderForDisplay(allPlaylists).AsValueEnumerable()
            .Where(item => !string.IsNullOrEmpty(item.ListCreateId) && item.Type == 0)
            .ToArray();
    }

    private async Task AddSongsToMultiplePlaylistsInnerAsync(
        IReadOnlyList<SongItem> songs,
        IReadOnlyList<PlaylistDialogPlaylistItemViewModel> selectedPlaylists)
    {
        if (songs.Count == 0 || selectedPlaylists.Count == 0)
            return;

        var songPayload = BuildSongPayload(songs);
        if (songPayload.Count == 0)
        {
            ShowToast(NotificationType.Warning, "没有可添加的歌曲", "选中的歌曲缺少必要的在线标识。");
            return;
        }

        var successPlaylists = new List<string>();
        var failedPlaylists = new List<string>();
        var likePlaylistUpdated = false;

        try
        {
            ShowProgressDialog("正在添加", $"准备将 {songPayload.Count} 首歌曲加入 {selectedPlaylists.Count} 个歌单...");

            foreach (var playlist in selectedPlaylists)
            {
                var result = await WaitWithTimeoutAsync(
                    playlistClient.AddSongsAsync(playlist.ListId, songPayload),
                    AddSongsToMultiplePlaylistsTimeout,
                    $"添加到「{playlist.Name}」超时，请稍后重试。");

                if (result?.Status == 1)
                {
                    successPlaylists.Add(playlist.Name);
                    if (playlist.ListId == _likeListIdForAction)
                    {
                        UpdateLikeCacheAfterBatchAdd(songs);
                        likePlaylistUpdated = true;
                    }
                }
                else
                {
                    failedPlaylists.Add(playlist.Name);
                }
            }
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "批量添加歌曲到歌单超时");
            ShowToast(NotificationType.Error, "添加超时", ex.Message);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "批量添加歌曲到歌单失败");
            ShowToast(NotificationType.Error, "添加失败", ex.Message);
            return;
        }
        finally
        {
            DismissDialog();
        }

        if (likePlaylistUpdated)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000);
                    await LoadLikeListAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "批量添加到喜欢歌单后刷新喜欢列表失败");
                }
            });
        }

        if (successPlaylists.Count == selectedPlaylists.Count)
        {
            ShowToast(NotificationType.Success, "添加成功", $"已添加到 {successPlaylists.Count} 个歌单");
            return;
        }

        if (successPlaylists.Count > 0)
        {
            ShowToast(
                NotificationType.Warning,
                "部分成功",
                $"成功 {successPlaylists.Count} 个，失败 {failedPlaylists.Count} 个");
            return;
        }

        ShowToast(NotificationType.Error, "添加失败", $"未能添加到 {failedPlaylists.Count} 个歌单");
    }

    private void UpsertSongInCache(SongItem song)
    {
        var cache = EnsureCacheForCurrentUser();
        var existing =
            cache.Items.AsValueEnumerable().FirstOrDefault(x => string.Equals(x.Hash, song.Hash, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.FileId = song.FileId == 0 ? existing.FileId : (int)song.FileId;
            existing.Name = string.IsNullOrWhiteSpace(song.Name) ? existing.Name : song.Name;
            existing.Singer = string.IsNullOrWhiteSpace(song.Singer) ? existing.Singer : song.Singer;
            existing.AlbumId = string.IsNullOrWhiteSpace(song.AlbumId) ? existing.AlbumId : song.AlbumId;
            existing.Cover = string.IsNullOrWhiteSpace(song.Cover) ? existing.Cover : song.Cover;
            existing.DurationSeconds = song.DurationSeconds > 0 ? song.DurationSeconds : existing.DurationSeconds;
            existing.Singers = song.Singers?.AsValueEnumerable().ToList() ?? existing.Singers;
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
            Singers = song.Singers?.AsValueEnumerable().ToList() ?? new List<SingerLite>()
        });
    }

    private void PersistCurrentLikeCacheSnapshot()
    {
        var cache = EnsureCacheForCurrentUser();
        cache.UpdatedAt = DateTimeOffset.Now.ToString("O");

        lock (_likedHashes)
        {
            var index = cache.Items.AsValueEnumerable().ToDictionary(x => x.Hash.ToLowerInvariant(), x => x);
            foreach (var hash in _likedHashes)
                if (!index.ContainsKey(hash))
                    cache.Items.Add(new LikeSongCacheItem
                    {
                        Hash = hash,
                        FileId = _hashToFileId.GetValueOrDefault(hash)
                    });

            cache.Items = cache.Items
                .AsValueEnumerable().Where(x => !string.IsNullOrWhiteSpace(x.Hash) && _likedHashes.Contains(x.Hash.ToLowerInvariant()))
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
                if (playlistId == _likeListIdForAction)
                {
                    UpdateLikeCacheAfterBatchAdd([song]);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(3000);
                            await LoadLikeListAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "添加到喜欢歌单后刷新喜欢列表失败");
                        }
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

    private List<(string Name, string Hash, string AlbumId, string MixSongId)> BuildSongPayload(IReadOnlyList<SongItem> songs)
    {
        return songs.AsValueEnumerable()
            .Where(song => !string.IsNullOrWhiteSpace(song.Hash))
            .Select(song => (song.Name, song.Hash, song.AlbumId, "0"))
            .ToList();
    }

    private void UpdateLikeCacheAfterBatchAdd(IEnumerable<SongItem> songs)
    {
        var changed = false;

        lock (_likedHashes)
        {
            foreach (var song in songs)
            {
                if (string.IsNullOrWhiteSpace(song.Hash))
                    continue;

                _likedHashes.Add(song.Hash.ToLowerInvariant());
                changed = true;
            }
        }

        if (!changed)
            return;

        foreach (var song in songs)
            UpsertSongInCache(song);

        PersistCurrentLikeCacheSnapshot();
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
            Items = songs.AsValueEnumerable().Where(s => !string.IsNullOrWhiteSpace(s.Hash))
                .Select(s => new LikeSongCacheItem
                {
                    Hash = s.Hash,
                    FileId = s.FileId,
                    Name = s.Name,
                    Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.AsValueEnumerable().Select(x => x.Name).ToArray()) : "未知",
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

        if (cache.PlaylistListId > 0)
            _likeListIdForAction = cache.PlaylistListId.ToString();

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

        var songs = cache.Items.AsValueEnumerable().Where(x => !string.IsNullOrWhiteSpace(x.Hash)).Select(x => new SongItem
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
            IsCompactCache = cache.Items.AsValueEnumerable().Any(x => string.IsNullOrWhiteSpace(x.Name)),
            UserId = cache.UserId
        };
    }

    private bool TryLoadLikeCacheFromDisk(out LikeCacheFileModel? cache)
    {
        cache = null;
        try
        {
            var json = AppSqliteStore.LoadValue(StoreScope, GetCurrentUserId());
            if (string.IsNullOrWhiteSpace(json))
            {
                var filePath = GetLikeCacheFilePath();
                if (!File.Exists(filePath))
                    return TryLoadLegacyCacheFile(out cache);

                json = File.ReadAllText(filePath);
                AppSqliteStore.SaveValue(StoreScope, GetCurrentUserId(), json);
                AppSqliteStore.DeleteFileIfExists(filePath);
            }

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
            AppSqliteStore.SaveValue(
                StoreScope,
                GetCurrentUserId(),
                JsonSerializer.Serialize(cache, LikeCacheJsonContext.Default.LikeCacheFileModel));
            AppSqliteStore.DeleteFileIfExists(legacyPath);
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
            var json = JsonSerializer.Serialize(cache, LikeCacheJsonContext.Default.LikeCacheFileModel);
            AppSqliteStore.SaveValue(StoreScope, GetCurrentUserId(), json);
            AppSqliteStore.DeleteFileIfExists(GetLikeCacheFilePath());
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
        return playlists.AsValueEnumerable().FirstOrDefault(x => string.Equals(x.Name, "我喜欢", StringComparison.OrdinalIgnoreCase))
               ?? playlists.AsValueEnumerable().FirstOrDefault(x => x.Name.Contains("我喜欢", StringComparison.OrdinalIgnoreCase))
               ?? playlists.AsValueEnumerable().FirstOrDefault(x => x.IsDefault == 2)
               ?? playlists.AsValueEnumerable().FirstOrDefault(x => x.ListId == 2)
               ?? playlists.AsValueEnumerable().FirstOrDefault(x => x.Name.Contains("喜欢", StringComparison.OrdinalIgnoreCase))
               ;
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

    private void ShowDialog(Control content)
    {
        void Show()
        {
            dialogManager.CreateDialog()
                .WithContent(content)
                .TryShow();
        }

        uiDispatcher.RunOrPost(Show);
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

        uiDispatcher.RunOrPost(Dismiss);
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
