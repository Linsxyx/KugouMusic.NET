// --- START OF NEW FILE KugouAvaloniaPlayer/ViewModels/RankViewModel.cs ---

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class RankItem : ObservableObject
{
    [ObservableProperty] private string _cover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long _rankId;
}

public partial class RankViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly RankClient _rankClient;
    private readonly ISukiToastManager _toastManager;

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private bool _isShowingSongs;
    [ObservableProperty] private RankItem? _selectedRank;

    public RankViewModel(RankClient rankClient, ISukiToastManager toastManager)
    {
        _rankClient = rankClient;
        _toastManager = toastManager;
        _ = LoadAllRanks();
    }

    public override string DisplayName => "排行榜";
    public override string Icon => "/Assets/headphones-with-music-note-svgrepo-com.svg";

    public AvaloniaList<RankItem> Ranks { get; } = new();
    public AvaloniaList<SongItem> SelectedRankSongs { get; } = new();

    [RelayCommand]
    private async Task LoadAllRanks()
    {
        Ranks.Clear();
        try
        {
            var response = await _rankClient.GetAllRanksAsync();
            if (response?.Info != null)
            {
                var items = response.Info.Select(r => new RankItem
                {
                    RankId = r.FileId,
                    Name = r.Name,
                    Cover = string.IsNullOrWhiteSpace(r.Cover) ? DefaultCover : r.Cover
                }).ToList();

                if (items.Any()) Ranks.AddRange(items);
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
        IsShowingSongs = false;
        SelectedRank = null;
        SelectedRankSongs.Clear();
    }

    [RelayCommand]
    private async Task OpenRank(RankItem? item)
    {
        if (item is null) return;

        SelectedRank = item;
        IsShowingSongs = true;
        SelectedRankSongs.Clear();

        _currentPage = 1;
        _hasMoreSongs = true;
        IsLoadingMore = false;

        await LoadMoreSongsInternal();
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (IsLoadingMore || !_hasMoreSongs) return;

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

                var songItems = response.RankSongLists.Select(s => new SongItem
                {
                    Name = s.Name,
                    Singer = s.Singers.Count > 0 ? string.Join("、", s.Singers.Select(x => x.Name)) : "未知",
                    Hash = s.Hash,
                    AlbumId = s.AlbumId.ToString(),
                    Singers = s.Singers,
                    Cover =
                        string.IsNullOrWhiteSpace(s.TransParam?.UnionCover) ? DefaultCover : s.TransParam.UnionCover,
                    DurationSeconds = s.DurationMs / 1000.0
                }).ToList();

                if (songItems.Any())
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
}