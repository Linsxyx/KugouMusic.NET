using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

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

    private MenuFlyout? _contextFlyout;
    private TopLevel? _lightDismissTopLevel;

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

        var flyout = EnsureContextFlyout();
        PopulateMenu(flyout, item);

        if (flyout.Items.Count == 0)
            return;

        flyout.ShowAt(ItemShell, showAtPointer: true);
        AttachLightDismissHandler();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _contextFlyout?.Hide();
        _contextFlyout?.Items.Clear();
        _contextFlyout = null;
        DetachLightDismissHandler();
        base.OnDetachedFromVisualTree(e);
    }

    private MenuFlyout EnsureContextFlyout()
    {
        return _contextFlyout ??= new MenuFlyout();
    }

    private void PopulateMenu(MenuFlyout flyout, PlaylistItem item)
    {
        flyout.Items.Clear();

        switch (item.Type)
        {
            case PlaylistType.Online:
                AddMenuItem(flyout, "删除歌单", DeleteOnlinePlaylistCommand, item);
                break;
            case PlaylistType.Album:
                AddMenuItem(flyout, "取消收藏专辑", DeleteOnlinePlaylistCommand, item);
                break;
            case PlaylistType.Local:
                AddMenuItem(flyout, "编辑本地歌单", EditLocalPlaylistCommand, item);
                AddMenuItem(flyout, "移除本地歌单", DeleteLocalPlaylistCommand, item);
                break;
        }
    }

    private static void AddMenuItem(MenuFlyout flyout, string header, ICommand? command, PlaylistItem item)
    {
        if (command == null)
            return;

        flyout.Items.Add(new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = item
        });
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
        _contextFlyout?.Hide();
        DetachLightDismissHandler();
    }
}
