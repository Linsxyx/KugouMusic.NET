using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class NowPlaying
{
    private Flyout? _moreFlyout;
    private TopLevel? _moreFlyoutLightDismissTopLevel;

    private void MoreFlyout_OnOpened(object? sender, EventArgs e)
    {
        _moreFlyout = sender as Flyout;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || ReferenceEquals(_moreFlyoutLightDismissTopLevel, topLevel))
            return;

        DetachMoreFlyoutLightDismissHandler();

        _moreFlyoutLightDismissTopLevel = topLevel;
        topLevel.AddHandler(
            PointerPressedEvent,
            OnMoreFlyoutTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void MoreFlyout_OnClosed(object? sender, EventArgs e)
    {
        _moreFlyout = sender as Flyout;
        if (DataContext is NowPlayingViewModel viewModel)
        {
            viewModel.IsSingerMenuExpanded = false;
            viewModel.IsVolumeVisible = false;
        }

        DetachMoreFlyoutLightDismissHandler();
    }

    private void HideMoreFlyout()
    {
        _moreFlyout?.Hide();
    }

    private void DetachMoreFlyoutLightDismissHandler()
    {
        _moreFlyoutLightDismissTopLevel?.RemoveHandler(
            PointerPressedEvent,
            OnMoreFlyoutTopLevelPointerPressed);
        _moreFlyoutLightDismissTopLevel = null;
    }

    private void OnMoreFlyoutTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_moreFlyout is not { IsOpen: true })
            return;

        if (e.Source is not Visual source)
        {
            HideMoreFlyout();
            return;
        }

        if (source == MoreButton || IsDescendantOf(source, MoreButton))
            return;

        if (source == MoreFlyoutContent || IsDescendantOf(source, MoreFlyoutContent))
            return;

        HideMoreFlyout();
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
}
