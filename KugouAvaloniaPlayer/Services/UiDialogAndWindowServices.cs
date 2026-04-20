using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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

public sealed class LoginDialogService(ISukiDialogManager dialogManager) : ILoginDialogService
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

        if (Dispatcher.UIThread.CheckAccess())
            showAction();
        else
            Dispatcher.UIThread.Post(showAction);
    }
}

public sealed class DesktopLyricWindowService(
    IDesktopLyricViewModelFactory desktopLyricViewModelFactory,
    IDesktopLyricMousePassthroughService desktopLyricMousePassthroughService)
    : IDesktopLyricWindowService
{
    private DesktopLyricWindow? _lyricWindow;

    public bool IsOpen => _lyricWindow != null;
    public event Action<bool>? IsOpenChanged;

    public void Toggle()
    {
        if (Dispatcher.UIThread.CheckAccess())
            ToggleCore();
        else
            Dispatcher.UIThread.Post(ToggleCore);
    }

    public void Close()
    {
        if (Dispatcher.UIThread.CheckAccess())
            CloseCore();
        else
            Dispatcher.UIThread.Post(CloseCore);
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
            if (e.PropertyName != nameof(DesktopLyricViewModel.IsLocked)) return;
            desktopLyricMousePassthroughService.Apply(lyricWindow, lyricViewModel.IsLocked);
        };

        lyricViewModel.PropertyChanged += onLyricViewModelPropertyChanged;

        lyricWindow.Closed += (_, _) =>
        {
            desktopLyricMousePassthroughService.Apply(lyricWindow, false);
            lyricViewModel.PropertyChanged -= onLyricViewModelPropertyChanged;
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

        desktopLyricMousePassthroughService.Apply(_lyricWindow, false);
        _lyricWindow.Close();
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