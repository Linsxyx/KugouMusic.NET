using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using KugouAvaloniaPlayer.Models;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

public partial class NowPlayingThemePicker : UserControl
{
    public static readonly StyledProperty<NowPlayingThemePreset> SelectedPresetProperty =
        AvaloniaProperty.Register<NowPlayingThemePicker, NowPlayingThemePreset>(
            nameof(SelectedPreset), defaultBindingMode: BindingMode.TwoWay);

    private readonly List<(NowPlayingThemePreset Preset, VelaMenuItem Item)> _items = [];

    public NowPlayingThemePicker()
    {
        InitializeComponent();

        foreach (var option in NowPlayingThemePresetRegistry.Presets)
        {
            var item = new VelaMenuItem
            {
                Header = option.DisplayName,
                CommandParameter = option.Preset
            };
            _items.Add((option.Preset, item));
            ThemeGlassMenu.Items.Add(item);
        }

        ThemeGlassMenu.ItemInvoked += OnThemeItemInvoked;
        UpdateSelection();
    }

    public NowPlayingThemePreset SelectedPreset
    {
        get => GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedPresetProperty && ThemeGlassMenu != null)
            UpdateSelection();
        if (change.Property == IsEnabledProperty && !IsEnabled && ThemeGlassMenu != null)
            ThemeGlassMenu.IsOpen = false;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (ThemeGlassMenu != null)
            ThemeGlassMenu.IsOpen = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeItemInvoked(object? sender, VelaMenuItem item)
    {
        if (item.CommandParameter is NowPlayingThemePreset preset)
            SetCurrentValue(SelectedPresetProperty, preset);
    }

    private void UpdateSelection()
    {
        var currentName = NowPlayingThemePresetRegistry.Get(SelectedPreset).DisplayName;
        ThemeGlassMenu.TriggerLabel = currentName;
        AutomationProperties.SetName(ThemeGlassMenu, $"播放主题：{currentName}，展开选择");

        foreach (var (preset, item) in _items)
            item.IsChecked = preset == SelectedPreset;
    }
}
