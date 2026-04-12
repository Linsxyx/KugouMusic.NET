using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricWordViewModel : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private double _startTime;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _isPlayed;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private double _liftOffset;
}
