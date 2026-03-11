using System;
using System.Collections.Generic;
using System.Linq;
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

public class FavoritePlaylistService
{
    private const string LikeListIdForAction = "2";
    private readonly ISukiDialogManager _dialogManager;
    private readonly Dictionary<string, int> _hashToFileId = new();

    private readonly HashSet<string> _likedHashes = new();
    private readonly ILogger<FavoritePlaylistService> _logger;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;

    private readonly UserClient _userClient;

    public FavoritePlaylistService(UserClient userClient, PlaylistClient playlistClient,
        ISukiToastManager toastManager, ISukiDialogManager dialogManager, ILogger<FavoritePlaylistService> logger)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;
        _toastManager = toastManager;
        _dialogManager = dialogManager;
        _logger = logger;
    }

    public async Task LoadLikeListAsync()
    {
        try
        {
            var playlists = await _userClient.GetPlaylistsAsync();
            if (playlists.Count < 1) return;

            var likePlaylist = playlists[1];
            if (string.IsNullOrEmpty(likePlaylist.ListCreateId)) return;

            var songs = await _playlistClient.GetSongsAsync(likePlaylist.ListCreateId, pageSize: 1000);

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
        }
        catch (Exception ex)
        {
            _logger.LogError($"加载收藏列表失败: {ex.Message}");
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
                    var result = await _playlistClient.RemoveSongsAsync(LikeListIdForAction, new List<long> { fileId });
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
                var result = await _playlistClient.AddSongsAsync(LikeListIdForAction, songList);
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
            _logger.LogError($"操作收藏失败: {ex.Message}");
        }

        return currentIsLiked; // 失败则保持原状态
    }

    public async Task ShowAddToPlaylistDialogAsync(SongItem song)
    {
        try
        {
            var playlists = await _userClient.GetPlaylistsAsync();
            var onlinePlaylists = playlists.Where(p => !string.IsNullOrEmpty(p.ListCreateId)).ToList();

            if (onlinePlaylists.Count == 0)
            {
                _toastManager.CreateToast().OfType(NotificationType.Warning).WithTitle("提示").WithContent("请先创建歌单")
                    .Queue();
                return;
            }

            var listBox = new ListBox
            {
                Width = 300, MaxHeight = 400, ItemsSource = onlinePlaylists, SelectionMode = SelectionMode.Single,
                ItemTemplate = new FuncDataTemplate<UserPlaylistItem>((item, _) => new TextBlock { Text = item.Name })
            };

            await _dialogManager.CreateDialog()
                .WithTitle("添加到歌单")
                .WithContent(listBox)
                .WithActionButton("取消", _ => { }, true)
                .WithActionButton("添加", async void (_) =>
                {
                    if (listBox.SelectedItem is UserPlaylistItem selectedPlaylist)
                        await AddSongToPlaylistInnerAsync(song, selectedPlaylist.ListId.ToString(),
                            selectedPlaylist.Name);
                }, true)
                .TryShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取歌单列表失败");
        }
    }

    private async Task AddSongToPlaylistInnerAsync(SongItem song, string playlistId, string playlistName)
    {
        try
        {
            var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
                { (song.Name, song.Hash, song.AlbumId, "0") };

            var result = await _playlistClient.AddSongsAsync(playlistId, songList);

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

                _toastManager.CreateToast().OfType(NotificationType.Success).WithTitle("添加成功")
                    .WithContent($"已添加到「{playlistName}」").Queue();
            }
            else
            {
                _toastManager.CreateToast().OfType(NotificationType.Error).WithTitle("添加失败").Queue();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加歌曲到歌单失败");
        }
    }
}