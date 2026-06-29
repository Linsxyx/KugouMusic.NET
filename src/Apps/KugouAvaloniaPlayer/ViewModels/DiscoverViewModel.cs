using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using ZLinq;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DiscoverTopCard : ObservableObject
{
    [ObservableProperty]
    public partial int CardId { get; set; }

    [ObservableProperty]
    public partial string Cover { get; set; } = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string SongSummary { get; set; } = "";

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    public AvaloniaList<SongItem> Songs { get; } = [];
}

public partial class DiscoverPlaylist : ObservableObject
{
    [ObservableProperty]
    public partial string Cover { get; set; } = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";

    [ObservableProperty]
    public partial string CreatorName { get; set; } = "";

    [ObservableProperty]
    public partial string GlobalId { get; set; } = "";

    [ObservableProperty]
    public partial string Id { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial long PlayCount { get; set; }

    public string PlayCountText => FormatPlayCount(PlayCount);

    partial void OnPlayCountChanged(long value)
    {
        OnPropertyChanged(nameof(PlayCountText));
    }

    private static string FormatPlayCount(long value)
    {
        return value switch
        {
            >= 100_000_000 => $"{value / 100_000_000d:0.#}亿",
            >= 10_000 => $"{value / 10_000d:0.#}万",
            > 0 => value.ToString("N0", CultureInfo.CurrentCulture),
            _ => ""
        };
    }
}

public partial class DiscoverViewModel : PageViewModelBase
{
    private const string DefaultCardCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const int PlaylistPageSize = 30;
    private const int PlaylistSongPageSize = 200;

    private static readonly DiscoverTopCardDefinition[] TopCardDefinitions =
    [
        new(1, "私人专属好歌"),
        new(2, "经典怀旧金曲"),
        new(3, "热门好歌精选"),
        new(4, "小众宝藏佳作"),
        new(5, "潮流尝鲜"),
        new(6, "VIP 专属推荐")
    ];

    private readonly RecommendClient _discoveryClient;
    private readonly IDiscoverTagViewModelFactory _discoverTagViewModelFactory;
    private readonly ILogger<DiscoverViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly PlaylistClient _playlistClient;
    private readonly ISukiToastManager _toastManager;
    private DetailSource _detailSource = DetailSource.None;
    private bool _hasMoreSongs = true;
    private int _playlistLoadVersion;
    private int _songPage = 1;

    [ObservableProperty]
    public partial string DetailCover { get; set; } = DefaultCardCover;

    [ObservableProperty]
    public partial string DetailSubtitle { get; set; } = "";

    [ObservableProperty]
    public partial string DetailTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMoreSongs { get; set; }

    [ObservableProperty]
    public partial bool IsPlaylistsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsShowingSongs { get; set; }

    [ObservableProperty]
    public partial bool IsTopCardsLoading { get; set; }

    [ObservableProperty]
    public partial DiscoverPlaylist? SelectedPlaylist { get; set; }

    [ObservableProperty]
    public partial DiscoverTopCard? SelectedTopCard { get; set; }

    public DiscoverViewModel(
        PlaylistClient playlistClient,
        RecommendClient discoveryClient,
        IDiscoverTagViewModelFactory discoverTagViewModelFactory,
        INavigationService navigationService,
        ISukiToastManager toastManager,
        ILogger<DiscoverViewModel> logger)
    {
        _playlistClient = playlistClient;
        _discoveryClient = discoveryClient;
        _discoverTagViewModelFactory = discoverTagViewModelFactory;
        _navigationService = navigationService;
        _toastManager = toastManager;
        _logger = logger;
        _ = LoadContentAsync();
    }

    public override string DisplayName => "探索发现";
    public override string Icon => "/Assets/tag-svgrepo-com.svg";

    public bool CanCollectSelectedPlaylist => SelectedPlaylist is not null;
    public AvaloniaList<DiscoverPlaylist> Playlists { get; } = [];
    public AvaloniaList<SongItem> SelectedDetailSongs { get; } = [];
    public AvaloniaList<SongItem> SelectedPlaylistSongs => SelectedDetailSongs;
    public AvaloniaList<DiscoverTopCard> TopCards { get; } = [];

    partial void OnSelectedPlaylistChanged(DiscoverPlaylist? value)
    {
        OnPropertyChanged(nameof(CanCollectSelectedPlaylist));
    }

    public async Task LoadContentAsync()
    {
        var version = ++_playlistLoadVersion;
        IsLoading = true;

        try
        {
            await Task.WhenAll(LoadTopCardsAsync(), LoadRecommendedPlaylistsAsync(version));
        }
        finally
        {
            if (version == _playlistLoadVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDiscovery()
    {
        await LoadContentAsync();
    }

    [RelayCommand]
    private void OpenPlaylistTags()
    {
        _navigationService.NavigateTransient(_discoverTagViewModelFactory.Create());
    }

    [RelayCommand]
    private void OpenTopCard(DiscoverTopCard? card)
    {
        if (card == null || card.Songs.Count == 0)
            return;

        _detailSource = DetailSource.TopCard;
        SelectedTopCard = card;
        SelectedPlaylist = null;
        DetailTitle = card.Title;
        DetailSubtitle = string.IsNullOrWhiteSpace(card.Description)
            ? $"{card.Songs.Count} 首歌曲"
            : card.Description;
        DetailCover = string.IsNullOrWhiteSpace(card.Cover) ? DefaultCardCover : card.Cover;

        SelectedDetailSongs.Clear();
        SelectedDetailSongs.AddRange(card.Songs);
        IsShowingSongs = true;
        _hasMoreSongs = false;
        IsLoadingMoreSongs = false;
    }

    [RelayCommand]
    private async Task OpenPlaylist(DiscoverPlaylist? playlist)
    {
        if (playlist is null || string.IsNullOrWhiteSpace(playlist.GlobalId))
            return;

        _detailSource = DetailSource.Playlist;
        SelectedPlaylist = playlist;
        SelectedTopCard = null;
        DetailTitle = playlist.Name;
        DetailSubtitle = string.IsNullOrWhiteSpace(playlist.CreatorName) ? "推荐歌单" : $"by {playlist.CreatorName}";
        DetailCover = string.IsNullOrWhiteSpace(playlist.Cover) ? DefaultCardCover : playlist.Cover;

        SelectedDetailSongs.Clear();
        IsShowingSongs = true;

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
        SelectedTopCard = null;
        SelectedDetailSongs.Clear();
        DetailTitle = "";
        DetailSubtitle = "";
        DetailCover = DefaultCardCover;
        _detailSource = DetailSource.None;
    }

    [RelayCommand]
    private async Task LoadMoreSongs()
    {
        if (_detailSource != DetailSource.Playlist || IsLoadingMoreSongs || !_hasMoreSongs || SelectedPlaylist == null)
            return;

        _songPage++;
        await LoadMoreSongsInternal();
    }

    private async Task LoadTopCardsAsync()
    {
        IsTopCardsLoading = true;
        try
        {
            var cardTasks = TopCardDefinitions
                .AsValueEnumerable()
                .Select(LoadTopCardAsync)
                .ToArray();
            var loadedCards = await Task.WhenAll(cardTasks);
            var cards = loadedCards
                .AsValueEnumerable()
                .Where(card => card is { Songs.Count: > 0 })
                .Select(card => card!)
                .ToList();

            TopCards.Clear();
            TopCards.AddRange(cards);
        }
        finally
        {
            IsTopCardsLoading = false;
        }
    }

    private async Task<DiscoverTopCard?> LoadTopCardAsync(DiscoverTopCardDefinition definition)
    {
        try
        {
            var response = await _discoveryClient.GetTopCardAsync(definition.CardId);
            return MapTopCard(definition, response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载发现推荐卡失败。cardId={CardId}", definition.CardId);
            return null;
        }
    }

    private async Task LoadRecommendedPlaylistsAsync(int version)
    {
        IsPlaylistsLoading = true;
        try
        {
            var result = await _discoveryClient.GetRecommendedPlaylistsAsync(page: 1, pageSize: PlaylistPageSize);
            if (version != _playlistLoadVersion)
                return;

            Playlists.Clear();

            if (result?.Playlists == null || result.Playlists.Count == 0)
                return;

            var items = result.Playlists.AsValueEnumerable().Select(MapDiscoverPlaylist).ToList();
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
                IsPlaylistsLoading = false;
        }
    }

    private async Task LoadMoreSongsInternal()
    {
        if (SelectedPlaylist == null)
            return;

        IsLoadingMoreSongs = true;
        try
        {
            var data = await _playlistClient.GetSongsAsync(SelectedPlaylist.GlobalId, _songPage, PlaylistSongPageSize);
            if (data == null)
            {
                _logger.LogWarning("LoadMoreSongs returned null response. playlist={Playlist} page={Page}",
                    SelectedPlaylist.GlobalId, _songPage);
                _songPage--;
                return;
            }

            if (data.Status != 1)
                _logger.LogError("Error : {ErrorCode}", data.ErrorCode);

            var songs = data.Songs;
            if (songs.Count < PlaylistSongPageSize)
                _hasMoreSongs = false;

            var songItems = songs.AsValueEnumerable().Select(s => new SongItem
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

            if (songItems.Count > 0)
                SelectedDetailSongs.AddRange(songItems);
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

    private static DiscoverTopCard MapTopCard(DiscoverTopCardDefinition definition, TopCardResponse? response)
    {
        var card = new DiscoverTopCard
        {
            CardId = definition.CardId,
            Title = definition.Title,
            Cover = string.IsNullOrWhiteSpace(response?.Cover) ? DefaultCardCover : response.Cover,
            Description = response?.RecommendDescription ?? ""
        };

        if (response?.Songs == null || response.Songs.Count == 0)
            return card;

        var songs = response.Songs.AsValueEnumerable().Select(MapTopCardSong).ToList();
        card.Songs.AddRange(songs);

        var firstSong = songs.AsValueEnumerable().FirstOrDefault();
        card.SongSummary = firstSong == null
            ? $"{songs.Count} 首歌曲"
            : $"{firstSong.Name} · {firstSong.Singer}";

        if (string.IsNullOrWhiteSpace(card.Description))
            card.Description = card.SongSummary;

        return card;
    }

    private static SongItem MapTopCardSong(TopCardSong item)
    {
        var singers = ResolveTopCardSingers(item);
        var singerText = singers.Count > 0
            ? string.Join("、", singers.AsValueEnumerable().Select(singer => singer.Name).ToArray())
            : string.IsNullOrWhiteSpace(item.SingerName) ? "未知" : item.SingerName;

        return new SongItem
        {
            Name = item.Name,
            Singer = singerText,
            Hash = item.Hash,
            AlbumId = item.AlbumId.ToString(CultureInfo.InvariantCulture),
            AlbumName = item.AlbumName,
            AudioId = item.AudioId,
            AlbumAudioId = long.TryParse(item.MixSongId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var albumAudioId)
                ? albumAudioId
                : 0,
            Singers = singers,
            Cover = string.IsNullOrWhiteSpace(item.SizableCover) ? DefaultSongCover : item.SizableCover,
            DurationSeconds = item.Duration
        };
    }

    private static List<SingerLite> ResolveTopCardSingers(TopCardSong item)
    {
        return item.Singers
            .AsValueEnumerable()
            .Where(singer => singer.Id > 0 && !string.IsNullOrWhiteSpace(singer.Name))
            .ToList();
    }

    private static DiscoverPlaylist MapDiscoverPlaylist(RecommendPlaylistItem item)
    {
        return new DiscoverPlaylist
        {
            Id = item.ListId.ToString(CultureInfo.InvariantCulture),
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

    private readonly record struct DiscoverTopCardDefinition(int CardId, string Title);

    private enum DetailSource
    {
        None,
        TopCard,
        Playlist
    }
}
