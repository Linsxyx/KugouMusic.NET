using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SingerMatchDialogViewModel : ObservableObject
{
    private readonly Func<SingerLite, Task> _selectAction;
    private readonly Action _cancelAction;

    [ObservableProperty]
    public partial string Title { get; set; }

    public List<SearchAuthorItem> Results { get; init; }

    public SingerMatchDialogViewModel(
        string keyword,
        List<SearchAuthorItem> results,
        Func<SingerLite, Task> selectAction,
        Action cancelAction)
    {
        Title = $"在线匹配歌手 - {keyword}";
        _selectAction = selectAction;
        _cancelAction = cancelAction;
        if (results.Count > 0) {
            Results = results;
            OnPropertyChanged(nameof(Results));
        } else 
            Results = new(); 
    }

    [RelayCommand]
    private async Task Select(SearchAuthorItem? item)
    {
        if (item is null)
            return;

        await _selectAction(
            new SingerLite
            {
                Id = item.AuthorId,
                Name = item.Name,
                SingerPic = item.Cover ?? string.Empty
            });
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelAction();
    }
}