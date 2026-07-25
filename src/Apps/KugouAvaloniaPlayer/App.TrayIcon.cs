using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;

namespace KugouAvaloniaPlayer;

partial class App
{
    private PlayerViewModel? _playerViewModel;
    private NativeMenuItem? _playPauseItem;
    private TrayIcon? _trayIcon;

    private void InitializeTrayIcon(PlayerViewModel player, IClassicDesktopStyleApplicationLifetime desktop,
        MainWindowViewModel mainWindowViewModel, IMainWindowService mainWindowService)
    {
        _playerViewModel = player;
        var playbackCommands = (IPlaybackCommands)player;

        var iconUri = new Uri("avares://KugouAvaloniaPlayer/Assets/Test.ico");
        using var iconStream = AssetLoader.Open(iconUri);
        var icon = new WindowIcon(iconStream);

        var showItem = new NativeMenuItem("显示主界面");

        showItem.Click += (_, _) => mainWindowService.ShowMainWindow();

        var showLyric = new NativeMenuItem("桌面歌词");
        showLyric.Click += (s, e) =>
        {
            if (mainWindowViewModel.ToggleDesktopLyricCommand.CanExecute(null))
                mainWindowViewModel.ToggleDesktopLyricCommand.Execute(null);
        };

        var sep1 = new NativeMenuItemSeparator();

        var prevItem = new NativeMenuItem("上一首");
        prevItem.Click += (_, _) => _ = playbackCommands.PlayPreviousAsync();

        _playPauseItem = new NativeMenuItem("播放");
        _playPauseItem.Click += (_, _) => playbackCommands.TogglePlayPause();

        var nextItem = new NativeMenuItem("下一首");
        nextItem.Click += (_, _) => _ = playbackCommands.PlayNextAsync();

        var sep2 = new NativeMenuItemSeparator();

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (s, e) =>
        {
            if (desktop.MainWindow is MainWindow mainWindowInstance) mainWindowInstance.CanClose = true;
            desktop.Shutdown();
        };

        var menu = new NativeMenu
        {
            showItem,
            showLyric,
            sep1,
            prevItem,
            _playPauseItem,
            nextItem,
            sep2,
            exitItem
        };

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "KA Music",
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => mainWindowService.ShowMainWindow();

        player.PropertyChanged += OnPlayerPropertyChanged;

        UpdatePlayPauseText();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlayingAudio)) UpdatePlayPauseText();
    }

    private void UpdatePlayPauseText()
    {
        if (_playPauseItem != null && _playerViewModel != null)
            Dispatcher.Post(() => { _playPauseItem.Header = _playerViewModel.IsPlayingAudio ? "暂停" : "播放"; });
    }

    private void ShutdownTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_playerViewModel != null) _playerViewModel.PropertyChanged -= OnPlayerPropertyChanged;
    }
}
