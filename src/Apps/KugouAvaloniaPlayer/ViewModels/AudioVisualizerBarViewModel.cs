using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class AudioVisualizerBarViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double Height { get; set; } = 10;

    [ObservableProperty]
    public partial double Opacity { get; set; } = 0.28;
}