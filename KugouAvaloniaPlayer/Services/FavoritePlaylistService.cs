using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Templates;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

public class FavoritePlaylistService(
    UserClient userClient,
    PlaylistClient playlistClient,
    ISukiToastManager toastManager,
    ISukiDialogManager dialogManager,
    ILogger<FavoritePlaylistService> logger)
{
    private const string LikeListIdForAction = "2";
    private readonly Dictionary<string, int> _hashToFileId = new();
    private int _likeCacheLoadAttemptCount;
    private bool _hasLoggedFirstLikeCacheSuccess;

    private readonly HashSet<string> _likedHashes = new();

    public async Task LoadLikeListAsync()
    {
        var attempt = Interlocked.Increment(ref _likeCacheLoadAttemptCount);
        var isFirstAttempt = attempt == 1;
        if (isFirstAttempt)
            logger.LogInformation("开始首次加载“我喜欢”红心缓存。");

        try
        {
            var playlists = await userClient.GetPlaylistsAsync();
            if (playlists is null || playlists.Status != 1)
            {
                logger.LogError("加载收藏列表失败: 获取歌单失败, err_code={ErrorCode}", playlists?.ErrorCode);
                if (isFirstAttempt)
                    logger.LogWarning("首次加载“我喜欢”红心缓存失败：歌单列表请求异常。");
                return;
            }

            if (playlists.Playlists.Count < 1)
            {
                logger.LogWarning("加载收藏列表失败: 用户没有任何歌单。");
                if (isFirstAttempt)
                    logger.LogWarning("首次加载“我喜欢”红心缓存失败：未找到任何歌单。");
                return;
            }

            var likePlaylist = ResolveLikePlaylist(playlists.Playlists);
            if (likePlaylist == null || string.IsNullOrWhiteSpace(likePlaylist.ListCreateId))
            {
                logger.LogWarning("加载收藏列表失败: 未定位到“我喜欢”歌单。");
                if (isFirstAttempt)
                    logger.LogWarning("首次加载“我喜欢”红心缓存失败：无法识别“我喜欢”歌单。");
                return;
            }

            logger.LogInformation("已识别“我喜欢”歌单: Name={Name}, ListId={ListId}, IsDefault={IsDefault}, ListCreateId={ListCreateId}",
                likePlaylist.Name, likePlaylist.ListId, likePlaylist.IsDefault, likePlaylist.ListCreateId);

            var songs = await playlistClient.GetSongsAsync(likePlaylist.ListCreateId, pageSize: 500);
            
            logger.LogInformation($"首次“我喜欢”红心缓存加载完成: Songs={songs.Count}, Hashes={_likedHashes.Count}, FileIds={_hashToFileId.Count}");
            
            lock (_likedHashes)
            {
                _likedHashes.Clear();
                _hashToFileId.Clear();
                foreach (var song in songs)
                    if (!string.IsNullOrEmpty(song.Hash))
                    {
                        _likedHashes.Add(song.Hash.ToLowerInvariant());
                        if (song.FileId != 0) _hashToFileId[song.Hash.ToLowerInvariant()] = song.FileId;
                    }
            }

            if (isFirstAttempt || !_hasLoggedFirstLikeCacheSuccess)
            {
                _hasLoggedFirstLikeCacheSuccess = true;
                logger.LogInformation($"首次“我喜欢”红心缓存加载完成: Songs={songs.Count}, Hashes={_likedHashes.Count}, FileIds={_hashToFileId.Count}");
            }
            else
            {
                logger.LogDebug("刷新“我喜欢”红心缓存完成: Songs={SongCount}, Hashes={HashCount}, FileIds={FileIdCount}",
                    songs.Count, _likedHashes.Count, _hashToFileId.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载收藏列表异常。");
            if (isFirstAttempt)
                logger.LogWarning("首次加载“我喜欢”红心缓存发生异常，当前红心状态可能暂时不准确。");
        }
    }

    public bool IsLiked(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
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

                        return false;
                    }
                }
                else
                {
                    await LoadLikeListAsync(); // 找不到ID，重新同步一次
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

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        await LoadLikeListAsync();
                    }); // 后台静默刷新拿FileId
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"操作收藏失败: {ex.Message}");
        }

        return currentIsLiked; // 失败则保持原状态
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
            logger.LogError($"获取歌单列表失败 err_code{playlists?.ErrorCode}");
        }
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

    private static UserPlaylistItem? ResolveLikePlaylist(List<UserPlaylistItem> playlists)
    {
        return playlists.FirstOrDefault(x => x.ListId == 2)
               ?? playlists.FirstOrDefault(x => x.IsDefault == 2)
               ?? playlists.FirstOrDefault(x => x.Name.Contains("喜欢", StringComparison.OrdinalIgnoreCase))
               ?? playlists.FirstOrDefault(x => x.Name.Contains("我喜欢", StringComparison.OrdinalIgnoreCase));
    }
}
