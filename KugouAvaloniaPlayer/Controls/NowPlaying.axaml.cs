using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class NowPlaying : UserControl
{
    private MainWindowViewModel? _mainWindowViewModel;

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
        if (_mainWindowViewModel != null)
            _mainWindowViewModel.PropertyChanged += OnMainWindowPropertyChanged;
    }

    private void UnhookMainViewModel()
    {
        if (_mainWindowViewModel == null) return;
        _mainWindowViewModel.PropertyChanged -= OnMainWindowPropertyChanged;
        _mainWindowViewModel = null;
    }

    private void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsNowPlayingOpen) ||
            _mainWindowViewModel?.IsNowPlayingOpen != true)
            return;

        Dispatcher.UIThread.Post(() => { LyricScrollView?.ForceSecondPassLayout(); }, DispatcherPriority.Render);
    }
}