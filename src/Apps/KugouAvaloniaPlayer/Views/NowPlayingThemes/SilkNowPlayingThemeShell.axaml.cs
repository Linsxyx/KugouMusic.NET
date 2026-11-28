using Avalonia;
using Avalonia.Controls;
using AvaloniaSilkEffects;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>Shared canvas, diagnostics, player bar and background picker for the three Silk foregrounds.</summary>
public partial class SilkNowPlayingThemeShell : UserControl
{
    public static readonly StyledProperty<NowPlayingThemePreset> PresetProperty =
        AvaloniaProperty.Register<SilkNowPlayingThemeShell, NowPlayingThemePreset>(nameof(Preset), NowPlayingThemePreset.Sonnet);
    private bool _initialized;

    public NowPlayingThemePreset Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public SilkEffectControl Surface => EffectSurface;
    public Grid Viewport => EffectViewport;
    public Border DiagnosticOverlay => DiagnosticOverlayControl;
    public TextBlock DiagnosticText => DiagnosticTextControl;

    public SilkNowPlayingThemeShell()
    {
        InitializeComponent();
        _initialized = true;
        ApplyPreset();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_initialized && change.Property == PresetProperty) ApplyPreset();
    }

    private void ApplyPreset()
    {
        var (style, title) = Preset switch
        {
            NowPlayingThemePreset.Lumiere => ("Lumiere", "绘光 背景"),
            NowPlayingThemePreset.Tempera => ("Tempera", "凝彩 背景"),
            _ => ("Sonnet", "Sonnet 背景"),
        };
        ControlBar.Classes.Remove("Lumiere");
        ControlBar.Classes.Remove("Tempera");
        ControlBar.Classes.Remove("Sonnet");
        ControlBar.Classes.Add(style);
        SettingsTitle.Text = title;
        ToolTip.SetTip(SettingsButton, title);
    }
}
