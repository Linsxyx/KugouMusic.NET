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
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using TagLib;
using File = TagLib.File;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string LikeCover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg";
    private readonly ICreatePlaylistDialogService _createPlaylistDialogService;
    private readonly IExternalPlaylistImportService _externalPlaylistImportService;
    private readonly FavoritePlaylistService _favoritePlaylistService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<MyPlaylistsViewModel> _logger;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private readonly UserClient _userClient;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    [ObservableProperty] private bool _isImportingExternalPlaylist;
    private bool _isLikePlaylistLocalMode;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private bool _isShowingSongs;
    private CancellationTokenSource? _refreshPlaylistsCts;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOnlinePlaylist))]
    private PlaylistItem? _selectedPlaylist;

    public MyPlaylistsViewModel(
        UserClient userClient,
        PlaylistClient playlistClient,
        FavoritePlaylistService favoritePlaylistService,
        ISukiToastManager toastManager,
        ICreatePlaylistDialogService createPlaylistDialogService,
        IFolderPickerService folderPickerService,
        IExternalPlaylistImportService externalPlaylistImportService,
        ILogger<MyPlaylistsViewModel> logger)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;
        _favoritePlaylistService = favoritePlaylistService;
        _toastManager = toastManager;
        _createPlaylistDialogService = createPlaylistDialogService;
        _folderPickerService = folderPickerService;
        _externalPlaylistImportService = externalPlaylistImportService;
        _logger = logger;

        _ = LoadAllPlaylists();

        WeakReferenceMessenger.Default.Register<RemoveFromPlaylistMessage>(this,
            (_, m) => _ = RemoveSongFromPlaylistSafelyAsync(m.Song));

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (r, m) => { _ = LoadAllPlaylists(); });
        WeakReferenceMessenger.Default.Register<RefreshPlaylistsMessage>(this,
            (r, m) => { _ = SchedulePlaylistsRefreshAsync("RefreshPlaylistsMessage", 1500); });
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

        if (!_userClient.IsLoggedIn())
            return;

        var onlinePlaylists = await _userClient.GetPlaylistsAsync();
        if (onlinePlaylists is not null && onlinePlaylists.Status == 1)
        {
            var onlineItems = new List<PlaylistItem>();
            foreach (var item in onlinePlaylists.Playlists)
                if (!string.IsNullOrEmpty(item.ListCreateId))
                    onlineItems.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.ListCreateId,
                        ListId = item.ListId,
                        Count = item.Count,
                        Type = PlaylistType.Online,
                        Cover = string.IsNullOrWhiteSpace(item.Pic)
                            ? item.ListId != 2 ? DefaultCover : LikeCover
                            : item.Pic
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
        else if (item.Type == PlaylistType.Local)
        {
            await ScanLocalFolder(item.LocalPath!);
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (SelectedPlaylist?.Type != PlaylistType.Online || IsLoadingMore || !_hasMoreSongs ||
            _isLikePlaylistLocalMode)
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
                        if (artists is { Length: > 0 }) singer = string.Join(", ", artists);

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
    private async Task ImportLocalFolder()
    {
        var path = await _folderPickerService.PickSingleFolderAsync("选择本地音乐文件夹");
        if (!string.IsNullOrWhiteSpace(path))
            AddLocalPlaylist(path);
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
        if (item == null || item.Type != PlaylistType.Online) return;

        try
        {
            var result = await _playlistClient.DeletePlaylistAsync(item.ListId.ToString());
            if (result != null)
            {
                var removed = RemoveOnlinePlaylistLocally(item);
                _logger.LogInformation("删除歌单后本地移除结果: removed={Removed}, listId={ListId}, id={Id}",
                    removed, item.ListId, item.Id);
                _ = SchedulePlaylistsRefreshAsync("DeleteOnlinePlaylist", 1500);

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

    private bool RemoveOnlinePlaylistLocally(PlaylistItem item)
    {
        var target = Items.FirstOrDefault(x =>
            x.Type == PlaylistType.Online &&
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
}