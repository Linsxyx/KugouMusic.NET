using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class GlobalShortcutItemViewModel(GlobalShortcutAction action, string displayName)
    : ObservableObject
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D92D20"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#667085"));
    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial string ShortcutText { get; set; } = "未设置";

    [ObservableProperty]
    public partial IBrush StatusForeground { get; set; } = InfoBrush;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }
    public GlobalShortcutAction Action { get; } = action;
    public string DisplayName { get; } = displayName;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
            ShortcutText = "按下快捷键...";
    }

    public void ApplyShortcutText(string? shortcutText)
    {
        ShortcutText = string.IsNullOrWhiteSpace(shortcutText) ? "未设置" : shortcutText;
    }

    public void SetInfo(string? message)
    {
        HasError = false;
        StatusForeground = InfoBrush;
        StatusMessage = message;
    }

    public void SetError(string? message)
    {
        HasError = !string.IsNullOrWhiteSpace(message);
        StatusForeground = ErrorBrush;
        StatusMessage = message;
    }

    public void ClearStatus()
    {
        HasError = false;
        StatusForeground = InfoBrush;
        StatusMessage = null;
    }
}