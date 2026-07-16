using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class BottomPlaybackControl : UserControl
{
    private const double QueueFlyoutRightInset = 20;
    private Flyout? _queueFlyout;
    private TopLevel? _lightDismissTopLevel;

    public BottomPlaybackControl()
    {
        InitializeComponent();
    }

    private void QueueFlyout_OnOpening(object? sender, EventArgs e)
    {
        if (sender is not Flyout flyout)
            return;

        _queueFlyout = flyout;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var queueButtonTopLeft = QueueButton.TranslatePoint(new Point(0, 0), topLevel);
        if (queueButtonTopLeft is null)
            return;

        var buttonRight = queueButtonTopLeft.Value.X + QueueButton.Bounds.Width;
        flyout.HorizontalOffset = topLevel.Bounds.Width - QueueFlyoutRightInset - buttonRight;
    }

    private void QueueFlyout_OnOpened(object? sender, EventArgs e)
    {
        _queueFlyout = sender as Flyout;

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

    private void QueueFlyout_OnClosed(object? sender, EventArgs e)
    {
        _queueFlyout = sender as Flyout;
        DetachLightDismissHandler();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _queueFlyout?.Hide();
        _queueFlyout = null;
        DetachLightDismissHandler();
        base.OnDetachedFromVisualTree(e);
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
        if (_queueFlyout is not { IsOpen: true })
            return;

        if (e.Source is not Visual source)
        {
            _queueFlyout.Hide();
            return;
        }

        if (source == QueueButton || IsDescendantOf(source, QueueButton))
            return;

        if (source == QueueFlyoutContent || IsDescendantOf(source, QueueFlyoutContent))
            return;

        _queueFlyout.Hide();
    }

    private static bool IsDescendantOf(Visual source, Visual ancestor)
    {
        foreach (var currentAncestor in source.GetVisualAncestors())
        {
            if (ReferenceEquals(currentAncestor, ancestor))
                return true;
        }

        return false;
    }

    private void VolumeButton_OnPointerEntered(object? sender, PointerEventArgs e) {
        if (sender is not Button button)
            return;
        FlyoutBase.ShowAttachedFlyout(button);
        VolumeSlider.Focus();
    }

    private void VolumeButton_OnClick(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainWindowViewModel vm)
            return;
        vm.Player.ToggleMute();
        e.Handled = true;
    }

    private void VolumeSlider_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) {
        if (DataContext is not MainWindowViewModel vm)
            return;
        vm.Player.MusicVolume = Math.Clamp((float)(vm.Player.MusicVolume+0.01f* e.Delta.Y), 0f, 1f);
    }
}
