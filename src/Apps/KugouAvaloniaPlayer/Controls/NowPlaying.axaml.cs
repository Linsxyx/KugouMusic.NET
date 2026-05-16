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
    private NowPlayingViewModel? _nowPlayingViewModel;
    private PlayerViewModel? _playerViewModel;

    public NowPlaying()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        HideMoreFlyout();
        DetachMoreFlyoutLightDismissHandler();
        base.OnDetachedFromVisualTree(e);
        UnhookViewModel();
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
