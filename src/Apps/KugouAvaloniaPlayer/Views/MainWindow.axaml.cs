using Avalonia.Controls;
#if DEBUG
using Avalonia.Input;
#endif
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class MainWindow : SukiWindow
{
#if DEBUG
    private LyricMotionDebugWindow? _lyricMotionDebugWindow;
#endif

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

#if DEBUG
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.L &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ShowLyricMotionDebugWindow();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void ShowLyricMotionDebugWindow()
    {
        if (_lyricMotionDebugWindow != null)
        {
            _lyricMotionDebugWindow.Activate();
            return;
        }

        _lyricMotionDebugWindow = new LyricMotionDebugWindow();
        _lyricMotionDebugWindow.Closed += (_, _) => _lyricMotionDebugWindow = null;
        _lyricMotionDebugWindow.Show(this);
    }
#endif
}
