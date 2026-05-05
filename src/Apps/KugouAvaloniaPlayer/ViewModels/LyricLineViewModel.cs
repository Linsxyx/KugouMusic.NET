using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricLineViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Content { get; set; } = "";

    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    public partial bool HasWordLevelTranslation { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsKrcWordLevel { get; set; }

    [ObservableProperty]
    public partial string Romanization { get; set; } = "";

    [ObservableProperty]
    public partial double StartTime { get; set; }

    [ObservableProperty]
    public partial string Translation { get; set; } = "";
    public AvaloniaList<LyricWordViewModel> Words { get; } = new();
    public AvaloniaList<LyricWordViewModel> TranslationWords { get; } = new();
}