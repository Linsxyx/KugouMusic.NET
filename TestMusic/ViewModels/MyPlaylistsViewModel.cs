using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using TestMusic.Models;
using TestMusic.Services;
using File = TagLib.File;

namespace TestMusic.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase
{
    private const string FolderCover = "avares://TestMusic/Assets/Default.png";
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    private const string LikeCover = "avares://TestMusic/Assets/LikeList.jpg";
    private readonly PlaylistClient _playlistClient;
    private readonly UserClient _userClient;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private bool _isShowingSongs;

    [ObservableProperty] private PlaylistItem? _selectedPlaylist;

    public MyPlaylistsViewModel(
        UserClient userClient,
        PlaylistClient playlistClient)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;

        _ = LoadAllPlaylists();
    }

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

        foreach (var path in SettingsManager.Settings.LocalMusicFolders)
            if (Directory.Exists(path))
            {
                var dirName = new DirectoryInfo(path).Name;
                Items.Add(new PlaylistItem
                {
                    Name = dirName,
                    LocalPath = path,
                    Type = PlaylistType.Local,
                    Cover = FolderCover, // 使用本地默认封面
                    Count = 0 // 暂时为0，点击进去扫描后再更新，或者后台预扫
                });
            }

        try
        {
            var onlinePlaylists = await _userClient.GetPlaylistsAsync();
            foreach (var item in onlinePlaylists)
                if (!string.IsNullOrEmpty(item.GlobalId))
                    Items.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.GlobalId,
                        Count = item.Count,
                        Type = PlaylistType.Online,
                        Cover = string.IsNullOrWhiteSpace(item.Pic)
                            ? item.ListId != 2 ? DefaultCover : LikeCover
                            : item.Pic
                    });
        }
        catch
        {
            //StatusMessage = "在线歌单加载失败";
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
    private void DeleteLocalPlaylist(PlaylistItem item)
    {
        if (item == null || item.Type != PlaylistType.Local) return;

        // 1. 从界面集合移除
        Items.Remove(item);

        // 2. 从配置中移除
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

            foreach (var s in songs)
            {
                var singerName = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知";

                SelectedPlaylistSongs.Add(new SongItem
                {
                    Name = s.Name,
                    Singer = singerName,
                    Hash = s.Hash,
                    AlbumId = s.AlbumId,
                    Singers = s.Singers,
                    Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultCover : s.Cover,
                    DurationSeconds = s.DurationMs / 1000.0
                });
            }
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
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                var tempList = new List<SongItem>();
                foreach (var file in files)
                    try
                    {
                        using var tfile = File.Create(file);
                        var title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(file);
                        var artists = tfile.Tag.Performers;
                        var singer = artists.Length > 0 ? string.Join(", ", artists) : "未知艺术家";

                        var songItem = new SongItem
                        {
                            Name = title,
                            Singer = singer,
                            DurationSeconds = tfile.Properties.Duration.TotalSeconds,
                            LocalFilePath = file,
                            Cover = DefaultCover
                        };

                        tempList.Add(songItem);
                    }
                    catch
                    {
                        /* 忽略损坏文件 */
                    }

                Dispatcher.UIThread.Post(() =>
                {
                    SelectedPlaylistSongs.Clear();
                    foreach (var item in tempList) SelectedPlaylistSongs.Add(item);
                });
            }
            catch
            {
                /* 忽略权限错误 */
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
}