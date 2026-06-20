using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;
using ZLinq;

namespace KugouAvaloniaPlayer.Views;

public partial class NowPlayingView : UserControl
{
    private NowPlayingViewModel? _nowPlayingViewModel;
    private PlayerViewModel? _playerViewModel;
    private Size _lastSharedBackgroundSize;
    private Point _lastSharedBackgroundOffset;

    public NowPlayingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
        UpdateSharedBackgroundFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        HideMoreFlyout();
        DetachMoreFlyoutLightDismissHandler();
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
        UnhookViewModel();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateSharedBackgroundFrame();
    }

    private void UpdateSharedBackgroundFrame()
    {
        var target = this.GetVisualAncestors()
            .AsValueEnumerable().OfType<Control>()
            .FirstOrDefault(control => control.Name == "MainGrid");
        if (target == null || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
            return;

        var offset = this.TranslatePoint(new Point(0, 0), target) ?? default;
        var size = target.Bounds.Size;
        if (_lastSharedBackgroundSize == size && _lastSharedBackgroundOffset == offset)
            return;

        _lastSharedBackgroundSize = size;
        _lastSharedBackgroundOffset = offset;
        SharedBackgroundFrame.Width = target.Bounds.Width;
        SharedBackgroundFrame.Height = target.Bounds.Height;
        SharedBackgroundFrame.RenderTransform = new TranslateTransform(-offset.X, -offset.Y);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnhookViewModel();
        _nowPlayingViewModel = DataContext as NowPlayingViewModel;
        _playerViewModel = _nowPlayingViewModel?.Player;
        if (_nowPlayingViewModel != null)
            _nowPlayingViewModel.PropertyChanged += OnNowPlayingPropertyChanged;
        if (_playerViewModel != null)
            _playerViewModel.RenderLyricLines.CollectionChanged += OnLyricLinesChanged;
    }

    private void UnhookViewModel()
    {
        if (_playerViewModel != null)
            _playerViewModel.RenderLyricLines.CollectionChanged -= OnLyricLinesChanged;
        if (_nowPlayingViewModel == null) return;
        _nowPlayingViewModel.PropertyChanged -= OnNowPlayingPropertyChanged;
        _nowPlayingViewModel = null;
        _playerViewModel = null;
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NowPlayingViewModel.IsOpen))
            return;

        if (_nowPlayingViewModel?.IsOpen != true)
        {
            HideMoreFlyout();
            return;
        }

        Dispatcher.Post(() => { LyricScrollView?.ForceSecondPassLayout(); }, DispatcherPriority.Render);
    }

    private void OnLyricLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_nowPlayingViewModel?.IsOpen != true || _playerViewModel?.RenderLyricLines.Count <= 0)
            return;

        Dispatcher.Post(() => { LyricScrollView?.ForceSecondPassLayout(); }, DispatcherPriority.Render);
    }
}
