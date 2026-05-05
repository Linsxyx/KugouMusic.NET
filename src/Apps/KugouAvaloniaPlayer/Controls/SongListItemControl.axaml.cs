using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Controls;

public partial class SongListItemControl : UserControl
{
    public SongListItemControl()
    {
        InitializeComponent();
    }

    private void MoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        TopLevel.GetTopLevel(this)?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.RemoveHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed);

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FlyoutBase.GetAttachedFlyout(MoreButton) is PopupFlyoutBase moreFlyout)
            moreFlyout.Hide();

        if (ItemShell.ContextFlyout is PopupFlyoutBase contextFlyout)
            contextFlyout.Hide();
    }
}