using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;

namespace TestMusic.ViewModels;

public partial class SearchViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    private readonly MusicClient _musicClient;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private string _searchKeyword = "";
    [ObservableProperty] private string _statusMessage = "";

    public SearchViewModel(MusicClient musicClient)
    {
        _musicClient = musicClient;
    }

    public override string DisplayName => "搜索";
    public override string Icon => "/Assets/Search.svg";

    public ObservableCollection<SongItem> Songs { get; } = new();

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        IsSearching = true;
        StatusMessage = $"正在搜索: {SearchKeyword}...";
        Songs.Clear();

        try
        {
            var results = await _musicClient.SearchAsync(SearchKeyword);
            foreach (var item in results)
                Songs.Add(new SongItem
                {
                    Name = item.Name,
                    Singer = item.Singer,
                    Hash = item.Hash,
                    Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
                    DurationSeconds = item.Duration
                });
            StatusMessage = $"找到 {Songs.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task SearchAsync(string keyword)
    {
        SearchKeyword = keyword;
        await Search();
    }
}