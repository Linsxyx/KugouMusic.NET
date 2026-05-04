using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class DesktopLyricLockOverlayWindow : Window
{
    public DesktopLyricLockOverlayWindow()
    {
        InitializeComponent();
    }

    public DesktopLyricViewModel? ViewModel => DataContext as DesktopLyricViewModel;

    private void OnHotspotPointerEntered(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetCollapsedLockIconHovered(true);
    }

    private void OnHotspotPointerExited(object? sender, PointerEventArgs e)
    {
        ViewModel?.SetCollapsedLockIconHovered(false);
    }

    private void OnHotspotPointerPressed(object? sender, PointerPressedEventArgs e)
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
