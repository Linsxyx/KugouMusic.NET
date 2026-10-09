using System;
using System.ComponentModel;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class SettingView : UserControl
{
    private SettingViewModel? _model;
    private string? _scrollKey;
    private Window? _window;
    public SettingView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => AdaptLayout();
    }
    private void AdaptLayout()
    {
        var wide = Bounds.Width >= 760;
        SideNavigation.IsVisible = wide;
        CategoryTitle.IsVisible = wide;
        CompactNavigation.IsVisible = !wide;
        LayoutGrid.ColumnDefinitions = new ColumnDefinitions(wide ? "188,*" : "*");
        Grid.SetColumn(ContentGrid, wide ? 1 : 0);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _model = DataContext as SettingViewModel;
        if (_model != null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.SyncEqPreset();
            _scrollKey = _model.ScrollKey;
            RestoreScroll();
        }
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += OnWindowDeactivated;
        AdaptLayout();
    }
    private void OnWindowDeactivated(object? sender, EventArgs e) => _model?.CancelShortcutRecording();
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SaveScroll();
        if (_model != null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.CancelShortcutRecording();
        }
        if (_window != null) _window.Deactivated -= OnWindowDeactivated;
        _model = null;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }
    private void SaveScroll()
    {
        if (_model != null && _scrollKey != null) _model.ScrollPositions[_scrollKey] = ContentScroll.Offset;
    }
    private void RestoreScroll() => Dispatcher.UIThread.Post(() =>
    {
        if (_model != null && _scrollKey != null)
            ContentScroll.Offset = _model.ScrollPositions.GetValueOrDefault(_scrollKey);
    }, DispatcherPriority.Loaded);
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingViewModel.ScrollKey)) return;
        SaveScroll();
        _scrollKey = _model?.ScrollKey;
        RestoreScroll();
    }
}
