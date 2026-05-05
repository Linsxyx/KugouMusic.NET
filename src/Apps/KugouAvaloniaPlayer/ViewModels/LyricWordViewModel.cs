using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricWordViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsPlayed { get; set; }

    [ObservableProperty]
    public partial double LiftOffset { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial double StartTime { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";
}
