using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Jellyfin;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase
{
    private const int UserPlaylistPageSize = 100;
    private const int MaxUserPlaylistPages = 200;
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string LikeCover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg";

    private readonly ICreatePlaylistDialogService _createPlaylistDialogService;
    private readonly IExternalPlaylistImportService _externalPlaylistImportService;
    private readonly FavoritePlaylistService _favoritePlaylistService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly ILocalMusicLibraryService _localMusicLibraryService;
    private readonly ILogger<MyPlaylistsViewModel> _logger;
    private readonly AlbumClient _albumClient;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private readonly UserClient _userClient;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    [ObservableProperty]
    public partial bool IsImportingExternalPlaylist { get; set; }

    [ObservableProperty]
    public partial bool IsImportingJellyfinLibrary { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingLocalLibrary { get; set; }

    private bool _isLikePlaylistLocalMode;
    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalLibraryHome))]
    public partial bool IsShowingSongs { get; set; }

    private CancellationTokenSource? _refreshPlaylistsCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnlinePlaylist))]
    [NotifyPropertyChangedFor(nameof(IsLocalPlaylist))]
    [NotifyPropertyChangedFor(nameof(IsLocalLibraryHome))]
    public partial PlaylistItem? SelectedPlaylist { get; set; }

    public MyPlaylistsViewModel(
        UserClient userClient,
        PlaylistClient playlistClient,
        AlbumClient albumClient,
        FavoritePlaylistService favoritePlaylistService,
        ISukiToastManager toastManager,
        ICreatePlaylistDialogService createPlaylistDialogService,
        IFolderPickerService folderPickerService,
        IJellyfinClient jellyfinClient,
        ILocalMusicLibraryService localMusicLibraryService,
        IExternalPlaylistImportService externalPlaylistImportService,
        ILogger<MyPlaylistsViewModel> logger)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;
        _albumClient = albumClient;
        _favoritePlaylistService = favoritePlaylistService;
        _toastManager = toastManager;
        _createPlaylistDialogService = createPlaylistDialogService;
        _folderPickerService = folderPickerService;
        _jellyfinClient = jellyfinClient;
        _localMusicLibraryService = localMusicLibraryService;
        _externalPlaylistImportService = externalPlaylistImportService;
        _logger = logger;

        _ = LoadAllPlaylists();

        WeakReferenceMessenger.Default.Register<RemoveFromPlaylistMessage>(this,
            (_, m) => _ = RemoveSongFromPlaylistSafelyAsync(m.Song));
        WeakReferenceMessenger.Default.Register<SetLocalSongCoverMessage>(this,
            (_, m) => _ = SetLocalSongCoverSafelyAsync(m.Song));

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (r, m) => { _ = LoadAllPlaylists(); });
        WeakReferenceMessenger.Default.Register<RefreshPlaylistsMessage>(this,
            (r, m) => { _ = SchedulePlaylistsRefreshAsync("RefreshPlaylistsMessage", 1500); });
    }

    // 标识当前选中的歌单是否为网络歌单
    public bool IsOnlinePlaylist => SelectedPlaylist?.Type == PlaylistType.Online;
    public bool IsLocalPlaylist => SelectedPlaylist?.Type == PlaylistType.Local;
    public bool IsLocalLibraryHome => !IsShowingSongs;

    public override string DisplayName => "我的歌单";
    public override string Icon => "/Assets/music-player-svgrepo-com.svg";

    public AvaloniaList<PlaylistItem> Playlists { get; } = new();

    public AvaloniaList<SongItem> SelectedPlaylistSongs { get; } = new();

    public AvaloniaList<PlaylistItem> Items { get; } = new();

    public AvaloniaList<PlaylistItem> LocalLibraryPlaylists { get; } = new();

    [RelayCommand]
    private void GoBack()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        SelectedPlaylistSongs.Clear();
        OnPropertyChanged(nameof(IsLocalLibraryHome));
    }

    [RelayCommand]
    private void BackFromPlaylist()
    {
        if (SelectedPlaylist?.Type == PlaylistType.Local)
        {
            GoBack();
            return;
        }

        WeakReferenceMessenger.Default.Send(new RequestNavigateBackMessage());
    }

    [RelayCommand]
    private async Task LoadAllPlaylists()
    {
        Items.Clear();
        LocalLibraryPlaylists.Clear();

        try
        {
            var localPlaylists = await _localMusicLibraryService.GetPlaylistsAsync();
            var localItems = localPlaylists.Select(ToLocalPlaylistItem).ToList();
            LocalLibraryPlaylists.AddRange(localItems);
            Items.AddRange(localItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载本地音乐库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("本地音乐库加载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }

        if (!_userClient.IsLoggedIn())
            return;

        var onlinePlaylists = await LoadAllOnlinePlaylistsAsync();
        if (onlinePlaylists is not null && onlinePlaylists.Status == 1)
        {
            var onlineItems = new List<PlaylistItem>();
            foreach (var item in OrderUserPlaylistsForDisplay(onlinePlaylists.Playlists))
                if (!string.IsNullOrEmpty(item.ListCreateId) || item.IsCollectedAlbum)
                    onlineItems.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.IsCollectedAlbum ? item.AlbumId.ToString() : item.ListCreateId,
                        ListId = item.ListId,
                        Count = item.Count,
                        Type = item.IsCollectedAlbum ? PlaylistType.Album : PlaylistType.Online,
                        Cover = string.IsNullOrWhiteSpace(item.Pic)
                            ? item.ListId != 2 ? DefaultCover : LikeCover
                            : item.Pic,
                        Subtitle = item.ListCreateUsername
                    });

            if (onlineItems.Count > 0)
                Items.AddRange(onlineItems);
        }
        else
        {
            _logger.LogError($"加载失败,err_code{onlinePlaylists?.ErrorCode}");
            if (_favoritePlaylistService.TryGetLikePlaylistCache(out var cachedLike))
            {
                Items.Add(cachedLike.Playlist);
                _logger.LogInformation("歌单列表远端失败，已从本地缓存兜底显示“我喜欢”。 source={Source}", cachedLike.Source);
            }
        }
    }

    private static IEnumerable<UserPlaylistItem> OrderUserPlaylistsForDisplay(List<UserPlaylistItem> playlists)
    {
        if (playlists.Count <= 2)
            return playlists;

        return playlists.Take(2).Concat(playlists.Skip(2).Reverse());
    }

    private static PlaylistItem ToLocalPlaylistItem(LocalPlaylistSummary item)
    {
        return new PlaylistItem
        {
            Name = item.Name,
            Id = item.Id.ToString(),
            Count = item.TrackCount,
            Type = PlaylistType.Local,
            Cover = GetImageSourceOrDefault(item.CoverPath, DefaultCover),
            Subtitle = $"{item.TrackCount} 首歌曲"
        };
    }

    private async Task<UserPlaylistResponse?> LoadAllOnlinePlaylistsAsync()
    {
        UserPlaylistResponse? result = null;

        for (var page = 1; page <= MaxUserPlaylistPages; page++)
        {
            var data = await _userClient.GetPlaylistsAsync(page, UserPlaylistPageSize);
            if (data is null)
                return result;

            result ??= data with { Playlists = new List<UserPlaylistItem>() };

            if (data.Status != 1)
            {
                result.Status = data.Status;
                result.ErrorCode = data.ErrorCode;
                return result;
            }

            if (data.Playlists.Count == 0)
                break;

            result.Playlists.AddRange(data.Playlists);
            result.ListCount = Math.Max(result.ListCount, data.ListCount);

            if (data.ListCount > 0 && result.Playlists.Count >= data.ListCount)
                break;

            if (data.Playlists.Count < UserPlaylistPageSize)
                break;
        }

        return result;
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
        _isLikePlaylistLocalMode = false;

        if (item.Type == PlaylistType.Online)
        {
            if (IsLikePlaylist(item))
            {
                var loadedLocal = false;
                if (_favoritePlaylistService.TryGetLikePlaylistCache(out var likeCache) && likeCache.Songs.Count > 0)
                {
                    SelectedPlaylistSongs.AddRange(likeCache.Songs);
                    _isLikePlaylistLocalMode = true;
                    _hasMoreSongs = false; // 本地快照阶段禁分页，等待远端接管
                    loadedLocal = true;
                    /*_logger.LogInformation("打开“我喜欢”歌单命中本地缓存: source={Source}, songs={SongCount}, updatedAt={UpdatedAt}",
                        likeCache.Source, likeCache.Songs.Count, likeCache.UpdatedAt);*/
                }

                _ = RefreshLikePlaylistAfterOpenAsync(item, loadedLocal);
                if (!loadedLocal)
                    await LoadMoreSongsInternal();
            }
            else
            {
                await LoadMoreSongsInternal();
            }
        }
        else if (item.Type == PlaylistType.Album)
        {
            await LoadAlbumSongsAsync();
        }
        else if (item.Type == PlaylistType.Local)
        {
            await LoadLocalPlaylistSongsAsync(item);
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if ((SelectedPlaylist?.Type != PlaylistType.Online && SelectedPlaylist?.Type != PlaylistType.Album) ||
            IsLoadingMore || !_hasMoreSongs ||
            _isLikePlaylistLocalMode)
            return;

        _currentPage++;
        if (SelectedPlaylist?.Type == PlaylistType.Album)
            await LoadAlbumSongsAsync();
        else
            await LoadMoreSongsInternal();
    }

    private async Task LoadLocalPlaylistSongsAsync(PlaylistItem item)
    {
        if (!long.TryParse(item.Id, out var playlistId))
            return;

        IsLoadingMore = true;
        try
        {
            var tracks = await _localMusicLibraryService.GetPlaylistTracksAsync(playlistId);
            SelectedPlaylistSongs.Clear();
            SelectedPlaylistSongs.AddRange(tracks.Select(ToSongItem));
            _hasMoreSongs = false;
            item.Count = tracks.Count;
            item.Subtitle = $"{tracks.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载本地歌单歌曲失败 playlistId={PlaylistId}", item.Id);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("加载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private static SongItem ToSongItem(LocalTrackItem item)
    {
        var isJellyfinTrack = string.Equals(
            item.SourceType,
            LocalMusicLibraryService.SourceTypeJellyfin,
            StringComparison.Ordinal);

        return new SongItem
        {
            LocalTrackId = item.Id,
            Name = item.Title,
            Singer = item.Artist,
            AlbumName = item.Album,
            DurationSeconds = item.DurationSeconds,
            LocalSourceType = item.SourceType,
            LocalFilePath = item.LocalPath,
            RemoteUrl = item.RemoteUrl,
            Cover = isJellyfinTrack
                ? string.IsNullOrWhiteSpace(item.CoverPath) ? DefaultSongCover : item.CoverPath
                : GetImageSourceOrDefault(item.CoverPath, DefaultSongCover)
        };
    }

    [RelayCommand]
    private async Task DeleteLocalPlaylist(PlaylistItem? item)
    {
        if (item == null || item.Type != PlaylistType.Local) return;

        if (!long.TryParse(item.Id, out var playlistId))
            return;

        try
        {
            await _localMusicLibraryService.DeletePlaylistAsync(playlistId);
            Items.Remove(item);
            LocalLibraryPlaylists.Remove(item);

            if (SelectedPlaylist != null && SelectedPlaylist.Type == PlaylistType.Local && SelectedPlaylist.Id == item.Id)
            {
                SelectedPlaylist = null;
                SelectedPlaylistSongs.Clear();
                IsShowingSongs = false;
                WeakReferenceMessenger.Default.Send(new RequestNavigateBackMessage());
            }

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("已删除")
                .WithContent($"已删除本地歌单「{item.Name}」")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除本地歌单失败 playlistId={PlaylistId}", item.Id);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task EditLocalPlaylist(PlaylistItem? item)
    {
        item ??= SelectedPlaylist;
        if (item?.Type != PlaylistType.Local || !long.TryParse(item.Id, out var playlistId)) return;

        var result = await _createPlaylistDialogService.PromptLocalPlaylistEditAsync(item.Name, LocalPathFromImageSource(item.Cover));
        if (result == null)
            return;

        await _localMusicLibraryService.UpdatePlaylistAsync(playlistId, result.Name, result.CoverPath);

        item.Name = result.Name;
        item.Cover = GetImageSourceOrDefault(result.CoverPath, DefaultCover);
        item.Subtitle = $"{item.Count} 首歌曲";

        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已保存")
            .WithContent($"已更新本地歌单「{item.Name}」")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand]
    private async Task SetLocalSongCover(SongItem? song)
    {
        if (song == null || SelectedPlaylist?.Type != PlaylistType.Local || song.LocalTrackId <= 0)
            return;

        var coverPath = await _folderPickerService.PickSingleImageFileAsync("选择歌曲封面");
        if (string.IsNullOrWhiteSpace(coverPath))
            return;

        await _localMusicLibraryService.SetTrackCoverAsync(song.LocalTrackId, coverPath);

        song.Cover = GetImageSourceOrDefault(coverPath, DefaultSongCover);

        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已设置封面")
            .WithContent($"已更新「{song.Name}」的本地封面")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

    private async Task LoadMoreSongsInternal()
    {
        if (SelectedPlaylist == null) return;

        IsLoadingMore = true;
        try
        {
            var data = await _playlistClient.GetSongsAsync(SelectedPlaylist.Id, _currentPage, 100);
            if (data is null)
            {
                _currentPage = Math.Max(1, _currentPage - 1);
                return;
            }

            if (data.Status != 1) _logger.LogWarning($"Error : {data.ErrorCode}");
            var songs = data.Songs;

            if (songs.Count < 100) _hasMoreSongs = false;

            var songItems = songs.Select(s => new SongItem
            {
                Name = s.Name,
                Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                Hash = s.Hash,
                AlbumId = s.AlbumId,
                AlbumName = s.Album?.Name ?? "",
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

    private async Task LoadAlbumSongsAsync()
    {
        if (SelectedPlaylist == null || SelectedPlaylist.Type != PlaylistType.Album) return;

        IsLoadingMore = true;
        try
        {
            var songs = await _albumClient.GetSongsAsync(SelectedPlaylist.Id, _currentPage, 50);
            if (songs == null)
            {
                _currentPage = Math.Max(1, _currentPage - 1);
                _hasMoreSongs = false;
                return;
            }

            if (songs.Count < 50) _hasMoreSongs = false;

            var songItems = songs.Select(s => new SongItem
            {
                Name = s.Name,
                Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                Hash = s.Hash,
                AlbumId = s.AlbumId,
                AlbumName = s.AlbumInfo.AlbumName,
                Singers = s.Singers,
                Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                DurationSeconds = s.DurationMs / 1000.0
            }).ToList();

            if (songItems.Count > 0)
                SelectedPlaylistSongs.AddRange(songItems);
        }
        catch (Exception)
        {
            _currentPage = Math.Max(1, _currentPage - 1);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task ShowCreateLocalPlaylistDialog()
    {
        var name = await _createPlaylistDialogService.PromptPlaylistNameAsync("新建本地歌单");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var playlist = await _localMusicLibraryService.CreatePlaylistAsync(name);
            await LoadAllPlaylists();
            var item = LocalLibraryPlaylists.FirstOrDefault(x => x.Id == playlist.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("创建成功")
                .WithContent($"已创建本地歌单「{playlist.Name}」")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建本地歌单失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("创建失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task AddLocalFiles()
    {
        var files = await _folderPickerService.PickAudioFilesAsync("选择本地歌曲");
        if (files.Count == 0)
            return;

        try
        {
            var target = SelectedPlaylist?.Type == PlaylistType.Local ? SelectedPlaylist : LocalLibraryPlaylists.FirstOrDefault();
            if (target == null)
            {
                var playlist = await _localMusicLibraryService.CreatePlaylistAsync("本地歌曲");
                await LoadAllPlaylists();
                target = LocalLibraryPlaylists.FirstOrDefault(x => x.Id == playlist.Id.ToString());
            }

            if (target == null || !long.TryParse(target.Id, out var playlistId))
                return;

            await _localMusicLibraryService.AddFilesToPlaylistAsync(playlistId, files);
            await LoadAllPlaylists();

            target = LocalLibraryPlaylists.FirstOrDefault(x => x.Id == playlistId.ToString());
            if (target != null)
                await OpenPlaylist(target);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("已添加")
                .WithContent($"已添加 {files.Count} 首本地歌曲。")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加本地歌曲失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("添加失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private void OpenLocalLibraryHome()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        SelectedPlaylistSongs.Clear();
    }

    [RelayCommand]
    private async Task ImportLocalFolder()
    {
        var path = await _folderPickerService.PickSingleFolderAsync("选择本地音乐文件夹");
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsLoadingMore = true;
        try
        {
            var imported = await _localMusicLibraryService.ImportFolderAsync(path);
            await LoadAllPlaylists();
            var item = LocalLibraryPlaylists.FirstOrDefault(x => x.Id == imported.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("导入完成")
                .WithContent($"已导入本地歌单「{imported.Name}」，共 {imported.TrackCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入本地文件夹失败 path={Path}", path);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("导入失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLocalLibrary()
    {
        if (IsRefreshingLocalLibrary)
            return;

        IsRefreshingLocalLibrary = true;
        var progressToast = _toastManager.CreateToast()
            .WithTitle("正在刷新本地音乐库...")
            .WithContent("正在重新扫描已导入的文件夹和 Jellyfin 媒体库。")
            .Queue();
        try
        {
            var refreshed = await _localMusicLibraryService.RefreshImportedLibrariesAsync();
            await LoadAllPlaylists();

            if (SelectedPlaylist?.Type == PlaylistType.Local &&
                long.TryParse(SelectedPlaylist.Id, out var playlistId) &&
                refreshed.Any(x => x.Id == playlistId))
            {
                await LoadLocalPlaylistSongsAsync(SelectedPlaylist);
            }

            var songCount = refreshed.Sum(x => x.TrackCount);
            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("刷新完成")
                .WithContent(refreshed.Count == 0
                    ? "没有可刷新的导入音乐库。"
                    : $"已刷新 {refreshed.Count} 个歌单，共 {songCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新本地音乐库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("刷新失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            _toastManager.Dismiss(progressToast);
            IsRefreshingLocalLibrary = false;
        }
    }

    [RelayCommand]
    private async Task ImportExternalPlaylist()
    {
        if (IsImportingExternalPlaylist)
            return;

        var link = await _createPlaylistDialogService.PromptTextAsync(
            "导入其他平台歌单",
            "请输入网易云或QQ音乐歌单分享链接",
            confirmText: "下一步");

        if (string.IsNullOrWhiteSpace(link))
            return;

        IsImportingExternalPlaylist = true;
        try
        {
            var parseResult = await _externalPlaylistImportService.ParseAndLoadAsync(link);
            if (!parseResult.Success)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("解析失败")
                    .WithContent(parseResult.ErrorMessage)
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            var targetName = await _createPlaylistDialogService.PromptPlaylistNameAsync(parseResult.SourcePlaylistName);
            if (string.IsNullOrWhiteSpace(targetName))
                return;

            var progressBar = new ProgressBar
            {
                Value = 0,
                Minimum = 0,
                Maximum = 100,
                ShowProgressText = true
            };
            var progressText = new TextBlock { Text = "准备导入..." };
            var progressContent = new StackPanel
            {
                Spacing = 8,
                Children = { progressText, progressBar }
            };

            var progressToast = _toastManager.CreateToast()
                .WithTitle("正在导入歌单...")
                .WithContent(progressContent)
                .Queue();

            var progressReporter = new Progress<ExternalPlaylistImportProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                progressText.Text = p.Message;
            });

            ExternalPlaylistImportResult importResult;
            try
            {
                importResult = await _externalPlaylistImportService.ImportToKugouAsync(
                    parseResult,
                    targetName,
                    progressReporter);
            }
            finally
            {
                _toastManager.Dismiss(progressToast);
            }

            if (!importResult.Success)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("导入失败")
                    .WithContent(importResult.ErrorMessage)
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            WeakReferenceMessenger.Default.Send(new RefreshPlaylistsMessage());

            var preview = string.Join("、", importResult.FailedNames.Take(10));
            var summary =
                $"来源：{parseResult.SourcePlatform}\n总计 {importResult.Total} 首，匹配 {importResult.Matched} 首，成功导入 {importResult.Imported} 首。";
            if (importResult.FailedNames.Count > 0)
                summary += $"\n未命中 {importResult.FailedNames.Count} 首：{preview}";
            else if (importResult.Matched == 0)
                summary += "\n未匹配到可导入歌曲。";

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("导入完成")
                .WithContent(summary)
                .Dismiss().After(TimeSpan.FromSeconds(6))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入其他平台歌单时发生异常");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("导入失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsImportingExternalPlaylist = false;
        }
    }

    [RelayCommand]
    private async Task ImportJellyfinLibrary()
    {
        if (IsImportingJellyfinLibrary)
            return;

        var lastSettings = GetLastJellyfinSettings();
        var options = await _createPlaylistDialogService.PromptJellyfinConnectionAsync(lastSettings);
        if (options == null)
            return;

        IsImportingJellyfinLibrary = true;
        try
        {
            var libraries = await _jellyfinClient.GetMusicLibrariesAsync(options);
            if (libraries.Count == 0)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("未找到音乐库")
                    .WithContent("当前 Jellyfin 用户没有可导入的音乐媒体库。")
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            SaveJellyfinSettings(options);

            var library = await _createPlaylistDialogService.PromptJellyfinLibraryAsync(libraries);
            if (library == null)
                return;

            var progressBar = new ProgressBar
            {
                Value = 0,
                Minimum = 0,
                Maximum = 100,
                ShowProgressText = true
            };
            var progressText = new TextBlock { Text = "准备导入..." };
            var progressContent = new StackPanel
            {
                Spacing = 8,
                Children = { progressText, progressBar }
            };

            var progressToast = _toastManager.CreateToast()
                .WithTitle("正在导入 Jellyfin...")
                .WithContent(progressContent)
                .Queue();

            var progressReporter = new Progress<JellyfinImportProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                progressText.Text = p.Message;
            });

            IReadOnlyList<LocalPlaylistSummary> imported;
            try
            {
                imported = await _localMusicLibraryService.ImportJellyfinLibraryAsync(
                    options,
                    library,
                    progressReporter);
            }
            finally
            {
                _toastManager.Dismiss(progressToast);
            }

            await LoadAllPlaylists();
            var firstImported = imported.FirstOrDefault();
            var item = firstImported == null
                ? null
                : LocalLibraryPlaylists.FirstOrDefault(x => x.Id == firstImported.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            var importedSongCount = imported.Sum(x => x.TrackCount);
            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("导入完成")
                .WithContent($"已按专辑同步 Jellyfin 媒体库「{library.Name}」，生成 {imported.Count} 个本地歌单，共 {importedSongCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入 Jellyfin 媒体库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("导入失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsImportingJellyfinLibrary = false;
        }
    }

    private static JellyfinServerSettings? GetLastJellyfinSettings()
    {
        var fingerprint = SettingsManager.Settings.LastJellyfinServerFingerprint;
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        return SettingsManager.Settings.JellyfinServers.TryGetValue(fingerprint, out var settings)
            ? settings
            : null;
    }

    private void SaveJellyfinSettings(JellyfinConnectionOptions options)
    {
        var fingerprint = _jellyfinClient.GetServerFingerprint(options.ServerUrl);
        SettingsManager.Settings.JellyfinServers[fingerprint] = new JellyfinServerSettings
        {
            ServerUrl = options.ServerUrl.Trim().TrimEnd('/'),
            UserId = options.UserId.Trim(),
            ApiKey = options.ApiKey.Trim()
        };
        SettingsManager.Settings.LastJellyfinServerFingerprint = fingerprint;
        SettingsManager.Save();
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
                WeakReferenceMessenger.Default.Send(new RefreshPlaylistsMessage());
                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("创建成功")
                    .WithContent($"已创建歌单「{name}」")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
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

    [RelayCommand]
    private async Task ShowCreatePlaylistDialog()
    {
        var name = await _createPlaylistDialogService.PromptPlaylistNameAsync();
        if (!string.IsNullOrWhiteSpace(name))
            await CreateOnlinePlaylistCommand.ExecuteAsync(name);
    }

    [RelayCommand]
    private async Task DeleteOnlinePlaylist(PlaylistItem? item)
    {
        if (item == null || (item.Type != PlaylistType.Online && item.Type != PlaylistType.Album)) return;

        try
        {
            var result = await _playlistClient.DeletePlaylistAsync(item.ListId.ToString());
            if (result != null)
            {
                var removed = RemoveOnlinePlaylistLocally(item);
                _logger.LogInformation("移除收藏项后本地移除结果: removed={Removed}, type={Type}, listId={ListId}, id={Id}",
                    removed, item.Type, item.ListId, item.Id);
                _ = SchedulePlaylistsRefreshAsync("DeleteOnlinePlaylist", 1500);

                var successTitle = item.Type == PlaylistType.Album ? "取消收藏成功" : "删除成功";
                var successContent = item.Type == PlaylistType.Album
                    ? $"已取消收藏专辑「{item.Name}」"
                    : $"已删除歌单「{item.Name}」";

                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle(successTitle)
                    .WithContent(successContent)
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            var failTitle = item.Type == PlaylistType.Album ? "取消收藏失败" : "删除失败";
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle(failTitle)
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    private bool RemoveOnlinePlaylistLocally(PlaylistItem item)
    {
        var target = Items.FirstOrDefault(x =>
            (x.Type == PlaylistType.Online || x.Type == PlaylistType.Album) &&
            (x.ListId == item.ListId || (!string.IsNullOrEmpty(item.Id) && x.Id == item.Id)));

        if (target == null)
            return false;

        Items.Remove(target);
        if (SelectedPlaylist != null &&
            (SelectedPlaylist.ListId == target.ListId || SelectedPlaylist.Id == target.Id))
        {
            SelectedPlaylist = null;
            SelectedPlaylistSongs.Clear();
            IsShowingSongs = false;
            WeakReferenceMessenger.Default.Send(new RequestNavigateBackMessage());
        }

        return true;
    }

    private async Task SchedulePlaylistsRefreshAsync(string reason, int delayMs)
    {
        _refreshPlaylistsCts?.Cancel();
        _refreshPlaylistsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshPlaylistsCts = cts;

        try
        {
            await Task.Delay(delayMs, cts.Token);
            if (cts.IsCancellationRequested) return;
            await LoadAllPlaylists();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("取消歌单刷新: reason={Reason}", reason);
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

    private async Task RemoveSongFromPlaylistSafelyAsync(SongItem? song)
    {
        try
        {
            await RemoveSongFromPlaylist(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理移除歌曲消息失败");
        }
    }

    private async Task SetLocalSongCoverSafelyAsync(SongItem? song)
    {
        try
        {
            await SetLocalSongCover(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理设置本地歌曲封面消息失败");
        }
    }

    private async Task RefreshLikePlaylistAfterOpenAsync(PlaylistItem openedItem, bool hadLocalSnapshot)
    {
        try
        {
            await _favoritePlaylistService.LoadLikeListAsync();

            var firstPage = await _playlistClient.GetSongsAsync(openedItem.Id, 1, 100);
            if (firstPage is null || firstPage.Status != 1)
            {
                _logger.LogWarning("打开“我喜欢”后远端接管失败，保留本地列表。 err_code={ErrorCode}, hadLocalSnapshot={HadLocalSnapshot}",
                    firstPage?.ErrorCode, hadLocalSnapshot);
                return;
            }

            var songItems = (firstPage.Songs ?? new List<PlaylistSong>()).Select(s => new SongItem
            {
                Name = s.Name,
                Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                Hash = s.Hash,
                AlbumId = s.AlbumId,
                AlbumName = s.Album?.Name ?? "",
                FileId = s.FileId,
                Singers = s.Singers,
                Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                DurationSeconds = s.DurationMs / 1000.0
            }).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedPlaylist == null || !IsLikePlaylist(SelectedPlaylist) ||
                    SelectedPlaylist.Id != openedItem.Id)
                    return;

                SelectedPlaylistSongs.Clear();
                SelectedPlaylistSongs.AddRange(songItems);
                _currentPage = 1;
                _hasMoreSongs = songItems.Count >= 100;
                _isLikePlaylistLocalMode = false;
            });

            /*_logger.LogInformation("打开“我喜欢”后远端分页接管成功: songs={SongCount}, hasMore={HasMore}",
                songItems.Count, _hasMoreSongs);*/
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开“我喜欢”后后台刷新失败，保留本地数据。");
        }
    }

    private static bool IsLikePlaylist(PlaylistItem item)
    {
        return item.ListId == 2 || string.Equals(item.Name, "我喜欢", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetImageSourceOrDefault(string? imagePath, string defaultSource)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return defaultSource;

        if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "avares"))
        {
            return imagePath;
        }

        if (!System.IO.File.Exists(imagePath))
            return defaultSource;

        return new Uri(imagePath).AbsoluteUri;
    }

    private static string? LocalPathFromImageSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsFile)
            return null;

        return uri.LocalPath;
    }
}
