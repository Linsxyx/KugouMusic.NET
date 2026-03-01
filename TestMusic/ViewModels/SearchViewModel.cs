using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.Extensions.Logging;

namespace TestMusic.ViewModels;

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
    PlayerViewModel player, 
    MusicClient musicClient,
    PlaylistClient playlistClient,
    AlbumClient albumClient,
    ILogger<SearchViewModel> logger) : PageViewModelBase
{
    private readonly PlayerViewModel _player = player;
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    [ObservableProperty] private SearchType _currentSearchType = SearchType.Song;
    [ObservableProperty] private string? _detailCover;
    [ObservableProperty] private string? _detailTitle;
    [ObservableProperty] private string? _detailSubTitle; 
    
    [ObservableProperty] private bool _isSearching;
    
    [ObservableProperty] private bool _isShowingDetail;
    [ObservableProperty] private string _searchKeyword = "";
    
    private int _currentDetailPage = 1;
    private bool _hasMoreDetails = true;
    private string _currentDetailId = "";
    private DetailType _currentDetailType = DetailType.None;
    [ObservableProperty] private bool _isLoadingMoreDetails;

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
        if (results != null) foreach (var item in results) Playlists.Add(item);
    }

    private async Task SearchAlbums()
    {
        var results = await musicClient.SearchAlbumAsync(SearchKeyword);
        if (results != null) foreach (var item in results) Albums.Add(item);
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
    private async Task OpenPlaylist(SearchPlaylistItem item)
    {
        if (item == null) return;

        // 初始化状态
        _currentDetailType = DetailType.Playlist;
        _currentDetailId = item.GlobalId;
        _currentDetailPage = 1;
        _hasMoreDetails = true;
        
        DetailTitle = item.Name;
        DetailSubTitle = $"{item.SongCount} 首歌曲 - {item.CreatorName}";
        DetailCover = item.Cover ?? DefaultCover;
        
        IsShowingDetail = true;
        DetailSongs.Clear();

        await LoadMoreDetailsInternal();
    }

    [RelayCommand]
    private async Task OpenAlbum(SearchAlbumItem item)
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

                foreach (var s in songs)
                {
                    var singerName = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知";
                    DetailSongs.Add(new SongItem
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
            else if (_currentDetailType == DetailType.Album)
            {
                var songs = await albumClient.GetSongsAsync(_currentDetailId, _currentDetailPage, 30);
                
                if (songs == null || songs.Count < 30) _hasMoreDetails = false;

                if (songs != null)
                {
                    foreach (var s in songs)
                    {
                        var singerName = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知";
                        DetailSongs.Add(new SongItem
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
}