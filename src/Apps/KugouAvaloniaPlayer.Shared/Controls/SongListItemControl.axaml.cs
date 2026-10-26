using System.Windows.Input;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class SongListItemControl : UserControl
{
    public static readonly StyledProperty<ICommand?> PlayCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(PlayCommand));

    public static readonly StyledProperty<ICommand?> AddToNextCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(AddToNextCommand));

    public static readonly StyledProperty<ICommand?> ShowPlaylistDialogCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(ShowPlaylistDialogCommand));

    public static readonly StyledProperty<ICommand?> ViewSingerCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(ViewSingerCommand));

    public static readonly StyledProperty<ICommand?> RemoveFromPlaylistCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(RemoveFromPlaylistCommand));

    public static readonly StyledProperty<ICommand?> SetLocalCoverCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(SetLocalCoverCommand));

    public static readonly StyledProperty<ICommand?> SearchLocalPlaylistsCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(SearchLocalPlaylistsCommand));

    public static readonly StyledProperty<ICommand?> MatchLocalLyricsCommandProperty =
        AvaloniaProperty.Register<SongListItemControl, ICommand?>(nameof(MatchLocalLyricsCommand));

    private MenuFlyout? _contextFlyout;
    private TopLevel? _lightDismissTopLevel;
    private MenuFlyout? _moreFlyout;

    public SongListItemControl()
    {
        InitializeComponent();
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ICommand? AddToNextCommand
    {
        get => GetValue(AddToNextCommandProperty);
        set => SetValue(AddToNextCommandProperty, value);
    }

    public ICommand? ShowPlaylistDialogCommand
    {
        get => GetValue(ShowPlaylistDialogCommandProperty);
        set => SetValue(ShowPlaylistDialogCommandProperty, value);
    }

    public ICommand? ViewSingerCommand
    {
        get => GetValue(ViewSingerCommandProperty);
        set => SetValue(ViewSingerCommandProperty, value);
    }

    public ICommand? RemoveFromPlaylistCommand
    {
        get => GetValue(RemoveFromPlaylistCommandProperty);
        set => SetValue(RemoveFromPlaylistCommandProperty, value);
    }

    public ICommand? SetLocalCoverCommand
    {
        get => GetValue(SetLocalCoverCommandProperty);
        set => SetValue(SetLocalCoverCommandProperty, value);
    }

    public ICommand? SearchLocalPlaylistsCommand
    {
        get => GetValue(SearchLocalPlaylistsCommandProperty);
        set => SetValue(SearchLocalPlaylistsCommandProperty, value);
    }

    public ICommand? MatchLocalLyricsCommand
    {
        get => GetValue(MatchLocalLyricsCommandProperty);
        set => SetValue(MatchLocalLyricsCommandProperty, value);
    }

    private void MoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
            return;

        var flyout = EnsureMoreFlyout();
        PopulateMenu(flyout, includePlaylistSpecificItems: true);
        flyout.ShowAt(control);
        AttachLightDismissHandler();
    }

    private void ItemShell_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var flyout = EnsureContextFlyout();
        PopulateMenu(flyout, includePlaylistSpecificItems: true);
        flyout.ShowAt(ItemShell, showAtPointer: true);
        AttachLightDismissHandler();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _moreFlyout?.Hide();
        _moreFlyout?.Items.Clear();
        _moreFlyout = null;
        _contextFlyout?.Hide();
        _contextFlyout?.Items.Clear();
        _contextFlyout = null;
        DetachLightDismissHandler();
        base.OnDetachedFromVisualTree(e);
    }

    private MenuFlyout EnsureMoreFlyout()
    {
        return _moreFlyout ??= new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight
        };
    }

    private MenuFlyout EnsureContextFlyout()
    {
        return _contextFlyout ??= new MenuFlyout();
    }

    private void PopulateMenu(MenuFlyout flyout, bool includePlaylistSpecificItems)
    {
        flyout.Items.Clear();

        if (DataContext is not SongItem song)
            return;

        flyout.Items.Add(CreateSingerMenuItem(song));
        flyout.Items.Add(new MenuItem
        {
            Header = "下一首播放",
            Command = AddToNextCommand,
            CommandParameter = song
        });

        if (song.LocalFilePath is null)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "添加到歌单",
                Command = ShowPlaylistDialogCommand,
                CommandParameter = song
            });
        }
        else
        {
            if (File.Exists(song.LocalFilePath))
            {
                flyout.Items.Add(new MenuItem
                {
                    Header = "匹配歌词...",
                    Command = MatchLocalLyricsCommand,
                    CommandParameter = song
                });
            }

            flyout.Items.Add(new MenuItem
            {
                Header = "查找所在歌单",
                Command = SearchLocalPlaylistsCommand,
                CommandParameter = song
            });
        }

        if (!includePlaylistSpecificItems)
            return;

        var playlistsViewModel = FindPlaylistsViewModel();
        var localMusicLibraryViewModel = FindLocalMusicLibraryViewModel();
        if (localMusicLibraryViewModel?.IsLocalPlaylist == true)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "设置歌曲封面",
                Command = SetLocalCoverCommand,
                CommandParameter = song
            });

            flyout.Items.Add(new MenuItem
            {
                Header = "从歌单移除",
                Command = RemoveFromPlaylistCommand,
                CommandParameter = song
            });
        }

        if (playlistsViewModel?.IsOnlinePlaylist == true)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "从歌单移除",
                Command = RemoveFromPlaylistCommand,
                CommandParameter = song
            });
        }
    }

    private MenuItem CreateSingerMenuItem(SongItem song)
    {
        var singerMenuItem = new MenuItem
        {
            Header = "查看歌手"
        };

        foreach (var singer in song.Singers)
        {
            singerMenuItem.Items.Add(new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(singer.Name) ? "未知歌手" : singer.Name,
                Command = ViewSingerCommand,
                CommandParameter = singer
            });
        }

        return singerMenuItem;
    }

    private MyPlaylistsViewModel? FindPlaylistsViewModel()
    {
        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor is Control { DataContext: MyPlaylistsViewModel viewModel })
                return viewModel;
        }

        return null;
    }

    private LocalMusicLibraryViewModel? FindLocalMusicLibraryViewModel()
    {
        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor is Control { DataContext: LocalMusicLibraryViewModel viewModel })
                return viewModel;
        }

        return null;
    }

    private void AttachLightDismissHandler()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || ReferenceEquals(_lightDismissTopLevel, topLevel))
            return;

        DetachLightDismissHandler();

        _lightDismissTopLevel = topLevel;
        topLevel.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void DetachLightDismissHandler()
    {
        _lightDismissTopLevel?.RemoveHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed);
        _lightDismissTopLevel = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _moreFlyout?.Hide();
        _contextFlyout?.Hide();
        DetachLightDismissHandler();
    }
}
