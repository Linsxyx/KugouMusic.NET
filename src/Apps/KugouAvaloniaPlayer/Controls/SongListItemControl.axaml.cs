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
    private MenuFlyout? _contextFlyout;
    private TopLevel? _lightDismissTopLevel;
    private MenuFlyout? _moreFlyout;

    public SongListItemControl()
    {
        InitializeComponent();
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
        PopulateMenu(flyout, includePlaylistSpecificItems: false);
        flyout.ShowAt(ItemShell, showAtPointer: true);
        AttachLightDismissHandler();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
            Command = song.AddToNextCommand,
            CommandParameter = song
        });

        if (song.LocalFilePath is null)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "添加到歌单",
                Command = song.ShowPlaylistDialogCommand,
                CommandParameter = song
            });
        }

        if (!includePlaylistSpecificItems)
            return;

        var playlistsViewModel = FindPlaylistsViewModel();
        if (playlistsViewModel?.IsLocalPlaylist == true)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "设置歌曲封面",
                Command = song.SetLocalCoverCommand,
                CommandParameter = song
            });
        }

        if (playlistsViewModel?.IsOnlinePlaylist == true)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "从歌单移除",
                Command = song.RemoveFromPlaylistCommand,
                CommandParameter = song
            });
        }
    }

    private static MenuItem CreateSingerMenuItem(SongItem song)
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
                Command = song.ViewSingerCommand,
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
