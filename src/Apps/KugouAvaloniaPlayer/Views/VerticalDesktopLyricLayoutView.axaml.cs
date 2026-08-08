using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class VerticalDesktopLyricLayoutView : UserControl
{
    private double _lastReportedWidth;
    private bool _widthReportQueued;

    public VerticalDesktopLyricLayoutView()
    {
        InitializeComponent();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not VerticalDesktopLyricLayoutViewModel layout)
            return;

        if (layout.Owner.VerticalDesiredWidth <= DesktopLyricViewModel.VerticalBaseWindowWidth &&
            _lastReportedWidth > DesktopLyricViewModel.VerticalBaseWindowWidth)
        {
            _lastReportedWidth = 0;
        }

        var contentWidth = layout.Owner.IsDoubleLineEnabled
            ? DoubleVerticalLines.DesiredSize.Width
            : SingleVerticalLine.DesiredSize.Width;
        var width = Math.Ceiling(contentWidth + 48);
        if (width <= _lastReportedWidth + 1 || _widthReportQueued)
            return;

        _lastReportedWidth = width;
        _widthReportQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _widthReportQueued = false;
            if (TopLevel.GetTopLevel(this) != null &&
                DataContext is VerticalDesktopLyricLayoutViewModel currentLayout &&
                ReferenceEquals(currentLayout.Owner.ActiveLayout, currentLayout))
            {
                currentLayout.Owner.ReportVerticalDesiredWidth(width);
            }
        });
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
