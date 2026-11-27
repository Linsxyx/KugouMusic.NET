using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;
using Vela.Avalonia.Tokens;

namespace KugouAvaloniaPlayer.Controls;

public partial class BottomPlaybackControl : UserControl
{
    private static readonly string[] PlaybackSpeedOptions = ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x"];
    private VelaContextMenu? _speedMenu;

    public BottomPlaybackControl()
    {
        InitializeComponent();
    }

    private void QueueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var drawerHost = this.FindAncestorOfType<VelaDrawerHost>();
        if (drawerHost is null)
            return;

        if (drawerHost.IsDrawerOpen)
        {
            drawerHost.Close();
            return;
        }

        var queueContent = new PlaybackQueueControl
        {
            DataContext = vm
        };

        _ = drawerHost.ShowAsync(queueContent, new VelaDrawerOptions
        {
            Title = "播放列表",
            Side = VelaDrawerSide.Right,
            Width = 380,
            Material = VelaSurfaceMaterial.LiquidGlass,
            ShowHeader = true,
            WrapInScrollViewer = false,
            BodyPadding = new Thickness(0),
            CloseOnBackdropClick = true
        });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _speedMenu?.Close();
        _speedMenu = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void PlaybackSpeedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button || DataContext is not MainWindowViewModel vm)
            return;

        _speedMenu ??= new VelaContextMenu
        {
            Placement = PlacementMode.Top,
            VerticalOffset = -8,
            MenuMinWidth = 124
        };

        _speedMenu.Items.Clear();
        var currentSpeed = vm.Player.PlaybackSpeedSelection;
        foreach (var option in PlaybackSpeedOptions)
        {
            _speedMenu.Items.Add(new VelaMenuItem
            {
                Header = option,
                IsChecked = string.Equals(currentSpeed, option, StringComparison.OrdinalIgnoreCase),
                Command = vm.Player.SetPlaybackSpeedOptionCommand,
                CommandParameter = option
            });
        }

        _speedMenu.ShowAt(button, showAtPointer: false);
    }

    private void VolumeButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button)
            return;
        FlyoutBase.ShowAttachedFlyout(button);
        VolumeSlider.Focus();
    }

    private void VolumeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        vm.Player.ToggleMute();
        e.Handled = true;
    }

    private void VolumeSlider_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        vm.Player.MusicVolume = Math.Clamp((float)(vm.Player.MusicVolume + 0.01f * e.Delta.Y), 0f, 1f);
    }

    private void VolumeButton_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        vm.Player.MusicVolume = Math.Clamp((float)(vm.Player.MusicVolume + 0.05f * e.Delta.Y), 0f, 1f);
    }
}
