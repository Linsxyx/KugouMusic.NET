using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Controls;

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
                _ = ScrollToCenter(listBox, listBox.SelectedItem);
            }
            finally
            {
                _autoScrolling = false;
            }
        }, DispatcherPriority.Input);
    }

    private async Task ScrollToCenter(ListBox listBox, object item)
    {
        var scrollViewer = listBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (scrollViewer == null)
            return;

        listBox.ScrollIntoView(item);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        var container = listBox.ContainerFromItem(item);
        if (container == null)
            return;

        // container 相对于 ScrollViewer 的位置
        var transform = container.TransformToVisual(scrollViewer);
        if (transform == null)
            return;

        var point = transform.Value.Transform(new Point(0, 0));

        var itemCenter = point.Y + container.Bounds.Height / 2;
        var viewportCenter = scrollViewer.Viewport.Height / 2;

        var targetOffset = scrollViewer.Offset.Y + (itemCenter - viewportCenter);

        targetOffset = Math.Clamp(
            targetOffset,
            0,
            scrollViewer.Extent.Height - scrollViewer.Viewport.Height
        );

        scrollViewer.Offset = new Vector(0, targetOffset);
    }
}