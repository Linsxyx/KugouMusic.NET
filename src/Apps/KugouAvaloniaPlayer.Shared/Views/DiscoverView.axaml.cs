using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Views;

public partial class DiscoverView : UserControl
{
    private static readonly CubicEaseOut ScrollEasing = new();
    private static readonly TimeSpan ScrollAnimationDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan ScrollAnimationFrameDelay = TimeSpan.FromMilliseconds(16);
    private readonly Dictionary<ScrollViewer, CancellationTokenSource> _scrollAnimations = [];

    public DiscoverView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelAllScrollAnimations();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopCardsScrollLeft(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ScrollHorizontal(TopCardsScrollViewer, -1);
    }

    private void OnTopCardsScrollRight(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ScrollHorizontal(TopCardsScrollViewer, 1);
    }

    private void OnPlaylistsScrollLeft(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ScrollHorizontal(PlaylistsScrollViewer, -1);
    }

    private void OnPlaylistsScrollRight(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ScrollHorizontal(PlaylistsScrollViewer, 1);
    }

    private void ScrollHorizontal(ScrollViewer scrollViewer, int direction)
    {
        var currentOffset = scrollViewer.Offset;
        var step = Math.Max(220, scrollViewer.Viewport.Width * 0.72);
        var maxOffsetX = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        var targetX = Math.Clamp(currentOffset.X + direction * step, 0, maxOffsetX);

        if (Math.Abs(targetX - currentOffset.X) < 0.5)
            return;

        CancelScrollAnimation(scrollViewer);

        var cts = new CancellationTokenSource();
        _scrollAnimations[scrollViewer] = cts;
        _ = AnimateHorizontalScrollAsync(scrollViewer, currentOffset.X, targetX, currentOffset.Y, cts);
    }

    private async Task AnimateHorizontalScrollAsync(
        ScrollViewer scrollViewer,
        double startX,
        double targetX,
        double offsetY,
        CancellationTokenSource cancellation)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                var elapsed = DateTime.UtcNow - startTime;
                var progress = Math.Clamp(elapsed.TotalMilliseconds / ScrollAnimationDuration.TotalMilliseconds, 0, 1);
                var easedProgress = ScrollEasing.Ease(progress);
                var currentX = startX + ((targetX - startX) * easedProgress);
                scrollViewer.Offset = new Vector(currentX, offsetY);

                if (progress >= 1)
                    break;

                await Task.Delay(ScrollAnimationFrameDelay, cancellation.Token);
            }

            scrollViewer.Offset = new Vector(targetX, offsetY);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_scrollAnimations.TryGetValue(scrollViewer, out var current) && ReferenceEquals(current, cancellation))
            {
                _scrollAnimations.Remove(scrollViewer);
            }

            cancellation.Dispose();
        }
    }

    private void CancelScrollAnimation(ScrollViewer scrollViewer)
    {
        if (_scrollAnimations.Remove(scrollViewer, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private void CancelAllScrollAnimations()
    {
        foreach (var cancellation in _scrollAnimations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _scrollAnimations.Clear();
    }
}
