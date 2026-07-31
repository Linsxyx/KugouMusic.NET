using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;
using ZLinq;

namespace KugouAvaloniaPlayer.Views;

public partial class NowPlayingView : UserControl
{
    private NowPlayingViewModel? _nowPlayingViewModel;
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
        HookViewModel();
        UpdateThemeContent();
        LayoutUpdated += OnLayoutUpdated;
        UpdateSharedBackgroundFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnhookViewModel();
        ThemeContentHost.Children.Clear();
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
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
        HookViewModel();
        UpdateThemeContent();
    }

    private void HookViewModel()
    {
        var viewModel = DataContext as NowPlayingViewModel;
        if (ReferenceEquals(_nowPlayingViewModel, viewModel))
            return;

        _nowPlayingViewModel = viewModel;
        if (_nowPlayingViewModel != null)
            _nowPlayingViewModel.PropertyChanged += OnNowPlayingPropertyChanged;
    }

    private void UnhookViewModel()
    {
        if (_nowPlayingViewModel != null)
            _nowPlayingViewModel.PropertyChanged -= OnNowPlayingPropertyChanged;
        _nowPlayingViewModel = null;
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentContent))
            UpdateThemeContent();
    }

    private void UpdateThemeContent()
    {
        ThemeContentHost.Children.Clear();
        if (_nowPlayingViewModel?.CurrentContent is not { } content)
            return;

        content.DataContext = _nowPlayingViewModel;
        ThemeContentHost.Children.Add(content);
    }

}
