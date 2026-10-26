using System;
using Avalonia;
using Avalonia.Controls;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Sonnet;
using KugouAvaloniaPlayer.ViewModels;

// 凝诗 (Sonnet) 主题视图：使用 SilkThemePlaybackCoordinator 统一管理播放时钟同步、音频可视化与场景调度

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

public partial class SonnetNowPlayingThemeView : UserControl
{
    private static readonly SonnetTheme PlayerTheme = new(
        Background: new(0.051f, 0.071f, 0.208f, 1),
        Primary: new(1, 1, 1, 1),
        Accent: new(0.55f, 0.59f, 0.72f, 1),
        Secondary: new(0.42f, 0.45f, 0.57f, 1),
        FontFamily: NowPlayingThemeHelper.ResolveFontFamily(),
        FontWeight: 600,
        AnimationIntensity: EffectAnimationIntensity.Normal,
        Name: "Midnight Dream",
        Description: "SONNET / FOLIA v0.7.2");

#if DEBUG
    private readonly TextBlock _presetDebugText = new()
    {
        FontSize = 18,
        FontWeight = Avalonia.Media.FontWeight.Bold,
        Foreground = Avalonia.Media.Brushes.Yellow,
    };
    private readonly Border _presetDebugOverlay;
#endif

    private readonly SilkThemePlaybackCoordinator _coordinator;
    private SonnetScene? _scene;

    public SonnetNowPlayingThemeView()
    {
        InitializeComponent();
#if DEBUG
        _presetDebugOverlay = new Border
        {
            Child = _presetDebugText,
            Background = Avalonia.Media.Brushes.Black,
            BorderBrush = Avalonia.Media.Brushes.Yellow,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(20, 64, 20, 20),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        ThemeShell.Viewport.Children.Add(_presetDebugOverlay);
#endif
        _coordinator = new SilkThemePlaybackCoordinator(
            this, ThemeShell.Surface, "Sonnet", ThemeShell.DiagnosticOverlay, ThemeShell.DiagnosticText, RebuildScene, OnVisualizerUpdated, RefreshPresetDebug);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _coordinator.OnAttachedToVisualTree();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _coordinator.OnDetachedFromVisualTree();
        _scene = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void RebuildScene(PlayerViewModel player, NowPlayingViewModel viewModel)
    {
        var song = player.DisplayedPlayingSong ?? player.CurrentPlayingSong;
        var identity = NowPlayingThemeHelper.ResolveTrackIdentity(song, "sonnet");
        var program = SonnetProgramCompiler.Compile(
            _coordinator.Lyrics.SonnetLines,
            identity);
        var color = viewModel.LyricColor;
        var theme = PlayerTheme with
        {
            Primary = new EffectColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)
        };
        var context = new SonnetSongContext(
            identity,
            identity,
            program,
            theme,
            new(song?.DisplayTitle, song?.Singer, song?.AlbumName));

        if (_scene is null)
        {
            _scene = new SonnetScene(context, new SonnetSceneOptions
            {
                TransparentBackground = true,
                Tuning = new SonnetTuning
                {
                    TextureResolution = 1.5f,
                    PostProcessEnabled = true,
                    PostProcessGrain = 0,
                    PostProcessVignette = 0,
                    ShowChromaticSplit = false,
                    PostProcessRgbShift = 0,
                    PostProcessLensDispersion = 0,
                },
            });
            ThemeShell.Surface.Scene = _scene;
        }
        else
        {
            var mode = context.TrackIdentity == _scene.CurrentSong.TrackIdentity
                ? SonnetSongSwapMode.Immediate
                : SonnetSongSwapMode.Animated;
            _scene.SetSong(context, mode);
        }

        _coordinator.SynchronizePlaybackClock();
        OnVisualizerUpdated();
        ThemeShell.Surface.RenderOnce();
    }

    private void OnVisualizerUpdated()
    {
        var player = _coordinator.Player;
        if (_scene is null || player is null)
            return;

        var bars = player.NowPlayingVisualizerBars;
        if (bars.Length == 0)
        {
            _scene.Audio = default;
            return;
        }

        var bassEnd = Math.Max(1, bars.Length / 3);
        var vocalStart = bars.Length / 3;
        var vocalEnd = Math.Max(vocalStart + 1, bars.Length * 2 / 3);
        _scene.Audio = new(
            (float)NowPlayingThemeHelper.AverageBandEnergy(bars, 0, bars.Length),
            (float)NowPlayingThemeHelper.AverageBandEnergy(bars, 0, bassEnd),
            (float)NowPlayingThemeHelper.AverageBandEnergy(bars, vocalStart, vocalEnd));
    }

    private void RefreshPresetDebug()
    {
#if DEBUG
        var presetLabel = _scene?.ActivePresetDebugLabel;
        _presetDebugOverlay.IsVisible = presetLabel is not null && string.IsNullOrWhiteSpace(ThemeShell.Surface.LastError);
        if (_presetDebugText.Text != presetLabel)
            _presetDebugText.Text = presetLabel;
#endif
    }
}
