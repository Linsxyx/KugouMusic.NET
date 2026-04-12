using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public bool CanClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var behavior = SettingsManager.Settings.CloseBehavior;
        if (behavior == CloseBehavior.MinimizeToTray && !CanClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (DataContext is MainWindowViewModel vm) vm.ForceCloseDesktopLyric();

        base.OnClosing(e);
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.IsQueuePaneOpen = false;
    }
}
