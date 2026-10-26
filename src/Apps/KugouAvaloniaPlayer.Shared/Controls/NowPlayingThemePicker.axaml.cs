using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Controls;

public partial class NowPlayingThemePicker : UserControl
{
    public static readonly StyledProperty<NowPlayingThemePreset> SelectedPresetProperty =
        AvaloniaProperty.Register<NowPlayingThemePicker, NowPlayingThemePreset>(
            nameof(SelectedPreset), defaultBindingMode: BindingMode.TwoWay);

    private readonly List<(NowPlayingThemePreset Preset, Button Button, TextBlock Check)> _options = [];
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private TopLevel? _topLevel;
    private bool _expanded;
    private double _collapsedWidth = 96;

    public NowPlayingThemePicker()
    {
        InitializeComponent();
        foreach (var option in NowPlayingThemePresetRegistry.Presets)
        {
            var label = new TextBlock { Text = option.DisplayName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var check = new TextBlock { Text = "✓", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 1);
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16") };
            content.Children.Add(label);
            content.Children.Add(check);
            var button = new Button { Content = content, Height = 34 };
            button.Classes.Add("ThemePickerButton");
            button.Click += (_, _) =>
            {
                SetCurrentValue(SelectedPresetProperty, option.Preset);
                Collapse(restoreFocus: true);
            };
            _options.Add((option.Preset, button, check));
            Options.Children.Add(button);
        }

        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsPointerOver)
                Collapse(restoreFocus: IsKeyboardFocusWithin);
        };
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
        if (change.Property == SelectedPresetProperty && CurrentName != null)
            UpdateSelection();
        if (change.Property == IsEnabledProperty && !IsEnabled && PickerBody != null)
            Collapse();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnOutsidePointerPressed, RoutingStrategies.Tunnel);
        if (_topLevel is Window window)
            window.Deactivated += OnWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Collapse();
        _topLevel?.RemoveHandler(PointerPressedEvent, OnOutsidePointerPressed);
        if (_topLevel is Window window)
            window.Deactivated -= OnWindowDeactivated;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // Touch opens on click, so its synthesized enter does not toggle twice.
        if (e.Pointer.Type == PointerType.Mouse)
            Expand();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin && !IsPointerOver)
                Collapse();
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _expanded)
        {
            Collapse(restoreFocus: true);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up or Key.Home or Key.End)
        {
            Expand();
            var index = _options.FindIndex(option => option.Button.IsFocused);
            index = e.Key switch
            {
                Key.Home => 0,
                Key.End => _options.Count - 1,
                Key.Up => index < 0 ? _options.Count - 1 : (index - 1 + _options.Count) % _options.Count,
                _ => (index + 1) % _options.Count
            };
            _options[index].Button.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    private void OnTriggerClick(object? sender, RoutedEventArgs e)
    {
        // A mouse click after hover should keep the choices available.
        Expand();
        _options.Find(option => option.Preset == SelectedPreset).Button?.Focus(NavigationMethod.Tab);
    }

    private void Expand()
    {
        _collapseTimer.Stop();
        if (!IsEnabled)
            return;
        _expanded = true;
        Width = Math.Max(164, _collapsedWidth);
        PickerBody.IsEnabled = true;
        PickerBody.Height = _options.Count * 36 + 36;
        PseudoClasses.Set(":expanded", true);
    }

    private void Collapse(bool restoreFocus = false)
    {
        _collapseTimer.Stop();
        if (restoreFocus)
            Trigger.Focus();
        _expanded = false;
        Width = _collapsedWidth;
        PickerBody.IsEnabled = false;
        PickerBody.Height = 0;
        PseudoClasses.Set(":expanded", false);
    }

    private void UpdateSelection()
    {
        CurrentName.Text = NowPlayingThemePresetRegistry.Get(SelectedPreset).DisplayName;
        // Measure the label (including its margins), then add icon, chevron,
        // button padding and border so the closed pill fits every theme name.
        CurrentName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _collapsedWidth = Math.Max(96, Math.Ceiling(CurrentName.DesiredSize.Width) + 50);
        Width = _expanded ? Math.Max(164, _collapsedWidth) : _collapsedWidth;
        AutomationProperties.SetName(Trigger, $"播放主题：{CurrentName.Text}，展开选择");
        foreach (var option in _options)
        {
            var selected = option.Preset == SelectedPreset;
            option.Button.Classes.Set("selected", selected);
            option.Check.Opacity = selected ? 1 : 0;
            AutomationProperties.SetName(option.Button,
                NowPlayingThemePresetRegistry.Get(option.Preset).DisplayName + (selected ? "，当前主题" : ""));
        }
    }

    private void OnOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_expanded && !new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            Collapse();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Collapse();
}
