using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricLineViewModel : ObservableObject
{
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _hasWordLevelTranslation;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isKrcWordLevel;
    [ObservableProperty] private string _romanization = "";
    [ObservableProperty] private double _startTime;
    [ObservableProperty] private string _translation = "";

    public AvaloniaList<LyricWordViewModel> Words { get; } = new();
    public AvaloniaList<LyricWordViewModel> TranslationWords { get; } = new();
}