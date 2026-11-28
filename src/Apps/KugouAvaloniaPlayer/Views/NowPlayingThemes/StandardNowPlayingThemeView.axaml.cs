using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

public partial class StandardNowPlayingThemeView : UserControl
{
    private NowPlayingViewModel? _nowPlayingViewModel;
    private PlayerViewModel? _playerViewModel;

    public StandardNowPlayingThemeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookViewModel();
        RebuildMoreGlassMenu();
        if (_nowPlayingViewModel?.IsOpen == true)
            Dispatcher.Post(() => LyricScrollView.ForceSecondPassLayout(), DispatcherPriority.Render);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseMoreGlassMenu();
        UnhookViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookViewModel();
        RebuildMoreGlassMenu();
    }

    private void HookViewModel()
    {
        var viewModel = DataContext as NowPlayingViewModel;
        if (ReferenceEquals(_nowPlayingViewModel, viewModel))
            return;

        UnhookViewModel();
        _nowPlayingViewModel = viewModel;
        _playerViewModel = _nowPlayingViewModel?.Player;
        if (_nowPlayingViewModel != null)
            _nowPlayingViewModel.PropertyChanged += OnNowPlayingPropertyChanged;
        if (_playerViewModel != null)
        {
            _playerViewModel.PropertyChanged += OnPlayerPropertyChanged;
            _playerViewModel.RenderLyricLines.CollectionChanged += OnLyricLinesChanged;
        }
    }

    private void UnhookViewModel()
    {
        if (_playerViewModel != null)
        {
            _playerViewModel.PropertyChanged -= OnPlayerPropertyChanged;
            _playerViewModel.RenderLyricLines.CollectionChanged -= OnLyricLinesChanged;
        }
        if (_nowPlayingViewModel != null)
            _nowPlayingViewModel.PropertyChanged -= OnNowPlayingPropertyChanged;
        _nowPlayingViewModel = null;
        _playerViewModel = null;
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.IsOpen))
        {
            if (_nowPlayingViewModel?.IsOpen != true)
            {
                CloseMoreGlassMenu();
                return;
            }

            RebuildMoreGlassMenu();
            Dispatcher.Post(() => LyricScrollView.ForceSecondPassLayout(), DispatcherPriority.Render);
            return;
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.NowPlayingLyricDisplayMode)
            or nameof(NowPlayingViewModel.IsPortraitModeEnabled)
            or nameof(NowPlayingViewModel.HasCurrentSinger)
            or nameof(NowPlayingViewModel.CanAddCurrentSongToPlaylist))
        {
            RebuildMoreGlassMenu();
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DisplayedPlayingSong))
            RebuildMoreGlassMenu();
    }

    private void OnLyricLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_nowPlayingViewModel?.IsOpen != true || _playerViewModel?.RenderLyricLines.Count <= 0)
            return;

        Dispatcher.Post(() => LyricScrollView.ForceSecondPassLayout(), DispatcherPriority.Render);
    }

    private void CloseMoreGlassMenu()
    {
        if (MoreGlassMenu != null)
            MoreGlassMenu.IsOpen = false;
    }

    private void RebuildMoreGlassMenu()
    {
        if (MoreGlassMenu == null || _nowPlayingViewModel == null)
            return;

        var vm = _nowPlayingViewModel;
        MoreGlassMenu.Items.Clear();

        // 1. 歌词显示模式（原位二级子菜单，直接勾选切换）
        var lyricModeItem = new VelaMenuItem
        {
            Header = $"歌词 · {vm.CurrentLyricDisplayModeText}",
            Icon = KugouIcons.Lyrics
        };
        lyricModeItem.Items.Add(new VelaMenuItem
        {
            Header = "仅歌词",
            IsChecked = vm.NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsOnly,
            Command = vm.SetLyricDisplayModeCommand,
            CommandParameter = NowPlayingLyricDisplayMode.LyricsOnly
        });
        lyricModeItem.Items.Add(new VelaMenuItem
        {
            Header = "歌词 + 翻译",
            IsChecked = vm.NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithTranslation,
            Command = vm.SetLyricDisplayModeCommand,
            CommandParameter = NowPlayingLyricDisplayMode.LyricsWithTranslation
        });
        lyricModeItem.Items.Add(new VelaMenuItem
        {
            Header = "歌词 + 音译",
            IsChecked = vm.NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithRomanization,
            Command = vm.SetLyricDisplayModeCommand,
            CommandParameter = NowPlayingLyricDisplayMode.LyricsWithRomanization
        });
        MoreGlassMenu.Items.Add(lyricModeItem);

        // 2. 跳转歌手（单歌手直达，多歌手原位子菜单）
        var singers = vm.Player.DisplayedPlayingSong?.Singers;
        if (singers is { Count: 1 })
        {
            MoreGlassMenu.Items.Add(new VelaMenuItem
            {
                Header = $"歌手 · {singers[0].Name}",
                Icon = KugouIcons.Singer,
                Command = vm.ViewSingerCommand,
                CommandParameter = singers[0]
            });
        }
        else if (singers is { Count: > 1 })
        {
            var singerMenuItem = new VelaMenuItem
            {
                Header = $"跳转歌手 ({singers.Count})",
                Icon = KugouIcons.Singer
            };
            foreach (var singer in singers)
            {
                singerMenuItem.Items.Add(new VelaMenuItem
                {
                    Header = string.IsNullOrWhiteSpace(singer.Name) ? "未知歌手" : singer.Name,
                    Icon = KugouIcons.Singer,
                    Command = vm.ViewSingerCommand,
                    CommandParameter = singer
                });
            }
            MoreGlassMenu.Items.Add(singerMenuItem);
        }

        MoreGlassMenu.Items.Add(new VelaMenuItem { IsSeparator = true });

        // 3. 写真模式切换
        MoreGlassMenu.Items.Add(new VelaMenuItem
        {
            Header = "写真模式",
            Icon = KugouIcons.Image,
            IsChecked = vm.IsPortraitModeEnabled,
            Command = vm.TogglePortraitModeCommand
        });

        // 4. 添加到歌单
        if (vm.CanAddCurrentSongToPlaylist)
        {
            MoreGlassMenu.Items.Add(new VelaMenuItem
            {
                Header = "添加到歌单",
                Icon = KugouIcons.PlaylistAdd,
                Command = vm.AddCurrentSongToPlaylistCommand
            });
        }
    }

    private void VolumeMuteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _playerViewModel?.ToggleMute();
    }

    private void VolumeButton_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_playerViewModel == null)
            return;

        _playerViewModel.MusicVolume = Math.Clamp((float)(_playerViewModel.MusicVolume + 0.05f * e.Delta.Y), 0f, 1f);
        e.Handled = true;
    }
}
