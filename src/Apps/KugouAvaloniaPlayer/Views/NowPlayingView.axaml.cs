using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views.NowPlayingThemes;
using ZLinq;

namespace KugouAvaloniaPlayer.Views;

public partial class NowPlayingView : UserControl
{
    private static readonly TimeSpan ThemeUnloadDelay = TimeSpan.FromMilliseconds(480);

    private NowPlayingViewModel? _nowPlayingViewModel;
    private CancellationTokenSource? _themeUnloadCancellation;
    private NowPlayingThemePreset? _loadedThemePreset;
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
        SynchronizeThemeContent();
        LayoutUpdated += OnLayoutUpdated;
        UpdateSharedBackgroundFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingThemeUnload();
        UnhookViewModel();
        ClearThemeContent();
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
        CancelPendingThemeUnload();
        UnhookViewModel();
        ClearThemeContent();
        HookViewModel();
        SynchronizeThemeContent();
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
        if (e.PropertyName == nameof(NowPlayingViewModel.IsOpen))
        {
            SynchronizeThemeContent();
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.SelectedThemePreset) &&
            _nowPlayingViewModel?.IsOpen == true)
            LoadThemeContent();
    }

    private void SynchronizeThemeContent()
    {
        if (_nowPlayingViewModel?.IsOpen == true)
        {
            CancelPendingThemeUnload();
            LoadThemeContent();
            return;
        }

        ScheduleThemeUnload();
    }

    private void LoadThemeContent()
    {
        var viewModel = _nowPlayingViewModel;
        if (viewModel == null)
        {
            ClearThemeContent();
            return;
        }

        var preset = viewModel.SelectedThemePreset;
        if (_loadedThemePreset == preset && ThemeContentHost.Children.Count > 0)
            return;

        ClearThemeContent();

        Control content = preset switch
        {
            NowPlayingThemePreset.Pendolo => new PendoloNowPlayingThemeView(),
            NowPlayingThemePreset.Fume => new FumeNowPlayingThemeView(),
            _ => new StandardNowPlayingThemeView()
        };

        content.DataContext = viewModel;
        ThemeContentHost.Children.Add(content);
        _loadedThemePreset = preset;
    }

    private async void ScheduleThemeUnload()
    {
        CancelPendingThemeUnload();

        var cancellation = new CancellationTokenSource();
        _themeUnloadCancellation = cancellation;

        try
        {
            await Task.Delay(ThemeUnloadDelay, cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_themeUnloadCancellation, cancellation) &&
                    _nowPlayingViewModel?.IsOpen != true)
                    ClearThemeContent();
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_themeUnloadCancellation, cancellation))
                _themeUnloadCancellation = null;

            cancellation.Dispose();
        }
    }

    private void CancelPendingThemeUnload()
    {
        var cancellation = _themeUnloadCancellation;
        _themeUnloadCancellation = null;
        cancellation?.Cancel();
    }

    private void ClearThemeContent()
    {
        foreach (var child in ThemeContentHost.Children)
            child.DataContext = null;

        ThemeContentHost.Children.Clear();
        _loadedThemePreset = null;
    }

    private void ShowModeConfigurationFlyout(object? sender, PointerEventArgs e) {
        if (sender is not Button btn)
            return; // fastfail
        FlyoutBase.ShowAttachedFlyout(btn);
    }
}
