using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class DesktopLyricWindow : Window
{
    public DesktopLyricWindow()
    {
        InitializeComponent();
    }

    public DesktopLyricViewModel? ViewModel => DataContext as DesktopLyricViewModel;

    private void OnLyricPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.IsLocked == true)
            return;

        BeginMoveDrag(e);
    }

    private void OnControlHotspotPointerEntered(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetControlHotspotHovered(true);
    }

    private void OnControlHotspotPointerExited(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetControlHotspotHovered(false);
    }

    private void OnCollapsedIconPointerEntered(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetCollapsedLockIconHovered(true);
    }

    private void OnCollapsedIconPointerExited(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetCollapsedLockIconHovered(false);
    }

    private void OnCollapsedIconPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            BeginMoveDrag(e);
            return;
        }

        if (properties.IsLeftButtonPressed && ViewModel?.IsLocked == true)
            ViewModel.Unlock();
    }
}
