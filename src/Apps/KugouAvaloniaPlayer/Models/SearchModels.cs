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
    [ObservableProperty]
    public partial int Index { get; set; }

    [ObservableProperty]
    public partial string Keyword { get; set; } = "";

    [ObservableProperty]
    public partial string Reason { get; set; } = "";
}

public partial class SearchHotTagGroup : ObservableObject
{
    [ObservableProperty]
    public partial int Index { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";
    public AvaloniaList<SearchHotTagItem> Keywords { get; } = new();
}