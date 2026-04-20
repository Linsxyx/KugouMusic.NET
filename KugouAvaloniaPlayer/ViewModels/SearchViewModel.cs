using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SearchViewModel(
    MusicClient musicClient,
    PlaylistClient playlistClient,
    AlbumClient albumClient,
    ISukiToastManager toastManager,
    ILogger<SearchViewModel> logger) : PageViewModelBase
{
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string DefaultCardCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
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
    [ObservableProperty] private bool _hasSearched;
    [ObservableProperty] private bool _isLoadingHot;
    [ObservableProperty] private bool _isLoadingMoreDetails;

    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isShowingDetail;
    [ObservableProperty] private string _searchKeyword = "";
    [ObservableProperty] private int _selectedHotMainIndex;
    private bool _suppressHotSelectionChanged;

    public override string DisplayName => "搜索";
    public override string Icon => "/Assets/Search.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();
    public AvaloniaList<SearchPlaylistItem> Playlists { get; } = new();
    public AvaloniaList<SearchAlbumItem> Albums { get; } = new();
    public AvaloniaList<SongItem> DetailSongs { get; } = new();
    public AvaloniaList<SearchHotTagGroup> HotCategories { get; } = new();
    public AvaloniaList<SearchHotTagItem> CurrentHotKeywords { get; } = new();

    // 当前是否显示歌单详情（用于控制收藏按钮可见性）
    public bool IsPlaylistDetail => _currentDetailType == DetailType.Playlist;

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        HasSearched = true;
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

    [RelayCommand]
    private async Task LoadSearchHot()
    {
        if (IsLoadingHot) return;
        if (HotCategories.Count > 0) return;

        IsLoadingHot = true;
        try
        {
            var response = await musicClient.GetSearchHotAsync();
            HotCategories.Clear();
            CurrentHotKeywords.Clear();

            if (response?.Categories == null || response.Categories.Count == 0)
                return;

            var groups = response.Categories
                .Select((category, groupIndex) => new SearchHotTagGroup
                {
                    Index = groupIndex,
                    Name = category.Name
                })
                .ToList();

            for (var i = 0; i < groups.Count; i++)
            {
                var keywords = response.Categories[i].Keywords
                    .Where(k => !string.IsNullOrWhiteSpace(k.Keyword))
                    .Select((k, keywordIndex) => new SearchHotTagItem
                    {
                        Index = keywordIndex,
                        Keyword = k.Keyword,
                        Reason = k.Reason
                    })
                    .ToList();
                if (keywords.Count > 0)
                    groups[i].Keywords.AddRange(keywords);
            }

            if (groups.Count == 0)
                return;

            HotCategories.AddRange(groups);

            _suppressHotSelectionChanged = true;
            SelectedHotMainIndex = 0;
            ResetCurrentHotKeywords(0);
            _suppressHotSelectionChanged = false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载热搜失败");
        }
        finally
        {
            IsLoadingHot = false;
        }
    }

    [RelayCommand]
    private void SelectHotMainCategory(int index)
    {
        if (index < 0 || index >= HotCategories.Count)
            return;

        SelectedHotMainIndex = index;
    }

    partial void OnSelectedHotMainIndexChanged(int value)
    {
        if (_suppressHotSelectionChanged)
            return;

        ResetCurrentHotKeywords(value);
    }

    private void ResetCurrentHotKeywords(int mainIndex)
    {
        CurrentHotKeywords.Clear();
        if (mainIndex < 0 || mainIndex >= HotCategories.Count)
            return;

        if (HotCategories[mainIndex].Keywords.Count > 0)
            CurrentHotKeywords.AddRange(HotCategories[mainIndex].Keywords);
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
    private void ClearSearch()
    {
        SearchKeyword = string.Empty;
        IsSearching = false;
        IsShowingDetail = false;
        HasSearched = false;

        _currentDetailType = DetailType.None;
        _currentDetailId = string.Empty;
        _currentDetailPage = 1;
        _hasMoreDetails = true;
        DetailTitle = null;
        DetailSubTitle = null;
        DetailCover = null;
        _currentPlaylistGlobalId = string.Empty;
        _currentPlaylistName = string.Empty;

        ClearResults();
        DetailSongs.Clear();
        OnPropertyChanged(nameof(IsPlaylistDetail));
    }

    [RelayCommand]
    private async Task QuickSearchHotKeyword(SearchHotTagItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Keyword))
            return;

        SearchKeyword = item.Keyword;
        await Search();
    }

    [RelayCommand]
    private async Task OpenPlaylist(SearchPlaylistItem? item)
    {
        if (item == null) return;

        // 初始化状态
        _currentDetailType = DetailType.Playlist;
        OnPropertyChanged(nameof(IsPlaylistDetail));
        _currentDetailId = item.GlobalId;
        _currentDetailPage = 1;
        _hasMoreDetails = true;

        // 保存收藏歌单所需的信息
        _currentPlaylistGlobalId = item.GlobalId;
        _currentPlaylistName = item.Name;

        DetailTitle = item.Name;
        DetailSubTitle = $"{item.SongCount} 首歌曲 - {item.CreatorName}";
        DetailCover = item.Cover ?? DefaultCardCover;

        IsShowingDetail = true;
        DetailSongs.Clear();

        await LoadMoreDetailsInternal();
    }

    [RelayCommand]
    private async Task OpenAlbum(SearchAlbumItem? item)
    {
        if (item == null) return;

        _currentDetailType = DetailType.Album;
        OnPropertyChanged(nameof(IsPlaylistDetail));
        _currentDetailId = item.AlbumId.ToString();
        _currentDetailPage = 1;
        _hasMoreDetails = true;

        DetailTitle = item.Name;
        DetailSubTitle = $"{item.SingerName}";
        DetailCover = item.Cover ?? DefaultCardCover;

        IsShowingDetail = true;
        DetailSongs.Clear();

        await LoadMoreDetailsInternal();
    }

    [RelayCommand]
    private async Task LoadMoreDetails()
    {
        if (IsLoadingMoreDetails || !_hasMoreDetails || !IsShowingDetail)
            return;


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
                var data = await playlistClient.GetSongsAsync(_currentDetailId, _currentDetailPage, 100);
                if (data == null)
                {
                    logger.LogWarning("Playlist detail response is null. detailId={DetailId} page={Page}",
                        _currentDetailId, _currentDetailPage);
                    if (_currentDetailPage > 1) _currentDetailPage--;
                    return;
                }

                if (data.Status != 1) logger.LogError($"Error : {data.ErrorCode}");
                var songs = data.Songs;
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
                        Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                        DurationSeconds = s.DurationMs / 1000.0
                    };
                }).ToList();

                if (songItems.Any())
                    DetailSongs.AddRange(songItems);
            }
            else if (_currentDetailType == DetailType.Album)
            {
                var songs = await albumClient.GetSongsAsync(_currentDetailId, _currentDetailPage, 50);

                if (songs == null || songs.Count < 50) _hasMoreDetails = false;

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
                            Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
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
            Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultSongCover : item.Cover,
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
            {
                WeakReferenceMessenger.Default.Send(new RefreshPlaylistsMessage());
                toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("收藏成功")
                    .WithContent($"已将「{_currentPlaylistName}」收藏到我的歌单")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            }
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