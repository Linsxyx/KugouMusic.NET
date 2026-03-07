using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public bool CanClose { get; set; } = false;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
        }
    }

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

    private void TextBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            if (DataContext is MainWindowViewModel vm && vm.SearchCommand.CanExecute(null))
                vm.SearchCommand.Execute(null);
    }
}