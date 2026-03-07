using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public enum SearchType
{
    Song,
    Playlist,
    Album
}

public enum DetailType
{
    None,
    Playlist,
    Album
}

public partial class SearchViewModel(
    MusicClient musicClient,
    PlaylistClient playlistClient,
    AlbumClient albumClient,
    ISukiToastManager toastManager,
    ILogger<SearchViewModel> logger) : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private string _currentDetailId = "";

    private int _currentDetailPage = 1;
    private DetailType _currentDetailType = DetailType.None;

    // 用于收藏歌单的信息
    private string _currentPlaylistGlobalId = "";
    private string _currentPlaylistName = "";
    [ObservableProperty] private SearchType _currentSearchType = SearchType.Song;
    [ObservableProperty] private string? _detailCover;
    [ObservableProperty] private string? _detailSubTitle;
    [ObservableProperty] private string? _detailTitle;
    private bool _hasMoreDetails = true;
    [ObservableProperty] private bool _isLoadingMoreDetails;

    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isShowingDetail;
    [ObservableProperty] private string _searchKeyword = "";

    public override string DisplayName => "搜索";
    public override string Icon => "/Assets/Search.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();
    public AvaloniaList<SearchPlaylistItem> Playlists { get; } = new();
    public AvaloniaList<SearchAlbumItem> Albums { get; } = new();
    public AvaloniaList<SongItem> DetailSongs { get; } = new();

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        IsShowingDetail = false;

        IsSearching = true;
        logger.LogInformation("正在搜索: {Keyword}, 类型: {Type}", SearchKeyword, CurrentSearchType);

        ClearResults();

        try
        {
            switch (CurrentSearchType)
            {
                case SearchType.Song:
                    await SearchSongs();
                    break;
                case SearchType.Playlist:
                    await SearchPlaylists();
                    break;
                case SearchType.Album:
                    await SearchAlbums();
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索失败");
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void ClearResults()
    {
        Songs.Clear();
        Playlists.Clear();
        Albums.Clear();
    }

    private async Task SearchSongs()
    {
        var results = await musicClient.SearchAsync(SearchKeyword);
        foreach (var item in results)
            Songs.Add(ConvertSong(item));
    }

    private async Task SearchPlaylists()
    {
        var results = await musicClient.SearchSpecialAsync(SearchKeyword);
        if (results != null)
            foreach (var item in results)
                Playlists.Add(item);
    }

    private async Task SearchAlbums()
    {
        var results = await musicClient.SearchAlbumAsync(SearchKeyword);
        if (results != null)
            foreach (var item in results)
                Albums.Add(item);
    }

    [RelayCommand]
    private void SwitchSearchType(string type)
    {
        if (Enum.TryParse<SearchType>(type, out var searchType))
        {
            CurrentSearchType = searchType;
            IsShowingDetail = false;
            ClearResults();
            if (!string.IsNullOrWhiteSpace(SearchKeyword)) _ = Search();
        }
    }

    [RelayCommand]
    private async Task OpenPlaylist(SearchPlaylistItem? item)
    {
        if (item == null) return;

        // 初始化状态
        _currentDetailType = DetailType.Playlist;
        _currentDetailId = item.GlobalId;
        _currentDetailPage = 1;
        _hasMoreDetails = true;

        // 保存收藏歌单所需的信息
        _currentPlaylistGlobalId = item.GlobalId;
        _currentPlaylistName = item.Name;

        DetailTitle = item.Name;
        DetailSubTitle = $"{item.SongCount} 首歌曲 - {item.CreatorName}";
        DetailCover = item.Cover ?? DefaultCover;

        IsShowingDetail = true;
        DetailSongs.Clear();

        await LoadMoreDetailsInternal();
    }

    [RelayCommand]
    private async Task OpenAlbum(SearchAlbumItem? item)
    {
        if (item == null) return;

        _currentDetailType = DetailType.Album;
        _currentDetailId = item.AlbumId.ToString();
        _currentDetailPage = 1;
        _hasMoreDetails = true;

        DetailTitle = item.Name;
        DetailSubTitle = $"{item.SingerName}";
        DetailCover = item.Cover ?? DefaultCover;

        IsShowingDetail = true;
        DetailSongs.Clear();

        await LoadMoreDetailsInternal();
    }

    [RelayCommand]
    private async Task LoadMoreDetails()
    {
        if (IsLoadingMoreDetails || !_hasMoreDetails || !IsShowingDetail) return;

        _currentDetailPage++;
        await LoadMoreDetailsInternal();
    }

    private async Task LoadMoreDetailsInternal()
    {
        IsLoadingMoreDetails = true;
        try
        {
            if (_currentDetailType == DetailType.Playlist)
            {
                var songs = await playlistClient.GetSongsAsync(_currentDetailId, _currentDetailPage, 100);
                if (songs.Count < 100) _hasMoreDetails = false;

                var songItems = songs.Select(s =>
                {
                    var singerName = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知";
                    return new SongItem
                    {
                        Name = s.Name,
                        Singer = singerName,
                        Hash = s.Hash,
                        AlbumId = s.AlbumId,
                        Singers = s.Singers,
                        Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultCover : s.Cover,
                        DurationSeconds = s.DurationMs / 1000.0
                    };
                }).ToList();

                if (songItems.Any())
                    DetailSongs.AddRange(songItems);
            }
            else if (_currentDetailType == DetailType.Album)
            {
                var songs = await albumClient.GetSongsAsync(_currentDetailId, _currentDetailPage);

                if (songs == null || songs.Count < 30) _hasMoreDetails = false;

                if (songs != null)
                {
                    var songItems = songs.Select(s =>
                    {
                        var singerName = s.Singers.Count > 0
                            ? string.Join("、", s.Singers.Select(x => x.Name))
                            : "未知";

                        return new SongItem
                        {
                            Name = s.Name,
                            Singer = singerName,
                            Hash = s.Hash,
                            AlbumId = s.AlbumId,
                            Singers = s.Singers,
                            Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultCover : s.Cover,
                            DurationSeconds = s.DurationMs / 1000.0
                        };
                    }).ToList();

                    if (songItems.Any())
                        DetailSongs.AddRange(songItems);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载详情失败");
            if (_currentDetailPage > 1) _currentDetailPage--;
        }
        finally
        {
            IsLoadingMoreDetails = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        IsShowingDetail = false;
        DetailSongs.Clear();
    }

    public async Task SearchAsync(string keyword)
    {
        SearchKeyword = keyword;
        await Search();
    }

    private SongItem ConvertSong(SongInfo item)
    {
        return new SongItem
        {
            Name = item.Name,
            Singer = item.Singer,
            Hash = item.Hash,
            Singers = item.Singers,
            Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
            DurationSeconds = item.Duration
        };
    }

    [RelayCommand]
    private async Task CollectPlaylist()
    {
        if (string.IsNullOrEmpty(_currentPlaylistGlobalId) || _currentDetailType != DetailType.Playlist)
            return;

        try
        {
            var result = await playlistClient.CollectPlaylistAsync(_currentPlaylistName, _currentPlaylistGlobalId);
            if (result != null)
                toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("收藏成功")
                    .WithContent($"已将「{_currentPlaylistName}」收藏到我的歌单")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "收藏歌单失败");
            toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("收藏失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }
}