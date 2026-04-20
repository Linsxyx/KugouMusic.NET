using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class GlobalShortcutItemViewModel(GlobalShortcutAction action, string displayName)
    : ObservableObject
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D92D20"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#667085"));
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isRecording;

    [ObservableProperty] private string _shortcutText = "未设置";
    [ObservableProperty] private IBrush _statusForeground = InfoBrush;
    [ObservableProperty] private string? _statusMessage;

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