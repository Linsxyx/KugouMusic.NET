using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SingerViewModel : PageViewModelBase
{
    private readonly string _authorId;
    private readonly MusicClient _musicClient;

    private int _currentPage = 1;
    [ObservableProperty] private string _currentSortText = "热门";
    private bool _hasMoreSongs = true;

    // 最新/热门切换
    [ObservableProperty] private bool _isHotSort = true;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private string _singerAvatar;
    [ObservableProperty] private string _singerName;

    public SingerViewModel(MusicClient musicClient, string authorId, string singerName)
    {
        _musicClient = musicClient;
        _authorId = authorId;
        _singerName = singerName;
        _singerAvatar = "avares://KugouAvaloniaPlayer/Assets/Default.png";
        _ = LoadSongsAsync();
    }

    public override string DisplayName => "歌手详情";
    public override string Icon => "/Assets/user-svgrepo-com.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();

    private async Task LoadSongsAsync()
    {
        IsLoading = true;

        try
        {
            var json = await _musicClient.GetSingerDetailAsync(_authorId);

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
            var result = await _musicClient.GetSingerSongsAsync(
                _authorId, page, 30, sort);

            if (result?.Songs == null)
                return false;

            if (result.Songs.Count < 30)
                _hasMoreSongs = false;

            foreach (var item in result.Songs)
                if (!string.IsNullOrEmpty(item.Hash))
                    Songs.Add(new SongItem
                    {
                        Name = item.Name,
                        Singer = item.SingerName,
                        Hash = item.Hash,
                        AlbumId = item.AlbumId.ToString(),
                        DurationSeconds = item.Duration / 1000.0,
                        Cover = item.TransParam.UnionCover
                    });

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
        }
    }


    [RelayCommand]
    private async Task ToggleSort()
    {
        IsHotSort = !IsHotSort;
        CurrentSortText = IsHotSort ? "热门" : "最新";

        await LoadSongsAsync();
    }
}