using System.Windows.Input;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

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

    private VelaContextMenu? _contextMenu;
    private VelaContextMenu? _moreMenu;

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

        var menu = EnsureMoreMenu();
        PopulateMenu(menu, includePlaylistSpecificItems: true);
        menu.ShowAt(control, showAtPointer: false);
    }

    private void ItemShell_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var menu = EnsureContextMenu();
        PopulateMenu(menu, includePlaylistSpecificItems: true);
        menu.ShowAt(ItemShell, showAtPointer: e.TryGetPosition(ItemShell, out _));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _moreMenu?.Close();
        _moreMenu?.Items.Clear();
        _moreMenu = null;
        _contextMenu?.Close();
        _contextMenu?.Items.Clear();
        _contextMenu = null;
        base.OnDetachedFromVisualTree(e);
    }

    private VelaContextMenu EnsureMoreMenu()
    {
        return _moreMenu ??= new VelaContextMenu
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            MenuMinWidth = 196
        };
    }

    private VelaContextMenu EnsureContextMenu()
    {
        return _contextMenu ??= new VelaContextMenu
        {
            MenuMinWidth = 196
        };
    }

    private void PopulateMenu(VelaContextMenu menu, bool includePlaylistSpecificItems)
    {
        menu.Items.Clear();

        if (DataContext is not SongItem song)
            return;

        if (CreateSingerMenuItem(song) is { } singerItem)
            menu.Items.Add(singerItem);

        menu.Items.Add(new VelaMenuItem
        {
            Header = "下一首播放",
            Icon = KugouIcons.QueueNext,
            Command = AddToNextCommand,
            CommandParameter = song
        });

        if (song.LocalFilePath is null)
        {
            menu.Items.Add(new VelaMenuItem
            {
                Header = "添加到歌单",
                Icon = KugouIcons.PlaylistAdd,
                Command = ShowPlaylistDialogCommand,
                CommandParameter = song
            });
        }
        else
        {
            if (File.Exists(song.LocalFilePath))
            {
                menu.Items.Add(new VelaMenuItem
                {
                    Header = "匹配歌词...",
                    Icon = KugouIcons.Lyrics,
                    Command = MatchLocalLyricsCommand,
                    CommandParameter = song
                });
            }

            menu.Items.Add(new VelaMenuItem
            {
                Header = "查找所在歌单",
                Icon = KugouIcons.Search,
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
            menu.Items.Add(new VelaMenuItem
            {
                Header = "设置歌曲封面",
                Icon = KugouIcons.Image,
                Command = SetLocalCoverCommand,
                CommandParameter = song
            });

            menu.Items.Add(new VelaMenuItem { IsSeparator = true });
            menu.Items.Add(new VelaMenuItem
            {
                Header = "从歌单移除",
                Icon = KugouIcons.Trash,
                IsDestructive = true,
                Command = RemoveFromPlaylistCommand,
                CommandParameter = song
            });
        }
        else if (playlistsViewModel?.IsOnlinePlaylist == true)
        {
            menu.Items.Add(new VelaMenuItem { IsSeparator = true });
            menu.Items.Add(new VelaMenuItem
            {
                Header = "从歌单移除",
                Icon = KugouIcons.Trash,
                IsDestructive = true,
                Command = RemoveFromPlaylistCommand,
                CommandParameter = song
            });
        }
    }

    private VelaMenuItem? CreateSingerMenuItem(SongItem song)
    {
        if (song.Singers.Count == 0)
            return null;

        if (song.Singers.Count == 1)
        {
            var only = song.Singers[0];
            var name = string.IsNullOrWhiteSpace(only.Name) ? "未知歌手" : only.Name;
            return new VelaMenuItem
            {
                Header = $"查看歌手：{name}",
                Icon = KugouIcons.Singer,
                Command = ViewSingerCommand,
                CommandParameter = only
            };
        }

        var singerMenuItem = new VelaMenuItem
        {
            Header = "查看歌手",
            Icon = KugouIcons.Singer
        };

        foreach (var singer in song.Singers)
        {
            singerMenuItem.Items.Add(new VelaMenuItem
            {
                Header = string.IsNullOrWhiteSpace(singer.Name) ? "未知歌手" : singer.Name,
                Icon = KugouIcons.Singer,
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
}
