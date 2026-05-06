using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ILoginDialogService
{
    void ShowLoginDialog(LoginViewModel loginViewModel);
}

public interface IDesktopLyricWindowService
{
    bool IsOpen { get; }
    event Action<bool>? IsOpenChanged;
    void Toggle();
    void Close();
}

public interface IMainWindowService
{
    Window? MainWindow { get; }
    void ShowMainWindow();
}

public sealed class LoginDialogService(ISukiDialogManager dialogManager, IUiDispatcherService uiDispatcher) : ILoginDialogService
{
    public void ShowLoginDialog(LoginViewModel loginViewModel)
    {
        var showAction = () =>
        {
            var loginView = new LoginView
            {
                DataContext = loginViewModel
            };

            dialogManager.CreateDialog()
                .WithContent(loginView)
                .WithActionButton("关闭", _ => { }, true, "Basic")
                .TryShow();
        };

        uiDispatcher.RunOrPost(showAction);
    }
}

public sealed class DesktopLyricWindowService(
    IDesktopLyricViewModelFactory desktopLyricViewModelFactory,
    IDesktopLyricMousePassthroughService desktopLyricMousePassthroughService,
    IUiDispatcherService uiDispatcher)
    : IDesktopLyricWindowService
{
    private const int CollapsedIconSize = 40;
    private const int CollapsedIconTopMargin = 12;

    private DesktopLyricWindow? _lyricWindow;
    private DesktopLyricLockOverlayWindow? _lockOverlayWindow;
    private bool _isSynchronizingWindowPositions;

    public bool IsOpen => _lyricWindow != null;
    public event Action<bool>? IsOpenChanged;

    public void Toggle()
    {
        uiDispatcher.RunOrPost(ToggleCore);
    }

    public void Close()
    {
        uiDispatcher.RunOrPost(CloseCore);
    }

    private void ToggleCore()
    {
        if (_lyricWindow == null)
            ShowCore();
        else
            CloseCore();
    }

    private void ShowCore()
    {
        var lyricViewModel = desktopLyricViewModelFactory.Create();
        var lyricWindow = new DesktopLyricWindow
        {
            DataContext = lyricViewModel
        };

        PropertyChangedEventHandler onLyricViewModelPropertyChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(DesktopLyricViewModel.IsLocked)
                or nameof(DesktopLyricViewModel.IsControlBarExpanded)
                or nameof(DesktopLyricViewModel.IsCollapsedLockIconHovered))
            {
                UpdateHitTestState(lyricWindow, lyricViewModel);
            }
        };

        lyricViewModel.PropertyChanged += onLyricViewModelPropertyChanged;

        lyricWindow.Opened += (_, _) => UpdateHitTestState(lyricWindow, lyricViewModel);
        lyricWindow.PositionChanged += (_, _) =>
        {
            if (_isSynchronizingWindowPositions) return;
            SyncOverlayPositionFromLyricWindow();
        };

        lyricWindow.Closed += (_, _) =>
        {
            desktopLyricMousePassthroughService.Apply(lyricWindow, DesktopLyricHitTestLayout.FullWindow);
            CloseLockOverlayWindow();
            lyricViewModel.PropertyChanged -= onLyricViewModelPropertyChanged;
            lyricViewModel.Dispose();
            if (ReferenceEquals(_lyricWindow, lyricWindow))
                _lyricWindow = null;
            IsOpenChanged?.Invoke(false);
        };

        _lyricWindow = lyricWindow;
        lyricWindow.Show();
        IsOpenChanged?.Invoke(true);
    }

    private void CloseCore()
    {
        if (_lyricWindow == null) return;

        desktopLyricMousePassthroughService.Apply(_lyricWindow, DesktopLyricHitTestLayout.FullWindow);
        CloseLockOverlayWindow();
        _lyricWindow.Close();
    }

    private void UpdateHitTestState(Window lyricWindow, DesktopLyricViewModel lyricViewModel)
    {
        if (!desktopLyricMousePassthroughService.IsSupported)
            return;

        if (!lyricViewModel.IsLocked)
        {
            CloseLockOverlayWindow();
            desktopLyricMousePassthroughService.Apply(lyricWindow, DesktopLyricHitTestLayout.FullWindow);
            return;
        }

        if (desktopLyricMousePassthroughService.SupportsSelectiveHitTesting)
        {
            CloseLockOverlayWindow();
            desktopLyricMousePassthroughService.Apply(
                lyricWindow,
                DesktopLyricHitTestLayout.ForRegions(GetCollapsedIconRegion(lyricWindow)));
            return;
        }

        desktopLyricMousePassthroughService.Apply(lyricWindow, DesktopLyricHitTestLayout.Transparent);
        EnsureLockOverlayWindow(lyricViewModel);
        SyncOverlayPositionFromLyricWindow();
    }

    private void EnsureLockOverlayWindow(DesktopLyricViewModel lyricViewModel)
    {
        if (_lockOverlayWindow != null || _lyricWindow == null)
            return;

        var overlayWindow = new DesktopLyricLockOverlayWindow
        {
            DataContext = lyricViewModel,
            Position = GetOverlayPosition(_lyricWindow)
        };

        overlayWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_lockOverlayWindow, overlayWindow))
                _lockOverlayWindow = null;
        };

        _lockOverlayWindow = overlayWindow;
        overlayWindow.Show();
        overlayWindow.PositionChanged += (_, _) =>
        {
            if (_isSynchronizingWindowPositions) return;
            SyncLyricWindowPositionFromOverlay();
        };
    }

    private void CloseLockOverlayWindow()
    {
        if (_lockOverlayWindow == null)
            return;

        var overlayWindow = _lockOverlayWindow;
        _lockOverlayWindow = null;
        overlayWindow.Close();
    }

    private void SyncOverlayPositionFromLyricWindow()
    {
        if (_lyricWindow == null || _lockOverlayWindow == null)
            return;

        _isSynchronizingWindowPositions = true;
        try
        {
            _lockOverlayWindow.Position = GetOverlayPosition(_lyricWindow);
        }
        finally
        {
            _isSynchronizingWindowPositions = false;
        }
    }

    private void SyncLyricWindowPositionFromOverlay()
    {
        if (_lyricWindow == null || _lockOverlayWindow == null)
            return;

        _isSynchronizingWindowPositions = true;
        try
        {
            _lyricWindow.Position = GetLyricWindowPosition(_lyricWindow, _lockOverlayWindow);
        }
        finally
        {
            _isSynchronizingWindowPositions = false;
        }
    }

    private static PixelRect GetCollapsedIconRegion(Window lyricWindow)
    {
        var width = (int)Math.Ceiling(lyricWindow.Bounds.Width);
        var x = Math.Max((width - CollapsedIconSize) / 2, 0);
        return new PixelRect(x, CollapsedIconTopMargin, CollapsedIconSize, CollapsedIconSize);
    }

    private static PixelPoint GetOverlayPosition(Window lyricWindow)
    {
        var region = GetCollapsedIconRegion(lyricWindow);
        return new PixelPoint(
            lyricWindow.Position.X + region.X - (CollapsedIconSize - region.Width) / 2,
            lyricWindow.Position.Y + region.Y - (CollapsedIconSize - region.Height) / 2);
    }

    private static PixelPoint GetLyricWindowPosition(Window lyricWindow, Window overlayWindow)
    {
        var region = GetCollapsedIconRegion(lyricWindow);
        return new PixelPoint(
            overlayWindow.Position.X - region.X + (CollapsedIconSize - region.Width) / 2,
            overlayWindow.Position.Y - region.Y + (CollapsedIconSize - region.Height) / 2);
    }
}

public sealed class MainWindowService : IMainWindowService
{
    public Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void ShowMainWindow()
    {
        var window = MainWindow;
        if (window == null)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        if (!window.IsVisible)
            window.Show();

        window.Activate();
    }
}
