using System;
using System.ComponentModel;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class SettingView : UserControl
{
    private SettingViewModel? _model;
    private string? _scrollKey;
    private Window? _window;
    private bool _syncingDock;

    public SettingView()
    {
        InitializeComponent();
        InitializeCategoryDock();
        SizeChanged += (_, _) => AdaptLayout();
    }

    private void InitializeCategoryDock()
    {
        CategoryDock.Items.Clear();
        foreach (var category in SettingsCatalog.Categories)
        {
            CategoryDock.Items.Add(new VelaDockItem
            {
                Header = ResolveShortDockLabel(category),
                Icon = category.Icon,
                ToolTip = $"{category.Title} · {category.Description}",
                Tag = category
            });
        }

        CategoryDock.SelectionChanged += OnCategoryDockSelectionChanged;
    }

    private static string ResolveShortDockLabel(SettingsCategory category) => category.Id switch
    {
        "appearance" => "外观",
        "playback" => "播放",
        "lyrics" => "歌词",
        "shortcuts" => "快捷键",
        "general" => "通用",
        "about" => "关于",
        "account" => "账户",
        _ => category.Title
    };

    private void OnCategoryDockSelectionChanged(object? sender, int index)
    {
        if (_syncingDock || _model == null)
            return;

        if (index >= 0 && index < _model.Categories.Length)
            _model.SelectedCategory = _model.Categories[index];
    }

    private void SyncCategoryDockSelection()
    {
        if (_model == null)
            return;

        var idx = Array.IndexOf(_model.Categories, _model.SelectedCategory);
        if (idx < 0)
            idx = 0;

        if (CategoryDock.SelectedIndex == idx)
            return;

        _syncingDock = true;
        try
        {
            CategoryDock.SelectedIndex = idx;
        }
        finally
        {
            _syncingDock = false;
        }
    }

    private void AdaptLayout()
    {
        var wide = Bounds.Width >= 760;
        SideNavigation.IsVisible = wide;
        CategoryHeading.IsVisible = wide;
        CompactNavigation.IsVisible = !wide;
        LayoutGrid.ColumnDefinitions = new ColumnDefinitions(wide ? "116,*" : "*");
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
            SyncCategoryDockSelection();
            RestoreScroll();
        }
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += OnWindowDeactivated;
        AdaptLayout();
        Dispatcher.UIThread.Post(AdaptLayout, DispatcherPriority.Loaded);
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
        if (e.PropertyName == nameof(SettingViewModel.SelectedCategory))
        {
            SyncCategoryDockSelection();
            return;
        }

        if (e.PropertyName != nameof(SettingViewModel.ScrollKey)) return;
        SaveScroll();
        _scrollKey = _model?.ScrollKey;
        RestoreScroll();
    }
}
