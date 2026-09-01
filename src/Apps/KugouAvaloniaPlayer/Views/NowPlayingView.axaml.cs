using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views.NowPlayingThemes;
using ZLinq;

namespace KugouAvaloniaPlayer.Views;

public partial class NowPlayingView : UserControl
{
    private static readonly TimeSpan ThemeUnloadDelay = TimeSpan.FromMilliseconds(480);

    private NowPlayingViewModel? _nowPlayingViewModel;
    private CancellationTokenSource? _themeUnloadCancellation;
    private CancellationTokenSource? _backgroundBlurCancellation;
    private Bitmap? _cachedBlurredBackground;
    private byte[]? _encodedBackgroundSource;
    private NowPlayingThemePreset? _loadedThemePreset;
    private Size _lastSharedBackgroundSize;
    private Point _lastSharedBackgroundOffset;

    private const double Epsilon = 1e-6; 

    public NowPlayingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BackgroundSourceImage.PropertyChanged += OnBackgroundSourceImagePropertyChanged;
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
        CancelPendingBackgroundBlur();
        ClearCachedBackground();
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

       
        var widthChanged = Math.Abs(_lastSharedBackgroundSize.Width - size.Width) > Epsilon;
        _lastSharedBackgroundSize = size;
        _lastSharedBackgroundOffset = offset;
        SharedBackgroundFrame.Width = target.Bounds.Width;
        SharedBackgroundFrame.Height = target.Bounds.Height;
        SharedBackgroundFrame.RenderTransform = new TranslateTransform(-offset.X, -offset.Y);

        if (widthChanged)
            QueueBackgroundBlur();
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
        if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundBlurRadius))
        {
            QueueBackgroundBlur();
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.IsOpen))
        {
            if (_nowPlayingViewModel?.IsOpen != true)
            {
                CancelPendingBackgroundBlur();
                ClearCachedBackground();
            }

            SynchronizeThemeContent();
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.SelectedThemePreset) &&
            _nowPlayingViewModel?.IsOpen == true)
            LoadThemeContent();
    }

    private void OnBackgroundSourceImagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Image.SourceProperty)
            return;

        CancelPendingBackgroundBlur();
        _encodedBackgroundSource = null;

        if (e.NewValue is not Bitmap bitmap)
        {
            // Image loaders clear Source before applying a replacement. Keep the last
            // completed background visible during that gap instead of exposing black.
            if (_nowPlayingViewModel?.IsOpen != true)
                ClearCachedBackground();

            return;
        }

        try
        {
            using var stream = new MemoryStream();

            bitmap.Save(stream, PngBitmapEncoderOptions.Default);

            _encodedBackgroundSource = stream.ToArray();
            QueueBackgroundBlur(debounce: false);
        }
        catch
        {
            // A failed replacement must not discard the last usable background.
            if (_nowPlayingViewModel?.IsOpen != true)
                ClearCachedBackground();
        }
    }

    private async void QueueBackgroundBlur(bool debounce = true)
    {
        var source = _encodedBackgroundSource;
        var viewModel = _nowPlayingViewModel;
        if (source is null || viewModel?.IsOpen != true)
            return;

        CancelPendingBackgroundBlur();
        var cancellation = new CancellationTokenSource();
        _backgroundBlurCancellation = cancellation;

        try
        {
            if (debounce)
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellation.Token);

            var radius = viewModel.BackgroundBlurRadius;
            var displayWidth = Math.Max(1, SharedBackgroundFrame.Bounds.Width);
            var png = await Task.Run(
                () => StaticImageBlurProcessor.CreateBlurredPng(source, radius, displayWidth),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            using var stream = new MemoryStream(png, writable: false);
            var bitmap = new Bitmap(stream);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_backgroundBlurCancellation, cancellation))
            {
                bitmap.Dispose();
                return;
            }

            var previous = _cachedBlurredBackground;
            _cachedBlurredBackground = bitmap;
            CachedBackgroundImage.Source = bitmap;
            previous?.Dispose();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep the previous successfully generated background if a replacement fails.
        }
        finally
        {
            if (ReferenceEquals(_backgroundBlurCancellation, cancellation))
                _backgroundBlurCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelPendingBackgroundBlur()
    {
        var cancellation = _backgroundBlurCancellation;
        _backgroundBlurCancellation = null;
        cancellation?.Cancel();
    }

    private void ClearCachedBackground()
    {
        _encodedBackgroundSource = null;
        CachedBackgroundImage.Source = null;
        _cachedBlurredBackground?.Dispose();
        _cachedBlurredBackground = null;
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
            NowPlayingThemePreset.Sonnet => new SonnetNowPlayingThemeView(),
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

}
