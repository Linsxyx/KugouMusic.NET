using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class NowPlaying : UserControl
{
    private MainWindowViewModel? _mainWindowViewModel;
    private PlayerViewModel? _playerViewModel;

    public NowPlaying()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookMainViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnhookMainViewModel();
        _mainWindowViewModel = DataContext as MainWindowViewModel;
        _playerViewModel = _mainWindowViewModel?.Player;
        if (_mainWindowViewModel != null)
            _mainWindowViewModel.PropertyChanged += OnMainWindowPropertyChanged;
        if (_playerViewModel != null)
            _playerViewModel.LyricLines.CollectionChanged += OnLyricLinesChanged;
    }

    private void UnhookMainViewModel()
    {
        if (_playerViewModel != null)
            _playerViewModel.LyricLines.CollectionChanged -= OnLyricLinesChanged;
        if (_mainWindowViewModel == null) return;
        _mainWindowViewModel.PropertyChanged -= OnMainWindowPropertyChanged;
        _mainWindowViewModel = null;
        _playerViewModel = null;
    }

    private void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsNowPlayingOpen) ||
            _mainWindowViewModel?.IsNowPlayingOpen != true)
            return;

        Dispatcher.Post(() => { LyricScrollView?.ForceSecondPassLayout(); }, DispatcherPriority.Render);
    }

    private void OnLyricLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_mainWindowViewModel?.IsNowPlayingOpen != true || _playerViewModel?.LyricLines.Count <= 0)
            return;

        Dispatcher.Post(() => { LyricScrollView?.ForceSecondPassLayout(); }, DispatcherPriority.Render);
    }
}
