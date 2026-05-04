using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class AudioVisualizerBarViewModel : ObservableObject
{
    [ObservableProperty] private double _height = 10;
    [ObservableProperty] private double _opacity = 0.28;
}