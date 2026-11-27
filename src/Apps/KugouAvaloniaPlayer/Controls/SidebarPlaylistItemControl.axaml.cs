using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

public partial class SidebarPlaylistItemControl : UserControl
{
    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<SidebarPlaylistItemControl, ICommand?>(nameof(OpenCommand));

    public static readonly StyledProperty<ICommand?> DeleteOnlinePlaylistCommandProperty =
        AvaloniaProperty.Register<SidebarPlaylistItemControl, ICommand?>(nameof(DeleteOnlinePlaylistCommand));

    public static readonly StyledProperty<ICommand?> EditLocalPlaylistCommandProperty =
        AvaloniaProperty.Register<SidebarPlaylistItemControl, ICommand?>(nameof(EditLocalPlaylistCommand));

    public static readonly StyledProperty<ICommand?> DeleteLocalPlaylistCommandProperty =
        AvaloniaProperty.Register<SidebarPlaylistItemControl, ICommand?>(nameof(DeleteLocalPlaylistCommand));

    private VelaContextMenu? _contextMenu;

    public SidebarPlaylistItemControl()
    {
        InitializeComponent();
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    public ICommand? DeleteOnlinePlaylistCommand
    {
        get => GetValue(DeleteOnlinePlaylistCommandProperty);
        set => SetValue(DeleteOnlinePlaylistCommandProperty, value);
    }

    public ICommand? EditLocalPlaylistCommand
    {
        get => GetValue(EditLocalPlaylistCommandProperty);
        set => SetValue(EditLocalPlaylistCommandProperty, value);
    }

    public ICommand? DeleteLocalPlaylistCommand
    {
        get => GetValue(DeleteLocalPlaylistCommandProperty);
        set => SetValue(DeleteLocalPlaylistCommandProperty, value);
    }

    private void ItemShell_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not PlaylistItem item)
            return;

        var menu = EnsureContextMenu();
        PopulateMenu(menu, item);

        if (menu.Items.Count == 0)
            return;

        menu.ShowAt(ItemShell, showAtPointer: e.TryGetPosition(ItemShell, out _));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _contextMenu?.Close();
        _contextMenu?.Items.Clear();
        _contextMenu = null;
        base.OnDetachedFromVisualTree(e);
    }

    private VelaContextMenu EnsureContextMenu()
    {
        return _contextMenu ??= new VelaContextMenu
        {
            MenuMinWidth = 176
        };
    }

    private void PopulateMenu(VelaContextMenu menu, PlaylistItem item)
    {
        menu.Items.Clear();

        switch (item.Type)
        {
            case PlaylistType.Online:
                AddMenuItem(menu, "删除歌单", KugouIcons.Trash, DeleteOnlinePlaylistCommand, item, isDestructive: true);
                break;
            case PlaylistType.Album:
                AddMenuItem(menu, "取消收藏专辑", KugouIcons.Trash, DeleteOnlinePlaylistCommand, item, isDestructive: true);
                break;
            case PlaylistType.Local:
                AddMenuItem(menu, "编辑本地歌单", KugouIcons.Edit, EditLocalPlaylistCommand, item);
                menu.Items.Add(new VelaMenuItem { IsSeparator = true });
                AddMenuItem(menu, "移除本地歌单", KugouIcons.Trash, DeleteLocalPlaylistCommand, item, isDestructive: true);
                break;
        }
    }

    private static void AddMenuItem(
        VelaContextMenu menu,
        string header,
        Geometry icon,
        ICommand? command,
        PlaylistItem item,
        bool isDestructive = false)
    {
        if (command == null)
            return;

        menu.Items.Add(new VelaMenuItem
        {
            Header = header,
            Icon = icon,
            IsDestructive = isDestructive,
            Command = command,
            CommandParameter = item
        });
    }
}
