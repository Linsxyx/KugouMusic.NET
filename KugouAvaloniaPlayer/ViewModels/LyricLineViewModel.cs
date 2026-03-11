using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricLineViewModel : ObservableObject
{
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private double _duration;
    
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _startTime;
    [ObservableProperty] private string _translation = "";
}