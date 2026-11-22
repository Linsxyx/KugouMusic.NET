using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SukiUI.Controls;

namespace KugouAvaloniaPlayer.Controls;

public class KugouSideMenu : SukiSideMenu
{
    public static readonly StyledProperty<ICommand?> ItemReclickedCommandProperty =
        AvaloniaProperty.Register<KugouSideMenu, ICommand?>(nameof(ItemReclickedCommand));

    public ICommand? ItemReclickedCommand
    {
        get => GetValue(ItemReclickedCommandProperty);
        set => SetValue(ItemReclickedCommandProperty, value);
    }

    public KugouSideMenu()
    {
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        if (e.Source is not Visual source)
            return;

        var menuItem = source.FindAncestorOfType<SukiSideMenuItem>(includeSelf: true);
        if (menuItem == null || !ReferenceEquals(ItemsControlFromItemContainer(menuItem), this))
            return;

        if (!menuItem.IsSelected)
            return;

        // 阻止 Avalonia TreeView.SelectSingleItem 对已选中项执行 Clear + Add，避免重复触发选中动画。
        e.Handled = true;

        var item = ItemFromContainer(menuItem) ?? menuItem.DataContext;
        if (ItemReclickedCommand is { } command && command.CanExecute(item))
            command.Execute(item);
    }
}
