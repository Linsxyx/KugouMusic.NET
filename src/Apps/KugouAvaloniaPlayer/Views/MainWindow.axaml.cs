using System;
#if KUGOU_WINDOWS
using System.ComponentModel;
#endif
using System.IO;
using ZLinq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Controls;
using Size = Avalonia.Size;

namespace KugouAvaloniaPlayer.Views;

public partial class MainWindow : SukiWindow
{
    private PixelPoint? _lastNormalPosition;
    private Size? _lastNormalSize;

#if KUGOU_WINDOWS
    private WindowsTaskbarThumbnailToolbar? _taskbarToolbar;
    private bool _taskbarButtonsInitialized;
    private PlayerViewModel? _taskbarPlayer;
#endif


    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowState();
/*#if DEBUG
        RendererDiagnostics.DebugOverlays = RendererDebugOverlays.Fps
                                            | RendererDebugOverlays.LayoutTimeGraph
                                            | RendererDebugOverlays.RenderTimeGraph;
#endif*/

        PositionChanged += (_, _) => CaptureNormalBounds();
        WeakReferenceMessenger.Default.Register<MainWindowChromeActionMessage>(this,
            (_, message) => { ApplyChromeAction(message.Action); });
    }

    public bool CanClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var behavior = SettingsManager.Settings.CloseBehavior;
        if (behavior == CloseBehavior.MinimizeToTray && !CanClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (DataContext is MainWindowViewModel vm) vm.ForceCloseDesktopLyric();

        SaveWindowState();

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
#if KUGOU_WINDOWS
        if (_taskbarPlayer != null)
        {
            _taskbarPlayer.PropertyChanged -= OnTaskbarPlayerPropertyChanged;
            _taskbarPlayer = null;
        }

        _taskbarToolbar?.Dispose();
        _taskbarToolbar = null;
#endif

        WeakReferenceMessenger.Default.UnregisterAll(this);
        base.OnClosed(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
#if KUGOU_WINDOWS
        InitializeTaskbarThumbnailToolbar();
#endif
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty || change.Property == WindowStateProperty)
            CaptureNormalBounds();
    }

    private void ApplyChromeAction(MainWindowChromeAction action)
    {
        switch (action)
        {
            case MainWindowChromeAction.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case MainWindowChromeAction.ToggleFullScreen:
                ToggleFullScreen();
                break;
            case MainWindowChromeAction.ToggleMaximize:
                if (WindowState == WindowState.FullScreen)
                    break;

                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                break;
            case MainWindowChromeAction.Close:
                Close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private void SaveWindowState()
    {
        var settings = SettingsManager.Settings.MainWindowState;
        var state = WindowState;

        settings.HasValue = true;
        settings.State = state == WindowState.Maximized
            ? SavedMainWindowState.Maximized
            : SavedMainWindowState.Normal;

        if (state == WindowState.Normal)
        {
            StoreBounds(settings, Position, new Size(Width, Height));
        }
        else
        {
            StoreBounds(
                settings,
                _lastNormalPosition ?? Position,
                _lastNormalSize ?? new Size(Width, Height));
        }

        SettingsManager.Save();
    }

    private void RestoreWindowState()
    {
        var settings = SettingsManager.Settings.MainWindowState;
        if (!settings.HasValue || !IsValidSavedSize(settings))
            return;

        Width = Math.Max(settings.Width, MinWidth);
        Height = Math.Max(settings.Height, MinHeight);
        _lastNormalSize = new Size(Width, Height);

        var position = new PixelPoint(settings.X, settings.Y);
        if (IsVisibleOnAnyScreen(position))
        {
            Position = position;
            _lastNormalPosition = position;
        }

        if (settings.State == SavedMainWindowState.Maximized)
            WindowState = WindowState.Maximized;
    }

    private static bool IsValidSavedSize(MainWindowStateSettings settings)
    {
        return double.IsFinite(settings.Width)
               && double.IsFinite(settings.Height)
               && settings is { Width: > 0, Height: > 0 };
    }

    private bool IsVisibleOnAnyScreen(PixelPoint position)
    {
        return Screens.All.AsValueEnumerable().Any(screen =>
            screen.Bounds.Contains(position) || screen.WorkingArea.Contains(position));
    }

    private void CaptureNormalBounds()
    {
        if (WindowState != WindowState.Normal || !IsValidSize(Width, Height))
            return;

        _lastNormalPosition = Position;
        _lastNormalSize = new Size(Width, Height);
    }

    private void StoreBounds(MainWindowStateSettings settings, PixelPoint position, Size size)
    {
        settings.Width = Math.Max(size.Width, MinWidth);
        settings.Height = Math.Max(size.Height, MinHeight);
        settings.X = position.X;
        settings.Y = position.Y;
    }

    private static bool IsValidSize(double width, double height)
    {
        return double.IsFinite(width)
               && double.IsFinite(height)
               && width > 0
               && height > 0;
    }

#if KUGOU_WINDOWS
    private void InitializeTaskbarThumbnailToolbar()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (_taskbarButtonsInitialized)
            return;

        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is null)
            return;

        if (platformHandle.HandleDescriptor != "HWND")
            return;

        var hwnd = platformHandle.Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var baseDir = AppContext.BaseDirectory;
        var previousIconPath = Path.Combine(baseDir, "Assets", "prev.ico");
        var playIconPath = Path.Combine(baseDir, "Assets", "play.ico");
        var pauseIconPath = Path.Combine(baseDir, "Assets", "pause.ico");
        var nextIconPath = Path.Combine(baseDir, "Assets", "next.ico");
        var heartGreyIconPath = Path.Combine(baseDir, "Assets", "heart_grey.ico");
        var heartRedIconPath = Path.Combine(baseDir, "Assets", "heart_red.ico");

        _taskbarToolbar = new WindowsTaskbarThumbnailToolbar(
            this,
            hwnd,
            previousIconPath,
            playIconPath,
            pauseIconPath,
            nextIconPath,
            heartGreyIconPath,
            heartRedIconPath,
            HandleTaskbarThumbnailButtonClick);

        if (!_taskbarToolbar.Initialize()) return;
        if (DataContext is MainWindowViewModel { Player: { } player })
        {
            _taskbarPlayer = player;
            _taskbarPlayer.PropertyChanged += OnTaskbarPlayerPropertyChanged;
            _taskbarToolbar.UpdatePlayPause(player.IsPlayingAudio);
            _taskbarToolbar.UpdateLike(player.IsLiked, player.CurrentPlayingSong != null);
        }

        _taskbarButtonsInitialized = true;
    }

    private void OnTaskbarPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_taskbarToolbar == null || _taskbarPlayer == null)
            return;

        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.IsPlayingAudio):
                _taskbarToolbar.UpdatePlayPause(_taskbarPlayer.IsPlayingAudio);
                return;
            case nameof(PlayerViewModel.IsLiked) or nameof(PlayerViewModel.CurrentPlayingSong):
                _taskbarToolbar.UpdateLike(_taskbarPlayer.IsLiked, _taskbarPlayer.CurrentPlayingSong != null);
                break;
        }
    }

    private static void HandleTaskbarThumbnailButtonClick(uint buttonId)
    {
        switch (buttonId)
        {
            case WindowsTaskbarThumbnailToolbar.PreviousButtonId:
                WeakReferenceMessenger.Default.Send(
                    new PlaybackControlMessage(PlaybackControlAction.PreviousTrack));
                break;

            case WindowsTaskbarThumbnailToolbar.PlayPauseButtonId:
                WeakReferenceMessenger.Default.Send(
                    new PlaybackControlMessage(PlaybackControlAction.TogglePlayPause));
                break;

            case WindowsTaskbarThumbnailToolbar.NextButtonId:
                WeakReferenceMessenger.Default.Send(
                    new PlaybackControlMessage(PlaybackControlAction.NextTrack));
                break;
            case WindowsTaskbarThumbnailToolbar.LikeButtonId:
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel { Player: { } player } })
                    player.ToggleLikeCommand.Execute(null);
                break;
        }
    }
#endif
}
