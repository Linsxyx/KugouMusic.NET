using CommunityToolkit.Mvvm.ComponentModel;

namespace TestMusic.ViewModels;

public partial class LyricLineViewModel : ObservableObject
{
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private double _duration;

    // 用于控制高亮样式 (字号变大、颜色变亮)
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _startTime;
    [ObservableProperty] private string _translation = "";
}