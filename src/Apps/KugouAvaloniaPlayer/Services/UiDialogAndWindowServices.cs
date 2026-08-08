using System;
using System.ComponentModel;
using ZLinq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Models;
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
    void Minimize();
    void ToggleFullScreen();
    void ToggleMaximize();
    void Close();
    void ApplyLinuxWindowDecorations(bool useFullDecorations);
}

internal static class MainWindowPresentationHelper
{
    public static void ShowAndActivate(Window? window)
    {
        if (window == null)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        if (!window.IsVisible)
            window.Show();

        window.Activate();
    }
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
    IDesktopLyricWindowChromeService desktopLyricWindowChromeService,
    IUiDispatcherService uiDispatcher)
    : IDesktopLyricWindowService
{
    private const int CollapsedIconSize = 40;
    private const int CollapsedIconTopMargin = 12;

    private DesktopLyricWindow? _lyricWindow;
    private DesktopLyricLockOverlayWindow? _lockOverlayWindow;
    private bool _isSynchronizingWindowPositions;
    private DesktopLyricLayoutMode _activeLayoutMode;

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
        _activeLayoutMode = lyricViewModel.LayoutMode;
        ApplyWindowSize(lyricWindow, lyricViewModel);
        RestoreLyricWindowPosition(lyricWindow, _activeLayoutMode);

        PropertyChangedEventHandler onLyricViewModelPropertyChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(DesktopLyricViewModel.IsLocked)
                or nameof(DesktopLyricViewModel.IsControlBarExpanded)
                or nameof(DesktopLyricViewModel.IsCollapsedLockIconHovered))
            {
                UpdateHitTestState(lyricWindow, lyricViewModel);
            }
            else if (e.PropertyName == nameof(DesktopLyricViewModel.LayoutMode))
            {
                SwitchWindowLayout(lyricWindow, lyricViewModel);
            }
            else if (e.PropertyName is nameof(DesktopLyricViewModel.WindowHeight)
                     or nameof(DesktopLyricViewModel.VerticalDesiredWidth))
            {
                ApplyWindowSize(lyricWindow, lyricViewModel);
                ClampWindowToWorkingArea(lyricWindow);
                UpdateHitTestState(lyricWindow, lyricViewModel);
                SyncOverlayPositionFromLyricWindow();
            }
        };

        lyricViewModel.PropertyChanged += onLyricViewModelPropertyChanged;

        lyricWindow.Opened += (_, _) =>
        {
            desktopLyricWindowChromeService.HideFromWindowSwitcher(lyricWindow);
            UpdateHitTestState(lyricWindow, lyricViewModel);
        };
        lyricWindow.PositionChanged += (_, _) =>
        {
            if (_isSynchronizingWindowPositions) return;
            CaptureLyricWindowPosition(lyricWindow, _activeLayoutMode);
            SyncOverlayPositionFromLyricWindow();
        };

        lyricWindow.Closed += (_, _) =>
        {
            CaptureLyricWindowPosition(lyricWindow, _activeLayoutMode);
            SettingsManager.Save();
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
        overlayWindow.Opened += (_, _) => desktopLyricWindowChromeService.HideFromWindowSwitcher(overlayWindow);

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
            CaptureLyricWindowPosition(_lyricWindow, _activeLayoutMode);
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

    private void SwitchWindowLayout(DesktopLyricWindow lyricWindow, DesktopLyricViewModel lyricViewModel)
    {
        if (_activeLayoutMode == lyricViewModel.LayoutMode)
            return;

        var previousCenter = new PixelPoint(
            lyricWindow.Position.X + (int)Math.Round(lyricWindow.Bounds.Width * lyricWindow.RenderScaling / 2d),
            lyricWindow.Position.Y + (int)Math.Round(lyricWindow.Bounds.Height * lyricWindow.RenderScaling / 2d));
        CaptureLyricWindowPosition(lyricWindow, _activeLayoutMode);
        _activeLayoutMode = lyricViewModel.LayoutMode;

        _isSynchronizingWindowPositions = true;
        try
        {
            ApplyWindowSize(lyricWindow, lyricViewModel);
            if (!RestoreLyricWindowPosition(lyricWindow, _activeLayoutMode))
            {
                lyricWindow.Position = new PixelPoint(
                    previousCenter.X - (int)Math.Round(lyricWindow.Width * lyricWindow.RenderScaling / 2d),
                    previousCenter.Y - (int)Math.Round(lyricWindow.Height * lyricWindow.RenderScaling / 2d));
                ClampWindowToWorkingArea(lyricWindow);
            }
        }
        finally
        {
            _isSynchronizingWindowPositions = false;
        }

        CaptureLyricWindowPosition(lyricWindow, _activeLayoutMode);
        UpdateHitTestState(lyricWindow, lyricViewModel);
        SyncOverlayPositionFromLyricWindow();
    }

    private static void ApplyWindowSize(DesktopLyricWindow lyricWindow, DesktopLyricViewModel lyricViewModel)
    {
        if (lyricViewModel.LayoutMode == DesktopLyricLayoutMode.Horizontal)
        {
            lyricWindow.Width = 1024;
            lyricWindow.Height = lyricViewModel.WindowHeight;
            return;
        }

        var screen = GetScreenForWindow(lyricWindow);
        var scaling = screen?.Scaling ?? lyricWindow.RenderScaling;
        var workWidth = screen == null ? 1280d : screen.WorkingArea.Width / scaling;
        var workHeight = screen == null ? 900d : screen.WorkingArea.Height / scaling;
        var maximumHeight = Math.Max(360d, workHeight - 48d);
        var targetHeight = Math.Clamp(workHeight * 0.78d, 520d, 800d);
        lyricWindow.Height = Math.Min(targetHeight, maximumHeight);
        lyricViewModel.ConfigureVerticalContentHeight(lyricWindow.Height - 140d);
        lyricWindow.Width = Math.Clamp(
            lyricViewModel.VerticalDesiredWidth,
            DesktopLyricViewModel.VerticalBaseWindowWidth,
            Math.Max(DesktopLyricViewModel.VerticalBaseWindowWidth, workWidth - 48d));
    }

    private static void CaptureLyricWindowPosition(Window lyricWindow, DesktopLyricLayoutMode mode)
    {
        var position = GetPositionSettings(mode);
        position.HasValue = true;
        position.X = lyricWindow.Position.X;
        position.Y = lyricWindow.Position.Y;
    }

    private static bool RestoreLyricWindowPosition(Window lyricWindow, DesktopLyricLayoutMode mode)
    {
        var position = GetPositionSettings(mode);
        if (!position.HasValue)
            return false;

        var savedPosition = new PixelPoint(position.X, position.Y);
        if (IsVisibleOnAnyScreen(lyricWindow, savedPosition))
        {
            lyricWindow.Position = savedPosition;
            ClampWindowToWorkingArea(lyricWindow);
            return true;
        }

        return false;
    }

    private static DesktopLyricWindowPositionSettings GetPositionSettings(DesktopLyricLayoutMode mode)
    {
        return mode == DesktopLyricLayoutMode.Vertical
            ? SettingsManager.Settings.VerticalDesktopLyricWindowPosition
            : SettingsManager.Settings.DesktopLyricWindowPosition;
    }

    private static void ClampWindowToWorkingArea(Window window)
    {
        var screen = GetScreenForWindow(window);
        if (screen == null)
            return;

        var width = (int)Math.Ceiling(window.Width * screen.Scaling);
        var height = (int)Math.Ceiling(window.Height * screen.Scaling);
        var area = screen.WorkingArea;
        window.Position = new PixelPoint(
            Math.Clamp(window.Position.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private static Screen? GetScreenForWindow(Window window)
    {
        foreach (var screen in window.Screens.All)
        {
            if (screen.WorkingArea.Contains(window.Position) || screen.Bounds.Contains(window.Position))
                return screen;
        }

        return window.Screens.Primary;
    }

    private static bool IsVisibleOnAnyScreen(Window window, PixelPoint position)
    {
        return window.Screens.All.AsValueEnumerable().Any(screen =>
            screen.Bounds.Contains(position) || screen.WorkingArea.Contains(position));
    }
}

public sealed class MainWindowService(IUiDispatcherService uiDispatcher) : IMainWindowService
{
    public Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void ShowMainWindow()
    {
        uiDispatcher.RunOrPost(() => MainWindowPresentationHelper.ShowAndActivate(MainWindow));
    }

    public void Minimize()
    {
        uiDispatcher.RunOrPost(() =>
        {
            if (MainWindow is { } window)
                window.WindowState = WindowState.Minimized;
        });
    }

    public void ToggleFullScreen()
    {
        uiDispatcher.RunOrPost(() =>
        {
            if (MainWindow is KugouWindow window)
                window.ToggleFullScreen();
        });
    }

    public void ToggleMaximize()
    {
        uiDispatcher.RunOrPost(() =>
        {
            if (MainWindow is KugouWindow window)
                window.ToggleMaximizeOrZoom();
        });
    }

    public void Close()
    {
        uiDispatcher.RunOrPost(() => MainWindow?.Close());
    }

    public void ApplyLinuxWindowDecorations(bool useFullDecorations)
    {
        uiDispatcher.RunOrPost(() =>
        {
            if (MainWindow is MainWindow window)
                window.ApplyLinuxWindowDecorations(useFullDecorations);
        });
    }
}
