using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class TrayPlaybackPanel : UserControl
{
    public event EventHandler? ShowMainWindowRequested;

    public TrayPlaybackPanel()
    {
        InitializeComponent();
    }

    private void TrackHeader_OnClick(object? sender, RoutedEventArgs e)
    {
        ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void VolumeMuteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Player.ToggleMute();
    }
}
