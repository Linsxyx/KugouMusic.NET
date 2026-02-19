using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TestMusic.Controls;

public partial class NowPlaying : UserControl
{
    private bool _autoScrolling;

    public NowPlaying()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLyricsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem == null) return;
        if (_autoScrolling) return;
        _autoScrolling = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ScrollToCenter(listBox, listBox.SelectedItem);
            }
            finally
            {
                _autoScrolling = false;
            }
        }, DispatcherPriority.Input);
    }

    private void ScrollToCenter(ListBox listBox, object item)
    {
        var scrollViewer = listBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (scrollViewer == null)
            return;

        listBox.ScrollIntoView(item);

        Dispatcher.UIThread.Post(() =>
        {
            var container = listBox.ContainerFromItem(item);
            if (container == null)
                return;

            var transform = container.TransformToVisual(scrollViewer);
            if (transform == null)
                return;

            var position = transform.Value.Transform(new Point(0, 0));

            var itemCenter = position.Y + container.Bounds.Height / 2;
            var viewportCenter = scrollViewer.Viewport.Height / 2;

            var targetOffset = scrollViewer.Offset.Y + (itemCenter - viewportCenter);

            targetOffset = Math.Clamp(
                targetOffset,
                0,
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height
            );

            scrollViewer.Offset = new Vector(0, targetOffset);
        }, DispatcherPriority.Render);
    }
}