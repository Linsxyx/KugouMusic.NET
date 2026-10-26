using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricWordViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    public partial double StartTime { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";
}
