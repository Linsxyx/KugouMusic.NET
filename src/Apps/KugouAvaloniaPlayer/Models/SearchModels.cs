using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.Models;

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

public partial class SearchHotTagItem : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private string _reason = "";
}

public partial class SearchHotTagGroup : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _name = "";
    public AvaloniaList<SearchHotTagItem> Keywords { get; } = new();
}