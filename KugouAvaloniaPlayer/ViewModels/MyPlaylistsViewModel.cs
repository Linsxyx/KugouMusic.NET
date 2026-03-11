using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using TagLib;
using File = TagLib.File;
using TextBox = Avalonia.Controls.TextBox;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string LikeCover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg";
    private readonly ISukiDialogManager _dialogManager;
    private readonly ILogger<MyPlaylistsViewModel> _logger;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private readonly UserClient _userClient;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private bool _isShowingSongs;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOnlinePlaylist))]
    private PlaylistItem? _selectedPlaylist;

    public MyPlaylistsViewModel(
        UserClient userClient,
        PlaylistClient playlistClient,
        ISukiToastManager toastManager,
        ISukiDialogManager dialogManager,
        ILogger<MyPlaylistsViewModel> logger)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;
        _toastManager = toastManager;
        _dialogManager = dialogManager;
        _logger = logger;

        _ = LoadAllPlaylists();

        WeakReferenceMessenger.Default.Register<RemoveFromPlaylistMessage>(this,
            async void (r, m) => { await RemoveSongFromPlaylist(m.Song); });

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (r, m) => { _ = LoadAllPlaylists(); });
    }

    // 标识当前选中的歌单是否为网络歌单
    public bool IsOnlinePlaylist => SelectedPlaylist?.Type == PlaylistType.Online;

    public override string DisplayName => "我的歌单";
    public override string Icon => "/Assets/music-player-svgrepo-com.svg";

    public AvaloniaList<PlaylistItem> Playlists { get; } = new();

    public AvaloniaList<SongItem> SelectedPlaylistSongs { get; } = new();

    public AvaloniaList<PlaylistItem> Items { get; } = new();

    [RelayCommand]
    private void GoBack()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        SelectedPlaylistSongs.Clear();
    }

    [RelayCommand]
    private async Task LoadAllPlaylists()
    {
        Items.Clear();

        Items.Add(new PlaylistItem
        {
            Name = "新建/添加",
            Type = PlaylistType.AddButton
        });

        var localItems = new List<PlaylistItem>();
        foreach (var path in SettingsManager.Settings.LocalMusicFolders)
            if (Directory.Exists(path))
                localItems.Add(new PlaylistItem
                {
                    Name = new DirectoryInfo(path).Name,
                    LocalPath = path,
                    Type = PlaylistType.Local,
                    Cover = DefaultCover,
                    Count = 0
                });

        if (localItems.Count > 0)
            Items.AddRange(localItems);
        try
        {
            var onlinePlaylists = await _userClient.GetPlaylistsAsync();
            var onlineItems = new List<PlaylistItem>();
            foreach (var item in onlinePlaylists)
                if (!string.IsNullOrEmpty(item.ListCreateId))
                    onlineItems.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.ListCreateId,
                        ListId = item.ListId, // 保存数字 ID 用于删除
                        Count = item.Count,
                        Type = PlaylistType.Online,
                        Cover = string.IsNullOrWhiteSpace(item.Pic)
                            ? item.ListId != 2 ? DefaultCover : LikeCover
                            : item.Pic
                    });

            if (onlineItems.Count > 0)
                Items.AddRange(onlineItems);
        }
        catch
        {
            _logger.LogError("加载失败");
        }
    }

    [RelayCommand]
    private async Task OpenPlaylist(PlaylistItem item)
    {
        if (item.Type == PlaylistType.AddButton) return;

        SelectedPlaylist = item;
        IsShowingSongs = true;
        SelectedPlaylistSongs.Clear();

        _currentPage = 1;
        _hasMoreSongs = true;
        IsLoadingMore = false;

        if (item.Type == PlaylistType.Online)
            await LoadMoreSongsInternal();
        else if (item.Type == PlaylistType.Local) await ScanLocalFolder(item.LocalPath!);
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (SelectedPlaylist?.Type != PlaylistType.Online || IsLoadingMore || !_hasMoreSongs)
            return;

        _currentPage++;
        await LoadMoreSongsInternal();
    }

    [RelayCommand]
    private void DeleteLocalPlaylist(PlaylistItem? item)
    {
        if (item == null || item.Type != PlaylistType.Local) return;

        Items.Remove(item);

        if (!string.IsNullOrEmpty(item.LocalPath))
            if (SettingsManager.Settings.LocalMusicFolders.Contains(item.LocalPath))
            {
                SettingsManager.Settings.LocalMusicFolders.Remove(item.LocalPath);
                SettingsManager.Save();
            }
    }

    private async Task LoadMoreSongsInternal()
    {
        if (SelectedPlaylist == null) return;

        IsLoadingMore = true;
        try
        {
            var songs = await _playlistClient.GetSongsAsync(SelectedPlaylist.Id, _currentPage, 100);

            if (songs.Count < 100) _hasMoreSongs = false;

            var songItems = songs.Select(s => new SongItem
            {
                Name = s.Name,
                Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                Hash = s.Hash,
                AlbumId = s.AlbumId,
                FileId = s.FileId, // 保存 FileId 用于删除
                Singers = s.Singers,
                Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                DurationSeconds = s.DurationMs / 1000.0
            }).ToList();

            if (songItems.Count > 0)
                SelectedPlaylistSongs.AddRange(songItems);
        }
        catch (Exception)
        {
            _currentPage--;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task ScanLocalFolder(string path)
    {
        await Task.Run(() =>
        {
            if (!Directory.Exists(path)) return;
            var supportedExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a" };

            try
            {
                var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                var tempList = new List<SongItem>();

                foreach (var file in files)
                {
                    var title = Path.GetFileNameWithoutExtension(file);
                    var singer = "未知艺术家";
                    double duration = 0;

                    try
                    {
                        using var tfile = File.Create(file);

                        title = !string.IsNullOrWhiteSpace(tfile.Tag.Title) ? tfile.Tag.Title : title;

                        var artists = tfile.Tag.Performers;
                        if (artists != null && artists.Length > 0) singer = string.Join(", ", artists);

                        duration = tfile.Properties?.Duration.TotalSeconds ?? 0;
                    }
                    catch (UnsupportedFormatException)
                    {
                        _logger.LogWarning($"文件格式不支持，已降级为文件名加载 [{file}]");
                    }
                    catch (CorruptFileException ex)
                    {
                        _logger.LogWarning($"文件头伪装或损坏，已降级为文件名加载 [{file}]: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"读取标签失败，已降级为文件名加载 [{file}]: {ex.Message}");
                    }

                    tempList.Add(new SongItem
                    {
                        Name = title,
                        Singer = singer,
                        DurationSeconds = duration,
                        LocalFilePath = file,
                        Cover = DefaultSongCover
                    });
                }

                Dispatcher.UIThread.Post(() =>
                {
                    SelectedPlaylistSongs.Clear();
                    SelectedPlaylistSongs.AddRange(tempList);
                });
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError($"权限错误: 无法访问文件夹 {path}");
                Dispatcher.UIThread.Post(() =>
                {
                    _toastManager.CreateToast()
                        .OfType(NotificationType.Error)
                        .WithTitle("无权访问")
                        .WithContent("无法读取某些文件夹，请检查权限。")
                        .Dismiss().After(TimeSpan.FromSeconds(3))
                        .Queue();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"扫描文件夹出错: {ex.Message}");
            }
        });
    }

    public void AddLocalPlaylist(string path)
    {
        if (!SettingsManager.Settings.LocalMusicFolders.Contains(path))
        {
            SettingsManager.Settings.LocalMusicFolders.Add(path);
            SettingsManager.Save();
        }

        _ = LoadAllPlaylists();
    }

    [RelayCommand]
    private async Task CreateOnlinePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var result = await _playlistClient.CreatePlaylistAsync(name);
            if (result != null)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("创建成功")
                    .WithContent($"已创建歌单「{name}」")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();

                await LoadAllPlaylists();
            }
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("创建失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    public void ShowCreatePlaylistDialog()
    {
        var textBox = new TextBox
        {
            Watermark = "请输入歌单名称",
            Width = 300
        };

        _dialogManager.CreateDialog()
            .WithTitle("新建歌单")
            .WithContent(textBox)
            .WithActionButton("取消", _ => { }, true, "Flat")
            .WithActionButton("创建", async _ =>
            {
                var name = textBox.Text;
                if (!string.IsNullOrWhiteSpace(name)) await CreateOnlinePlaylistCommand.ExecuteAsync(name);
            }, true, "Accent")
            .TryShow();
    }

    [RelayCommand]
    private async Task DeleteOnlinePlaylist(PlaylistItem? item)
    {
        if (item == null || item.Type != PlaylistType.Online) return;

        try
        {
            var result = await _playlistClient.DeletePlaylistAsync(item.ListId.ToString());
            if (result != null)
            {
                Items.Remove(item);
                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("删除成功")
                    .WithContent($"已删除歌单「{item.Name}」")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task RemoveSongFromPlaylist(SongItem? song)
    {
        if (song == null || SelectedPlaylist?.Type != PlaylistType.Online) return;

        try
        {
            var result =
                await _playlistClient.RemoveSongsAsync(SelectedPlaylist.ListId.ToString(), new[] { song.FileId });
            if (result != null)
            {
                SelectedPlaylistSongs.Remove(song);
                // 更新歌单歌曲数量
                if (SelectedPlaylist.Count > 0)
                    SelectedPlaylist.Count--;

                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("移除成功")
                    .WithContent($"已从歌单移除「{song.Name}」")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("移除失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }
}