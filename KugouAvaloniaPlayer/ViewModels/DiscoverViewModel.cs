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

public partial class PlaylistTag : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private int _tagId;
    [ObservableProperty] private string _tagName = "";
}

public partial class PlaylistTagGroup : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private int _tagId;
    [ObservableProperty] private string _tagName = "";
    public AvaloniaList<PlaylistTag> Son { get; } = new();
}

public partial class DiscoverPlaylist : ObservableObject
{
    [ObservableProperty] private string _cover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    [ObservableProperty] private string _creatorName = "";
    [ObservableProperty] private string _globalId = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long _playCount;
}

public partial class DiscoverViewModel : PageViewModelBase
{
    private const string DefaultCardCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private readonly DiscoveryClient _discoveryClient;
    private readonly ILogger<DiscoverViewModel> _logger;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private bool _hasMoreSongs = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMoreSongs;

    [ObservableProperty] private bool _isShowingSongs;

    private int _playlistLoadVersion;

    [ObservableProperty] private int _selectedMainIndex;
    [ObservableProperty] private DiscoverPlaylist? _selectedPlaylist;
    [ObservableProperty] private int _selectedSubIndex;
    private int _songPage = 1;
    private bool _suppressSelectionChanged;

    public DiscoverViewModel(
        PlaylistClient playlistClient,
        DiscoveryClient discoveryClient,
        ISukiToastManager toastManager,
        ILogger<DiscoverViewModel> logger)
    {
        _playlistClient = playlistClient;
        _discoveryClient = discoveryClient;
        _toastManager = toastManager;
        _logger = logger;
        _ = LoadTagsAsync();
    }

    public override string DisplayName => "发现";
    public override string Icon => "/Assets/tag-svgrepo-com.svg";

    public AvaloniaList<PlaylistTagGroup> Categories { get; } = new();
    public AvaloniaList<PlaylistTag> CurrentSubCategories { get; } = new();
    public AvaloniaList<DiscoverPlaylist> Playlists { get; } = new();
    public AvaloniaList<SongItem> SelectedPlaylistSongs { get; } = new();

    [RelayCommand]
    public async Task LoadTagsAsync()
    {
        IsLoading = true;
        try
        {
            var tags = await _playlistClient.GetTagsAsync();
            Categories.Clear();
            CurrentSubCategories.Clear();
            Playlists.Clear();

            if (tags == null || tags.Count == 0)
                return;

            foreach (var category in tags)
            {
                var group = new PlaylistTagGroup
                {
                    Index = Categories.Count,
                    TagId = category.TagId,
                    TagName = category.TagName
                };

                var subTags = category.Children
                    .OrderBy(x => x.Sort)
                    .Select((x, idx) => new PlaylistTag
                    {
                        Index = idx,
                        TagId = x.TagId,
                        TagName = x.TagName
                    })
                    .ToList();

                if (subTags.Any())
                    group.Son.AddRange(subTags);

                Categories.Add(group);
            }

            if (Categories.Count == 0)
                return;

            _suppressSelectionChanged = true;
            SelectedMainIndex = 0;
            ResetCurrentSubCategories(0);
            SelectedSubIndex = 0;
            _suppressSelectionChanged = false;

            if (CurrentSubCategories.Count == 0)
                return;

            var defaultTagId = CurrentSubCategories[0].TagId;
            await LoadPlaylistsAsync(defaultTagId);
        }
        catch (Exception ex)
        {
            ShowWarning("加载标签失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedMainIndexChanged(int value)
    {
        if (_suppressSelectionChanged)
            return;
        _ = HandleMainCategoryChanged(value);
    }

    partial void OnSelectedSubIndexChanged(int value)
    {
        if (_suppressSelectionChanged)
            return;
        _ = HandleSubCategoryChanged(value);
    }

    [RelayCommand]
    private void SelectMainCategory(int index)
    {
        if (index < 0 || index >= Categories.Count)
            return;
        SelectedMainIndex = index;
    }

    [RelayCommand]
    private void SelectSubCategory(int index)
    {
        if (index < 0 || index >= CurrentSubCategories.Count)
            return;
        SelectedSubIndex = index;
    }

    private void ResetCurrentSubCategories(int mainIndex)
    {
        CurrentSubCategories.Clear();

        if (mainIndex < 0 || mainIndex >= Categories.Count)
            return;

        if (Categories[mainIndex].Son.Count > 0)
            CurrentSubCategories.AddRange(Categories[mainIndex].Son);
    }

    private async Task HandleMainCategoryChanged(int index)
    {
        if (index < 0 || index >= Categories.Count)
            return;

        ResetCurrentSubCategories(index);

        _suppressSelectionChanged = true;
        SelectedSubIndex = 0;
        _suppressSelectionChanged = false;

        if (CurrentSubCategories.Count == 0)
        {
            Playlists.Clear();
            return;
        }

        await LoadPlaylistsAsync(CurrentSubCategories[0].TagId);
    }

    private async Task HandleSubCategoryChanged(int index)
    {
        if (index < 0 || index >= CurrentSubCategories.Count)
            return;

        await LoadPlaylistsAsync(CurrentSubCategories[index].TagId);
    }

    private async Task LoadPlaylistsAsync(int tagId, int withSong = 0)
    {
        _ = withSong;
        var version = ++_playlistLoadVersion;
        IsLoading = true;
        try
        {
            var result = await _discoveryClient.GetRecommendedPlaylistsAsync(tagId);
            if (version != _playlistLoadVersion)
                return;

            Playlists.Clear();

            if (result?.Playlists == null || result.Playlists.Count == 0)
                return;

            var items = result.Playlists.Select(MapDiscoverPlaylist).ToList();
            Playlists.AddRange(items);
        }
        catch (Exception ex)
        {
            if (version == _playlistLoadVersion)
                ShowWarning("加载推荐歌单失败", ex.Message);
        }
        finally
        {
            if (version == _playlistLoadVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenPlaylist(DiscoverPlaylist? playlist)
    {
        if (playlist is null || string.IsNullOrWhiteSpace(playlist.GlobalId))
            return;

        SelectedPlaylist = playlist;
        IsShowingSongs = true;
        SelectedPlaylistSongs.Clear();

        _songPage = 1;
        _hasMoreSongs = true;
        IsLoadingMoreSongs = false;

        await LoadMoreSongsInternal();
    }

    [RelayCommand]
    private async Task CollectPlaylist()
    {
        if (SelectedPlaylist is null || string.IsNullOrWhiteSpace(SelectedPlaylist.GlobalId))
            return;

        try
        {
            var result = await _playlistClient.CollectPlaylistAsync(SelectedPlaylist.Name, SelectedPlaylist.GlobalId);
            if (result != null)
            {
                WeakReferenceMessenger.Default.Send(new RefreshPlaylistsMessage());
                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("收藏成功")
                    .WithContent($"已将「{SelectedPlaylist.Name}」收藏到我的歌单")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("收藏失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        SelectedPlaylistSongs.Clear();
    }

    [RelayCommand]
    private async Task LoadMoreSongs()
    {
        if (IsLoadingMoreSongs || !_hasMoreSongs || SelectedPlaylist == null)
            return;

        _songPage++;
        await LoadMoreSongsInternal();
    }

    private async Task LoadMoreSongsInternal()
    {
        if (SelectedPlaylist == null)
            return;

        IsLoadingMoreSongs = true;
        try
        {
            var data = await _playlistClient.GetSongsAsync(SelectedPlaylist.GlobalId, _songPage, 100);
            if (data == null)
            {
                _logger.LogWarning("LoadMoreSongs returned null response. playlist={Playlist} page={Page}",
                    SelectedPlaylist.GlobalId, _songPage);
                _songPage--;
                return;
            }

            if (data.Status != 1) _logger.LogError($"Error : {data.ErrorCode}");
            var songs = data.Songs;
            if (songs.Count < 100)
                _hasMoreSongs = false;

            var songItems = songs.Select(s => new SongItem
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

            if (songItems.Count > 0)
                SelectedPlaylistSongs.AddRange(songItems);
        }
        catch
        {
            _songPage--;
        }
        finally
        {
            IsLoadingMoreSongs = false;
        }
    }

    private DiscoverPlaylist MapDiscoverPlaylist(RecommendPlaylistItem item)
    {
        return new DiscoverPlaylist
        {
            Id = item.ListId.ToString(),
            GlobalId = item.GlobalId,
            Name = item.Name,
            Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultCardCover : item.Cover!,
            PlayCount = item.PlayCount,
            CreatorName = item.CreatorName
        };
    }

    private void ShowWarning(string title, string content)
    {
        _toastManager.CreateToast()
            .OfType(NotificationType.Warning)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }
}