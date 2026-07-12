using System;
using System.Collections.Generic;
using System.Linq;
using ZLinq;
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

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase, IDisposable
{
    private const string DefaultSortText = "默认排序";
    private const string ArtistSortText = "按歌手排序";
    private const string AlbumSortText = "按专辑排序";
    private const int UserPlaylistPageSize = 100;
    private const int PlaylistSongPageSize = 200;
    private const int MaxUserPlaylistPages = 200;
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string LikeCover = "avares://KugouAvaloniaPlayer/Assets/LikeList.jpg";

    private readonly ICreatePlaylistDialogService _createPlaylistDialogService;
    private readonly IExternalPlaylistImportService _externalPlaylistImportService;
    private readonly FavoritePlaylistService _favoritePlaylistService;
    private readonly UserCreatedPlaylistCacheService _userCreatedPlaylistCacheService;
    private readonly ILogger<MyPlaylistsViewModel> _logger;
    private readonly AlbumClient _albumClient;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private readonly UserClient _userClient;

    private int _currentPage = 1;
    private CancellationTokenSource? _playlistBackgroundLoadCts;
    private int _playlistLoadVersion;
    private bool _hasMoreSongs = true;
    private bool _isBackgroundLoading;
    [ObservableProperty]
    public partial bool IsImportingExternalPlaylist { get; set; }

    private bool _isLikePlaylistLocalMode;
    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial bool IsShowingSongs { get; set; }

    private CancellationTokenSource? _refreshPlaylistsCts;
    private bool _isDisposed;
    private readonly List<SongItem> _selectedPlaylistSongsDefaultOrder = new();

    [ObservableProperty]
    public partial string CurrentSortText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnlinePlaylist))]
    public partial PlaylistItem? SelectedPlaylist { get; set; }

    public MyPlaylistsViewModel(
        UserClient userClient,
        PlaylistClient playlistClient,
        AlbumClient albumClient,
        FavoritePlaylistService favoritePlaylistService,
        UserCreatedPlaylistCacheService userCreatedPlaylistCacheService,
        ISukiToastManager toastManager,
        ICreatePlaylistDialogService createPlaylistDialogService,
        IExternalPlaylistImportService externalPlaylistImportService,
        ILogger<MyPlaylistsViewModel> logger)
    {
        _userClient = userClient;
        _playlistClient = playlistClient;
        _albumClient = albumClient;
        _favoritePlaylistService = favoritePlaylistService;
        _userCreatedPlaylistCacheService = userCreatedPlaylistCacheService;
        _toastManager = toastManager;
        _createPlaylistDialogService = createPlaylistDialogService;
        _externalPlaylistImportService = externalPlaylistImportService;
        _logger = logger;
        CurrentSortText = GetSortText(SettingsManager.Settings.UserPlaylistSongSortMode);

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

    public IReadOnlyList<string> SortOptions { get; } = [DefaultSortText, ArtistSortText, AlbumSortText];

    public AvaloniaList<PlaylistItem> Playlists { get; } = new();

    public AvaloniaList<SongItem> SelectedPlaylistSongs { get; } = new();

    public AvaloniaList<PlaylistItem> Items { get; } = new();

    partial void OnCurrentSortTextChanged(string value)
    {
        if (_isDisposed)
            return;

        if (string.IsNullOrWhiteSpace(value))
        {
            CurrentSortText = GetSortText(SettingsManager.Settings.UserPlaylistSongSortMode);
            return;
        }

        SettingsManager.Settings.UserPlaylistSongSortMode = GetSortMode(value);
        SettingsManager.Save();
        ApplySongSort();
    }

    [RelayCommand]
    private void GoBack()
    {
        CancelPlaylistBackgroundLoad();
        IsShowingSongs = false;
        SelectedPlaylist = null;
        _selectedPlaylistSongsDefaultOrder.Clear();
        SelectedPlaylistSongs.Clear();
    }

    [RelayCommand]
    private void BackFromPlaylist()
    {
        CancelPlaylistBackgroundLoad();
        WeakReferenceMessenger.Default.Send(new RequestNavigateBackMessage());
    }

    [RelayCommand]
    private async Task LoadAllPlaylists()
    {
        if (_isDisposed)
            return;

        Items.Clear();

        if (!_userClient.IsLoggedIn())
        {
            _userCreatedPlaylistCacheService.Clear();
            return;
        }

        var onlinePlaylists = await LoadAllOnlinePlaylistsAsync();
        if (_isDisposed)
            return;

        if (onlinePlaylists is not null && onlinePlaylists.Status == 1)
        {
            var orderedPlaylists = UserPlaylistDisplayHelper.OrderForDisplay(onlinePlaylists.Playlists).AsValueEnumerable().ToArray();
            _userCreatedPlaylistCacheService.Update(orderedPlaylists.AsValueEnumerable()
                .Where(item => !string.IsNullOrEmpty(item.ListCreateId) && item.Type == 0)
                .ToArray());

            var onlineItems = new List<PlaylistItem>();
            foreach (var item in orderedPlaylists)
                if (!string.IsNullOrEmpty(item.ListCreateId) || item.IsCollectedAlbum)
                    onlineItems.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.IsCollectedAlbum ? item.AlbumId.ToString() : item.ListCreateId,
                        ListId = item.ListId,
                        Count = item.Count,
                        Type = item.IsCollectedAlbum ? PlaylistType.Album : PlaylistType.Online,
                        UserPlaylistType = item.Type,
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
            _logger.LogError("加载失败,err_code{onlinePlaylists.ErrorCode}" , onlinePlaylists?.Status);
            if (_favoritePlaylistService.TryGetLikePlaylistCache(out var cachedLike))
            {
                Items.Add(cachedLike.Playlist);
                _logger.LogInformation("歌单列表远端失败，已从本地缓存兜底显示“我喜欢”。 source={Source}", cachedLike.Source);
            }
        }
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
        if (_isDisposed)
            return;

        if (item.Type == PlaylistType.AddButton) return;

        CancelPlaylistBackgroundLoad();
        var loadVersion = Interlocked.Increment(ref _playlistLoadVersion);

        SelectedPlaylist = item;
        IsShowingSongs = true;
        _selectedPlaylistSongsDefaultOrder.Clear();
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
                    AddSongsInDefaultOrder(likeCache.Songs);
                    _isLikePlaylistLocalMode = true;
                    _hasMoreSongs = false; // 本地快照阶段禁分页，等待远端接管
                    loadedLocal = true;
                    /*_logger.LogInformation("打开“我喜欢”歌单命中本地缓存: source={Source}, songs={SongCount}, updatedAt={UpdatedAt}",
                        likeCache.Source, likeCache.Songs.Count, likeCache.UpdatedAt);*/
                }

                if (loadedLocal)
                {
                    _ = RefreshLikePlaylistAfterOpenAsync(item, hadLocalSnapshot: true, loadVersion);
                }
                else
                {
                    var loaded = await LoadOnlinePlaylistPageAsync(item, 1, replaceExisting: false, loadVersion);
                    if (loaded)
                        StartPlaylistBackgroundLoad(item, loadVersion);
                }
            }
            else
            {
                var loaded = await LoadOnlinePlaylistPageAsync(item, 1, replaceExisting: false, loadVersion);
                if (loaded)
                    StartPlaylistBackgroundLoad(item, loadVersion);
            }
        }
        else if (item.Type == PlaylistType.Album)
        {
            await LoadAlbumSongsAsync();
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if ((SelectedPlaylist?.Type != PlaylistType.Online && SelectedPlaylist?.Type != PlaylistType.Album) ||
            IsLoadingMore || !_hasMoreSongs ||
            _isLikePlaylistLocalMode ||
            _isBackgroundLoading)
            return;

        _currentPage++;
        if (SelectedPlaylist?.Type == PlaylistType.Album)
            await LoadAlbumSongsAsync();
        else
        {
            var selectedPlaylist = SelectedPlaylist;
            if (selectedPlaylist != null)
                await LoadOnlinePlaylistPageAsync(selectedPlaylist, _currentPage, replaceExisting: false, _playlistLoadVersion);
        }
    }

    private async Task<bool> LoadOnlinePlaylistPageAsync(PlaylistItem playlist, int page, bool replaceExisting, int loadVersion)
    {
        if (_isDisposed)
            return false;

        IsLoadingMore = true;
        try
        {
            var data = await _playlistClient.GetSongsAsync(playlist.Id, page, PlaylistSongPageSize);
            if (!IsCurrentPlaylistLoad(playlist, loadVersion))
                return false;

            if (data is null)
            {
                if (!replaceExisting && page > 1)
                    _currentPage = Math.Max(1, page - 1);

                return false;
            }

            if (data.Status != 1)
            {
                _logger.LogWarning("加载歌单分页失败。 playlistId={PlaylistId}, page={Page}, error={ErrorCode}",
                    playlist.Id, page, data.ErrorCode);
                if (!replaceExisting && page > 1)
                    _currentPage = Math.Max(1, page - 1);

                return false;
            }

            var songs = data.Songs;
            var hasMoreSongs = songs.Count >= PlaylistSongPageSize;

            var songItems = MapPlaylistSongs(songs);
            if (!IsCurrentPlaylistLoad(playlist, loadVersion))
                return false;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentPlaylistLoad(playlist, loadVersion))
                    return;

                if (replaceExisting)
                    _selectedPlaylistSongsDefaultOrder.Clear();

                if (songItems.Count > 0)
                    _selectedPlaylistSongsDefaultOrder.AddRange(songItems);

                ApplySongSort();

                _currentPage = page;
                _hasMoreSongs = hasMoreSongs;
                if (replaceExisting)
                    _isLikePlaylistLocalMode = false;
            });

            return true;
        }
        catch (Exception ex)
        {
            if (!replaceExisting && page > 1)
                _currentPage = Math.Max(1, page - 1);

            _logger.LogWarning(ex, "加载歌单分页异常。 playlistId={PlaylistId}, page={Page}", playlist.Id, page);
            return false;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private List<SongItem> MapPlaylistSongs(IReadOnlyList<PlaylistSong> songs)
    {
        return songs.AsValueEnumerable().Select(s => new SongItem
        {
            Name = s.Name,
            Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.AsValueEnumerable().Select(x => x.Name).ToArray()) : "未知",
            Hash = s.Hash,
            AlbumId = s.AlbumId,
            AlbumName = s.Album?.Name ?? "",
            FileId = s.FileId,
            Singers = s.Singers,
            Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
            DurationSeconds = s.DurationMs / 1000.0
        }).ToList();
    }

    private void StartPlaylistBackgroundLoad(PlaylistItem playlist, int loadVersion)
    {
        if (_isDisposed || playlist.Type != PlaylistType.Online || !IsCurrentPlaylistLoad(playlist, loadVersion) || !_hasMoreSongs)
            return;

        CancelPlaylistBackgroundLoad();

        var cts = new CancellationTokenSource();
        _playlistBackgroundLoadCts = cts;
        _isBackgroundLoading = true;
        _ = CompletePlaylistInBackgroundAsync(playlist, loadVersion, cts);
    }

    private async Task CompletePlaylistInBackgroundAsync(PlaylistItem playlist, int loadVersion, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested && !_isDisposed && IsCurrentPlaylistLoad(playlist, loadVersion) && _hasMoreSongs)
            {
                var nextPage = _currentPage + 1;
                var loaded = await LoadOnlinePlaylistPageAsync(playlist, nextPage, replaceExisting: false, loadVersion);
                if (!loaded)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "后台补全歌单失败。 playlistId={PlaylistId}", playlist.Id);
        }
        finally
        {
            if (ReferenceEquals(_playlistBackgroundLoadCts, cts))
            {
                _playlistBackgroundLoadCts = null;
                _isBackgroundLoading = false;
                cts.Dispose();
            }
        }
    }

    private bool IsCurrentPlaylistLoad(PlaylistItem playlist, int loadVersion)
    {
        return !_isDisposed &&
               IsShowingSongs &&
               _playlistLoadVersion == loadVersion &&
               SelectedPlaylist?.Type == playlist.Type &&
               SelectedPlaylist.Id == playlist.Id &&
               SelectedPlaylist.ListId == playlist.ListId;
    }

    private void CancelPlaylistBackgroundLoad()
    {
        var cts = _playlistBackgroundLoadCts;
        _playlistBackgroundLoadCts = null;
        _isBackgroundLoading = false;

        if (cts == null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
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

            var songItems = songs.AsValueEnumerable().Select(s => new SongItem
            {
                Name = s.Name,
                Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.AsValueEnumerable().Select(x => x.Name).ToArray()) : "未知",
                Hash = s.Hash,
                AlbumId = s.AlbumId,
                AlbumName = s.AlbumInfo.AlbumName,
                Singers = s.Singers,
                Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                DurationSeconds = s.DurationMs / 1000.0
            }).ToList();

            if (songItems.Count > 0)
            {
                _selectedPlaylistSongsDefaultOrder.AddRange(songItems);
                ApplySongSort();
            }
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

            var preview = string.Join("、", importResult.FailedNames.AsValueEnumerable().Take(10).ToArray());
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
        var target = Items.AsValueEnumerable().FirstOrDefault(x =>
            (x.Type == PlaylistType.Online || x.Type == PlaylistType.Album) &&
            (x.ListId == item.ListId || (!string.IsNullOrEmpty(item.Id) && x.Id == item.Id)));

        if (target == null)
            return false;

        Items.Remove(target);
        if (SelectedPlaylist != null &&
            (SelectedPlaylist.ListId == target.ListId || SelectedPlaylist.Id == target.Id))
        {
            CancelPlaylistBackgroundLoad();
            SelectedPlaylist = null;
            _selectedPlaylistSongsDefaultOrder.Clear();
            SelectedPlaylistSongs.Clear();
            IsShowingSongs = false;
            WeakReferenceMessenger.Default.Send(new RequestNavigateBackMessage());
        }

        return true;
    }

    private async Task SchedulePlaylistsRefreshAsync(string reason, int delayMs)
    {
        if (_isDisposed)
            return;

        _refreshPlaylistsCts?.Cancel();
        _refreshPlaylistsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshPlaylistsCts = cts;

        try
        {
            await Task.Delay(delayMs, cts.Token);
            if (cts.IsCancellationRequested || _isDisposed) return;
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
                _selectedPlaylistSongsDefaultOrder.Remove(song);
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

    private async Task RefreshLikePlaylistAfterOpenAsync(PlaylistItem openedItem, bool hadLocalSnapshot, int loadVersion)
    {
        try
        {
            if (_isDisposed || !IsCurrentPlaylistLoad(openedItem, loadVersion))
                return;

            await _favoritePlaylistService.LoadLikeListAsync();
            if (!IsCurrentPlaylistLoad(openedItem, loadVersion))
                return;

            var firstPage = await _playlistClient.GetSongsAsync(openedItem.Id, 1, PlaylistSongPageSize);
            if (_isDisposed || !IsCurrentPlaylistLoad(openedItem, loadVersion))
                return;

            if (firstPage is null || firstPage.Status != 1)
            {
                _logger.LogWarning("打开“我喜欢”后远端接管失败，保留本地列表。 err_code={ErrorCode}, hadLocalSnapshot={HadLocalSnapshot}",
                    firstPage?.ErrorCode, hadLocalSnapshot);
                return;
            }

            var remoteSongs = firstPage.Songs ?? new List<PlaylistSong>();
            var songItems = MapPlaylistSongs(remoteSongs);
            var hasMoreSongs = remoteSongs.Count >= PlaylistSongPageSize;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentPlaylistLoad(openedItem, loadVersion) || SelectedPlaylist == null || !IsLikePlaylist(SelectedPlaylist))
                    return;

                _selectedPlaylistSongsDefaultOrder.Clear();
                _selectedPlaylistSongsDefaultOrder.AddRange(songItems);
                ApplySongSort();
                _currentPage = 1;
                _hasMoreSongs = hasMoreSongs;
                _isLikePlaylistLocalMode = false;
            });

            if (hasMoreSongs)
                StartPlaylistBackgroundLoad(openedItem, loadVersion);

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

    private void AddSongsInDefaultOrder(IEnumerable<SongItem> songs)
    {
        _selectedPlaylistSongsDefaultOrder.AddRange(songs);
        ApplySongSort();
    }

    private void ApplySongSort()
    {
        IEnumerable<SongItem> sortedSongs = GetSortMode(CurrentSortText) switch
        {
            PlaylistSongSortMode.Artist => _selectedPlaylistSongsDefaultOrder
                .AsValueEnumerable().OrderBy(song => song.Singer, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            PlaylistSongSortMode.Album => _selectedPlaylistSongsDefaultOrder
                .AsValueEnumerable().OrderBy(song => song.AlbumName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => _selectedPlaylistSongsDefaultOrder
        };

        SelectedPlaylistSongs.Clear();
        SelectedPlaylistSongs.AddRange(sortedSongs);
    }

    private static PlaylistSongSortMode GetSortMode(string? value)
    {
        return value switch
        {
            ArtistSortText => PlaylistSongSortMode.Artist,
            AlbumSortText => PlaylistSongSortMode.Album,
            _ => PlaylistSongSortMode.Default
        };
    }

    private static string GetSortText(PlaylistSongSortMode mode)
    {
        return mode switch
        {
            PlaylistSongSortMode.Artist => ArtistSortText,
            PlaylistSongSortMode.Album => AlbumSortText,
            _ => DefaultSortText
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CancelPlaylistBackgroundLoad();
        _refreshPlaylistsCts?.Cancel();
        _refreshPlaylistsCts?.Dispose();
        _refreshPlaylistsCts = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

}
