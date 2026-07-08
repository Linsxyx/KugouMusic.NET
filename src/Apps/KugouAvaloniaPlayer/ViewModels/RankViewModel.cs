using System;
using System.Linq;
using ZLinq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class RankItem : ObservableObject
{
    [ObservableProperty]
    public partial string Cover { get; set; } = "avares://KugouAvaloniaPlayer/Assets/Default.png";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial long RankId { get; set; }

    [ObservableProperty]
    public partial long Classify { get; set; }
}

public sealed class RankSection
{
    public string Title { get; init; } = "";

    public AvaloniaList<RankItem> Items { get; } = new();
}

public partial class RankViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly ILogger<RankViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly RankClient _rankClient;
    private readonly ISukiToastManager _toastManager;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    private RankDetailBackTarget _detailBackTarget = RankDetailBackTarget.RankList;
    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial bool IsShowingSongs { get; set; }

    [ObservableProperty]
    public partial RankItem? SelectedRank { get; set; }

    public RankViewModel(
        RankClient rankClient,
        INavigationService navigationService,
        ISukiToastManager toastManager,
        ILogger<RankViewModel> logger)
    {
        _rankClient = rankClient;
        _navigationService = navigationService;
        _toastManager = toastManager;
        _logger = logger;
        _ = LoadAllRanks();
    }

    public override string DisplayName => "排行榜";
    public override string Icon => "/Assets/headphones-with-music-note-svgrepo-com.svg";

    public AvaloniaList<RankItem> Ranks { get; } = new();
    public AvaloniaList<RankSection> RankSections { get; } = new();
    public AvaloniaList<SongItem> SelectedRankSongs { get; } = new();

    public void ShowRankList()
    {
        _detailBackTarget = RankDetailBackTarget.RankList;
        ClearDetail();
    }

    public Task OpenRankDetailFromPreviousPageAsync(long rankId, string? fallbackName = null, string? fallbackCover = null)
    {
        if (rankId <= 0)
            return Task.CompletedTask;

        var rank = new RankItem
        {
            RankId = rankId,
            Name = fallbackName ?? "",
            Cover = string.IsNullOrWhiteSpace(fallbackCover) ? DefaultCover : fallbackCover
        };

        return OpenRankDetailAsync(rank, RankDetailBackTarget.PreviousPage);
    }

    public async Task OpenRankFromListByIdAsync(
        long rankId,
        string? fallbackName = null,
        string? fallbackCover = null)
    {
        if (rankId <= 0)
            return;

        if (Ranks.Count == 0)
            await LoadAllRanks();

        var rank = Ranks.AsValueEnumerable().FirstOrDefault(item => item.RankId == rankId)
                   ?? new RankItem
                   {
                       RankId = rankId,
                       Name = fallbackName ?? "",
                       Cover = string.IsNullOrWhiteSpace(fallbackCover) ? DefaultCover : fallbackCover
                   };

        await OpenRankDetailAsync(rank, RankDetailBackTarget.RankList);
    }

    [RelayCommand]
    private async Task LoadAllRanks()
    {
        Ranks.Clear();
        RankSections.Clear();
        try
        {
            var response = await _rankClient.GetAllRanksAsync();
            if (response?.Info != null)
            {
                var items = response.Info.AsValueEnumerable().Select(r => new RankItem
                {
                    RankId = r.FileId,
                    Name = r.Name,
                    Cover = string.IsNullOrWhiteSpace(r.Cover) ? DefaultCover : r.Cover,
                    Classify = r.Classify
                }).ToList();

                if (items.AsValueEnumerable().Any())
                {
                    Ranks.AddRange(items);

                    var sections = items
                        .AsEnumerable()
                        .GroupBy(item => item.Classify)
                        .OrderBy(group => GetClassifyOrder(group.Key))
                        .Select(group =>
                        {
                            var section = new RankSection
                            {
                                Title = GetClassifyTitle(group.Key)
                            };
                            section.Items.AddRange(group.OrderBy(item => item.Name));
                            return section;
                        })
                        .ToList();

                    RankSections.AddRange(sections);
                }
            }
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("获取排行榜失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_detailBackTarget == RankDetailBackTarget.PreviousPage)
        {
            _navigationService.GoBack();
            _detailBackTarget = RankDetailBackTarget.RankList;
            return;
        }

        ClearDetail();
    }

    [RelayCommand]
    private void NavigateBack()
    {
        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task OpenRank(RankItem? item)
    {
        await OpenRankDetailAsync(item, RankDetailBackTarget.RankList);
    }

    private async Task OpenRankDetailAsync(RankItem? item, RankDetailBackTarget backTarget)
    {
        if (item is null) return;

        _detailBackTarget = backTarget;
        SelectedRank = item;
        IsShowingSongs = true;
        SelectedRankSongs.Clear();

        _currentPage = 1;
        _hasMoreSongs = true;
        IsLoadingMore = false;

        await LoadMoreSongsInternal();
    }

    private void ClearDetail()
    {
        IsShowingSongs = false;
        SelectedRank = null;
        SelectedRankSongs.Clear();
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (IsLoadingMore || !_hasMoreSongs)
            return;

        _currentPage++;
        await LoadMoreSongsInternal();
    }

    private async Task LoadMoreSongsInternal()
    {
        if (SelectedRank == null) return;

        IsLoadingMore = true;
        try
        {
            var response = await _rankClient.GetRankSongsAsync((int)SelectedRank.RankId, _currentPage, 100);
            if (response == null || response.RankSongLists.Count == 0)
            {
                _hasMoreSongs = false;
            }
            else
            {
                if (response.RankSongLists.Count < 100) _hasMoreSongs = false;

                var songItems = response.RankSongLists.AsValueEnumerable().Select(s => new SongItem
                {
                    Name = s.Name,
                    Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.AsValueEnumerable().Select(x => x.Name).ToArray()) : "未知",
                    Hash = s.Hash,
                    AlbumId = s.AlbumId.ToString(),
                    AlbumName = s.Album?.Name ?? "",
                    Singers = s.Singers,
                    Cover =
                        string.IsNullOrWhiteSpace(s.TransParam?.UnionCover) ? DefaultCover : s.TransParam.UnionCover,
                    DurationSeconds = s.DurationMs / 1000.0
                }).ToList();

                if (songItems.AsValueEnumerable().Any())
                    SelectedRankSongs.AddRange(songItems);
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

    private enum RankDetailBackTarget
    {
        RankList,
        PreviousPage
    }

    private static int GetClassifyOrder(long classify)
    {
        return classify switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            _ => int.MaxValue
        };
    }

    private static string GetClassifyTitle(long classify)
    {
        return classify switch
        {
            1 => "星耀榜",
            2 => "地区榜",
            3 => "特色榜",
            4 => "全球榜",
            5 => "曲风榜",
            _ => "其他榜单"
        };
    }
}
