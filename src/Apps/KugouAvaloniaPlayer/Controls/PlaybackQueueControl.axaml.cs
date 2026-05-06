using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Controls;

public partial class PlaybackQueueControl : UserControl
{
    public PlaybackQueueControl()
    {
        InitializeComponent();
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
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is Control { ContextFlyout: PopupFlyoutBase contextFlyout })
                contextFlyout.Hide();
        }
    }
}
