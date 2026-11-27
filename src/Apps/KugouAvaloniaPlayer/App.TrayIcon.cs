using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using Vela.Avalonia.Controls;
using Vela.Avalonia.Tokens;

namespace KugouAvaloniaPlayer;

partial class App
{
    private PlayerViewModel? _playerViewModel;
    private MainWindowViewModel? _trayMainViewModel;
    private VelaTrayIcon? _trayIcon;
    private Bitmap? _trayIconBitmap;
    private VelaMenuItem? _playPauseTrayItem;
    private VelaMenuItem? _desktopLyricTrayItem;

    private void InitializeTrayIcon(PlayerViewModel player, IClassicDesktopStyleApplicationLifetime desktop,
        MainWindowViewModel mainWindowViewModel, IMainWindowService mainWindowService)
    {
        _playerViewModel = player;
        _trayMainViewModel = mainWindowViewModel;
        var playbackCommands = (IPlaybackCommands)player;
        var isLinux = OperatingSystem.IsLinux();

        var showItem = new VelaMenuItem
        {
            Header = "显示主界面",
            Icon = KugouIcons.Home
        };
        showItem.Click += (_, _) => mainWindowService.ShowMainWindow();

        _desktopLyricTrayItem = new VelaMenuItem
        {
            Header = "桌面歌词",
            Icon = KugouIcons.Lyrics,
            IsChecked = mainWindowViewModel.IsDesktopLyricEnabled
        };
        _desktopLyricTrayItem.Click += (_, _) =>
        {
            if (mainWindowViewModel.ToggleDesktopLyricCommand.CanExecute(null))
                mainWindowViewModel.ToggleDesktopLyricCommand.Execute(null);
        };

        var exitItem = new VelaMenuItem
        {
            Header = "退出",
            Icon = KugouIcons.WindowClose,
            IsDestructive = true
        };
        exitItem.Click += (_, _) =>
        {
            if (desktop.MainWindow is MainWindow mainWindowInstance)
                mainWindowInstance.CanClose = true;
            desktop.Shutdown();
        };

        _trayIcon = new VelaTrayIcon
        {
            ToolTipText = "KA Music",
            IsVisible = true,
            TargetWindow = desktop.MainWindow,
            CloseToTray = false,
            MinimizeToTray = false,
            Presentation = isLinux ? VelaTrayPresentation.Native : VelaTrayPresentation.VelaMenu,
            SurfaceMaterial = VelaSurfaceMaterial.Solid,
            FlyoutMinWidth = 280,
            FlyoutMaxWidth = 304,
            FlyoutPadding = new Thickness(8),
            IconThemeMode = VelaTrayIconThemeMode.Original
        };

        if (isLinux)
        {
            var iconUri = new Uri("avares://KugouAvaloniaPlayer/Assets/ka-music.ico");
            using var iconStream = AssetLoader.Open(iconUri);
            _trayIcon.Icon = new WindowIcon(iconStream);
            _trayIcon.Command = new RelayCommand(mainWindowService.ShowMainWindow);

            var prevItem = new VelaMenuItem
            {
                Header = "上一首",
                Icon = KugouIcons.SkipPrevious
            };
            prevItem.Click += (_, _) => _ = playbackCommands.PlayPreviousAsync();

            _playPauseTrayItem = new VelaMenuItem
            {
                Header = "播放",
                Icon = KugouIcons.Play
            };
            _playPauseTrayItem.Click += (_, _) => playbackCommands.TogglePlayPause();

            var nextItem = new VelaMenuItem
            {
                Header = "下一首",
                Icon = KugouIcons.SkipNext
            };
            nextItem.Click += (_, _) => _ = playbackCommands.PlayNextAsync();

            _trayIcon.MenuItems.Add(showItem);
            _trayIcon.MenuItems.Add(_desktopLyricTrayItem);
            _trayIcon.MenuItems.Add(new VelaMenuItem { IsSeparator = true });
            _trayIcon.MenuItems.Add(prevItem);
            _trayIcon.MenuItems.Add(_playPauseTrayItem);
            _trayIcon.MenuItems.Add(nextItem);
            _trayIcon.MenuItems.Add(new VelaMenuItem { IsSeparator = true });
            _trayIcon.MenuItems.Add(exitItem);
        }
        else
        {
            var logoUri = new Uri("avares://KugouAvaloniaPlayer/Assets/ka-logo.png");
            using var logoStream = AssetLoader.Open(logoUri);
            _trayIconBitmap = new Bitmap(logoStream);
            _trayIcon.IconBitmap = _trayIconBitmap;

            var playbackPanel = new TrayPlaybackPanel
            {
                DataContext = mainWindowViewModel
            };
            playbackPanel.ShowMainWindowRequested += (_, _) =>
            {
                _trayIcon?.HideFlyout();
                mainWindowService.ShowMainWindow();
            };

            _trayIcon.FlyoutHeader = playbackPanel;
            _trayIcon.MenuItems.Add(showItem);
            _trayIcon.MenuItems.Add(_desktopLyricTrayItem);
            _trayIcon.MenuItems.Add(new VelaMenuItem { IsSeparator = true });
            _trayIcon.MenuItems.Add(exitItem);
        }

        _trayIcon.Attach(this);

        player.PropertyChanged += OnPlayerPropertyChanged;
        mainWindowViewModel.PropertyChanged += OnTrayMainViewModelPropertyChanged;

        UpdateTrayPlaybackState();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.IsPlayingAudio)
            or nameof(PlayerViewModel.DisplayedPlayingSong))
        {
            UpdateTrayPlaybackState();
        }
    }

    private void OnTrayMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDesktopLyricEnabled))
            UpdateDesktopLyricMenuState();
    }

    private void UpdateTrayPlaybackState()
    {
        if (_playerViewModel is null || _trayIcon is null)
            return;

        Dispatcher.Post(() =>
        {
            if (_playerViewModel is null || _trayIcon is null)
                return;

            var isPlaying = _playerViewModel.IsPlayingAudio;

            if (_playPauseTrayItem is not null)
            {
                _playPauseTrayItem.Header = isPlaying ? "暂停" : "播放";
                _playPauseTrayItem.Icon = isPlaying ? KugouIcons.Pause : KugouIcons.Play;
            }

            if (!OperatingSystem.IsLinux())
            {
                _trayIcon.BadgeVariant = isPlaying ? VelaTrayBadgeVariant.Dot : VelaTrayBadgeVariant.None;
                _trayIcon.BadgeTone = VelaTrayBadgeTone.Success;
            }

            var song = _playerViewModel.DisplayedPlayingSong;
            if (song is not null && !string.IsNullOrWhiteSpace(song.DisplayTitle))
            {
                var status = isPlaying ? "正在播放" : "已暂停";
                var artist = string.IsNullOrWhiteSpace(song.Singer) ? string.Empty : $" - {song.Singer}";
                _trayIcon.ToolTipText = $"{status}: {song.DisplayTitle}{artist} · KA Music";
            }
            else
            {
                _trayIcon.ToolTipText = "KA Music";
            }
        });
    }

    private void UpdateDesktopLyricMenuState()
    {
        if (_desktopLyricTrayItem is null || _trayMainViewModel is null)
            return;

        Dispatcher.Post(() =>
        {
            if (_desktopLyricTrayItem is not null && _trayMainViewModel is not null)
                _desktopLyricTrayItem.IsChecked = _trayMainViewModel.IsDesktopLyricEnabled;
        });
    }

    private void ShutdownTrayIcon()
    {
        if (_playerViewModel is not null)
        {
            _playerViewModel.PropertyChanged -= OnPlayerPropertyChanged;
            _playerViewModel = null;
        }

        if (_trayMainViewModel is not null)
        {
            _trayMainViewModel.PropertyChanged -= OnTrayMainViewModelPropertyChanged;
            _trayMainViewModel = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayIconBitmap?.Dispose();
        _trayIconBitmap = null;
        _playPauseTrayItem = null;
        _desktopLyricTrayItem = null;
    }
}
