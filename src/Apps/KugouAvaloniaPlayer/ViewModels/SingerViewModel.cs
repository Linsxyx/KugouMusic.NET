using System;
using System.Collections.Generic;
using ZLinq;
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

public partial class SingerViewModel : PageViewModelBase
{
    private const string HotSortText = "热门";
    private const string NewSortText = "最新";
    private const string AlbumsText = "专辑";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const string DefaultCardCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";

    private readonly string _authorId;
    private readonly ILogger<SingerViewModel> _logger;
    private readonly ArtistClient _artistClient;
    private readonly AlbumClient _albumClient;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;

    private int _currentPage = 1;
    private int _currentAlbumPage = 1;
    private int _currentAlbumDetailPage = 1;
    private bool _hasMoreAlbums = true;
    private bool _hasMoreAlbumSongs = true;
    private long _currentAlbumId;
    private long _currentAlbumAuthorId;

    [ObservableProperty]
    public partial string CurrentSortText { get; set; } = HotSortText;

    private bool _hasMoreSongs = true;

    // 最新/热门切换
    [ObservableProperty]
    public partial bool IsHotSort { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingAlbums { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMoreAlbums { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingAlbumSongs { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMoreAlbumSongs { get; set; }

    [ObservableProperty]
    public partial bool IsAlbumBrowserVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAlbumDetailVisible { get; set; }

    [ObservableProperty]
    public partial string SingerAvatar { get; set; }

    [ObservableProperty]
    public partial string SingerName { get; set; }

    [ObservableProperty]
    public partial string? AlbumDetailCover { get; set; }

    [ObservableProperty]
    public partial string? AlbumDetailTitle { get; set; }

    [ObservableProperty]
    public partial string? AlbumDetailSubTitle { get; set; }

    public SingerViewModel(
        ArtistClient artistClient,
        AlbumClient albumClient,
        PlaylistClient playlistClient,
        ISukiToastManager toastManager,
        ILogger<SingerViewModel> logger,
        string authorId,
        string singerName)
    {
        _artistClient = artistClient;
        _albumClient = albumClient;
        _playlistClient = playlistClient;
        _toastManager = toastManager;
        _logger = logger;
        _authorId = authorId;
        SingerName = singerName;
        SingerAvatar = "avares://KugouAvaloniaPlayer/Assets/default_singer.png";
        _ = LoadSongsAsync();
    }

    public override string DisplayName => "歌手详情";
    public override string Icon => "avares://KugouAvaloniaPlayer/Assets/default_singer.png";

    public IReadOnlyList<string> SortOptions { get; } = [HotSortText, NewSortText, AlbumsText];

    public AvaloniaList<SongItem> Songs { get; } = new();
    public AvaloniaList<SingerAlbumItem> Albums { get; } = new();
    public AvaloniaList<SongItem> AlbumSongs { get; } = new();

    public bool IsSongListVisible => !IsAlbumBrowserVisible && !IsAlbumDetailVisible;

    partial void OnCurrentSortTextChanged(string value)
    {
        if (value == AlbumsText)
        {
            IsAlbumBrowserVisible = true;
            IsAlbumDetailVisible = false;
            OnPropertyChanged(nameof(IsSongListVisible));
            if (Albums.Count == 0)
                _ = LoadAlbumsAsync();
            return;
        }

        IsAlbumBrowserVisible = false;
        IsAlbumDetailVisible = false;
        OnPropertyChanged(nameof(IsSongListVisible));
        IsHotSort = value != NewSortText;
        _ = LoadSongsAsync();
    }

    partial void OnIsAlbumBrowserVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSongListVisible));
    }

    partial void OnIsAlbumDetailVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSongListVisible));
    }

    private async Task LoadSongsAsync()
    {
        IsLoading = true;

        try
        {
            var json = await _artistClient.GetDetailAsync(_authorId);

            if (json != null && json.Status == 1)
                SingerAvatar = string.IsNullOrWhiteSpace(json.Cover)
                    ? Icon
                    : json.Cover;

            Songs.Clear();
            _hasMoreSongs = true;

            var firstPage = 1;

            var success = await LoadMoreSongsInternal(firstPage);

            if (success)
                _currentPage = firstPage;
        }
        finally
        {
            // 确保无论如何最后取消加载状态
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (IsLoadingMore || IsLoading || !_hasMoreSongs)
            return;

        var nextPage = _currentPage + 1;

        var success = await LoadMoreSongsInternal(nextPage);

        if (success)
            _currentPage = nextPage;
    }

    private async Task<bool> LoadMoreSongsInternal(int page)
    {
        IsLoadingMore = true;
        try
        {
            var sort = IsHotSort ? "hot" : "new";
            var result = await _artistClient.GetAudiosAsync(
                _authorId, page, 100, sort);

            if (result?.Songs == null)
                return false;

            if (result.Songs.Count < 100)
                _hasMoreSongs = false;

            var songItems = result.Songs
                .AsValueEnumerable().Where(item => !string.IsNullOrEmpty(item.Hash))
                .Select(item => new SongItem
                {
                    Name = item.Name,
                    Singer = item.SingerName,
                    Hash = item.Hash,
                    AlbumId = item.AlbumId.ToString(),
                    AlbumName = item.AlbumName,
                    DurationSeconds = item.Duration / 1000.0,
                    Cover = item.TransParam?.UnionCover
                })
                .ToList();

            if (songItems.AsValueEnumerable().Any())
                Songs.AddRange(songItems);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌手歌曲失败，authorId={AuthorId}, page={Page}", _authorId, page);
            return false;
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
        }
    }


    [RelayCommand]
    private void ToggleSort()
    {
        CurrentSortText = IsHotSort ? NewSortText : HotSortText;
    }

    [RelayCommand]
    private async Task LoadMoreAlbums()
    {
        if (IsLoadingAlbums || IsLoadingMoreAlbums || !_hasMoreAlbums)
            return;

        var nextPage = _currentAlbumPage + 1;
        var success = await LoadMoreAlbumsInternal(nextPage);
        if (success)
            _currentAlbumPage = nextPage;
    }

    private async Task LoadAlbumsAsync()
    {
        IsLoadingAlbums = true;

        try
        {
            Albums.Clear();
            _currentAlbumPage = 1;
            _hasMoreAlbums = true;

            await LoadMoreAlbumsInternal(_currentAlbumPage);
        }
        finally
        {
            IsLoadingAlbums = false;
        }
    }

    private async Task<bool> LoadMoreAlbumsInternal(int page)
    {
        IsLoadingMoreAlbums = true;
        try
        {
            var result = await _artistClient.GetAlbumsAsync(_authorId, page, 50, "new");
            if (result?.Albums == null)
                return false;

            if (result.Albums.Count < 50)
                _hasMoreAlbums = false;

            var albums = result.Albums
                .AsValueEnumerable().Where(x => x.AlbumId > 0 && !string.IsNullOrWhiteSpace(x.AlbumName))
                .Select(ToSingerAlbumItem)
                .ToList();

            if (albums.Count > 0)
                Albums.AddRange(albums);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌手专辑失败，authorId={AuthorId}, page={Page}", _authorId, page);
            return false;
        }
        finally
        {
            IsLoadingMoreAlbums = false;
        }
    }

    [RelayCommand]
    private async Task OpenAlbum(SingerAlbumItem? item)
    {
        if (item == null)
            return;

        _currentAlbumId = item.AlbumId;
        _currentAlbumAuthorId = item.AuthorId;
        if (_currentAlbumAuthorId <= 0 && long.TryParse(_authorId, out var authorId))
            _currentAlbumAuthorId = authorId;

        _currentAlbumDetailPage = 1;
        _hasMoreAlbumSongs = true;
        AlbumSongs.Clear();

        AlbumDetailTitle = item.AlbumName;
        AlbumDetailSubTitle = item.Subtitle;
        AlbumDetailCover = item.Cover;

        IsAlbumBrowserVisible = false;
        IsAlbumDetailVisible = true;
        IsLoadingAlbumSongs = true;

        try
        {
            await LoadMoreAlbumSongsInternal();
        }
        finally
        {
            IsLoadingAlbumSongs = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAlbumSongs()
    {
        if (IsLoadingAlbumSongs || IsLoadingMoreAlbumSongs || !_hasMoreAlbumSongs || _currentAlbumId <= 0)
            return;

        _currentAlbumDetailPage++;
        await LoadMoreAlbumSongsInternal();
    }

    private async Task LoadMoreAlbumSongsInternal()
    {
        IsLoadingMoreAlbumSongs = true;
        try
        {
            var songs = await _albumClient.GetSongsAsync(_currentAlbumId.ToString(), _currentAlbumDetailPage, 50);
            if (songs == null || songs.Count < 50)
                _hasMoreAlbumSongs = false;

            if (songs == null)
                return;

            var songItems = songs.AsValueEnumerable().Select(s =>
            {
                var singerName = s.Singers.Count > 0
                    ? string.Join("、", s.Singers.AsValueEnumerable().Select(x => x.Name).ToArray())
                    : SingerName;

                return new SongItem
                {
                    Name = s.Name,
                    Singer = singerName,
                    Hash = s.Hash,
                    AlbumId = s.AlbumId,
                    AlbumName = s.AlbumInfo.AlbumName,
                    Singers = s.Singers,
                    Cover = string.IsNullOrWhiteSpace(s.Cover) ? DefaultSongCover : s.Cover,
                    DurationSeconds = s.DurationMs / 1000.0
                };
            }).Where(x => !string.IsNullOrWhiteSpace(x.Hash)).ToList();

            if (songItems.Count <= 0)
                return;

            if (_currentAlbumAuthorId <= 0)
            {
                var authorId = songItems
                    .AsValueEnumerable().SelectMany(x => x.Singers)
                    .Select(x => x.Id)
                    .FirstOrDefault(x => x > 0);
                if (authorId > 0)
                    _currentAlbumAuthorId = authorId;
            }

            AlbumSongs.AddRange(songItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载歌手专辑详情失败，albumId={AlbumId}, page={Page}",
                _currentAlbumId, _currentAlbumDetailPage);
            if (_currentAlbumDetailPage > 1)
                _currentAlbumDetailPage--;
        }
        finally
        {
            IsLoadingMoreAlbumSongs = false;
        }
    }

    [RelayCommand]
    private void BackToAlbums()
    {
        IsAlbumDetailVisible = false;
        IsAlbumBrowserVisible = true;
        AlbumSongs.Clear();
    }

    [RelayCommand]
    private async Task CollectAlbum()
    {
        if (_currentAlbumId <= 0 || string.IsNullOrWhiteSpace(AlbumDetailTitle))
            return;

        try
        {
            var result = await _playlistClient.CollectAlbumAsync(
                AlbumDetailTitle,
                _currentAlbumId,
                _currentAlbumAuthorId);

            if (result == null)
                return;

            WeakReferenceMessenger.Default.Send(new RefreshPlaylistsMessage());
            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("收藏成功")
                .WithContent($"已将专辑「{AlbumDetailTitle}」收藏到我的歌单")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "收藏歌手专辑失败，albumId={AlbumId}", _currentAlbumId);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("收藏失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    private static SingerAlbumItem ToSingerAlbumItem(ArtistAlbumItem item)
    {
        var author = ResolveAlbumAuthorName(item);
        var authorId = item.Authors.AsValueEnumerable().Select(x => x.AuthorId).FirstOrDefault(x => x > 0);
        return new SingerAlbumItem(
            item.AlbumId,
            item.AlbumName,
            author,
            ResolveAlbumCover(item),
            BuildAlbumSubtitle(item, author),
            authorId);
    }

    private static string ResolveAlbumCover(ArtistAlbumItem item)
    {
        var cover = string.IsNullOrWhiteSpace(item.SizableCover) ? item.Cover : item.SizableCover;
        if (string.IsNullOrWhiteSpace(cover))
            return DefaultCardCover;

        return cover.Replace("{size}", "400");
    }

    private static string ResolveAlbumAuthorName(ArtistAlbumItem item)
    {
        return string.IsNullOrWhiteSpace(item.AuthorName)
            ? string.Join("、", item.Authors.AsValueEnumerable().Select(x => x.AuthorName).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray())
            : item.AuthorName;
    }

    private static string BuildAlbumSubtitle(ArtistAlbumItem item, string author)
    {
        if (!string.IsNullOrWhiteSpace(item.PublishDate) && !string.IsNullOrWhiteSpace(author))
            return $"{author} - {item.PublishDate}";

        if (!string.IsNullOrWhiteSpace(item.PublishDate))
            return item.PublishDate;

        return author;
    }
}

public sealed record SingerAlbumItem(
    long AlbumId,
    string AlbumName,
    string AuthorName,
    string Cover,
    string Subtitle,
    long AuthorId);
