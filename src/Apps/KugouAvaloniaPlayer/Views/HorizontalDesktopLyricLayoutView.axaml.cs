using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KugouAvaloniaPlayer.Views;

public partial class HorizontalDesktopLyricLayoutView : UserControl
{
    public HorizontalDesktopLyricLayoutView()
    {
        InitializeComponent();
    }

    private void OnControlSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
            window.BeginMoveDrag(e);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        (TopLevel.GetTopLevel(this) as Window)?.Close();
    }
}
