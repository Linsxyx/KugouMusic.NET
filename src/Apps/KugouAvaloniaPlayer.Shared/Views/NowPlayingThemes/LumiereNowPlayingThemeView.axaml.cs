using System;
using Avalonia;
using Avalonia.Controls;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Lumiere;
using KugouAvaloniaPlayer.ViewModels;

// 绘光主题视图：使用 SilkThemePlaybackCoordinator 统一管理播放时钟同步、音频可视化与场景调度

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>绘光: Folia's stage-lighting lyric visualizer (volumetric light, fog, line art, lyrics lit by the beams).</summary>
public partial class LumiereNowPlayingThemeView : UserControl
{
    private static readonly LumiereTheme PlayerTheme = new(
        Background: new(0.051f, 0.071f, 0.208f, 1),
        Primary: new(1, 1, 1, 1),
        Secondary: new(0.42f, 0.45f, 0.57f, 1),
        Accent: new(0.55f, 0.59f, 0.72f, 1),
        FontFamily: NowPlayingThemeHelper.ResolveFontFamily(),
        FontWeight: 500);

    private readonly SilkThemePlaybackCoordinator _coordinator;
    private LumiereEffectScene? _scene;

    public LumiereNowPlayingThemeView()
    {
        InitializeComponent();
        _coordinator = new SilkThemePlaybackCoordinator(
            this, ThemeShell.Surface, "绘光", ThemeShell.DiagnosticOverlay, ThemeShell.DiagnosticText, RebuildScene, OnVisualizerUpdated);
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
        var identity = NowPlayingThemeHelper.ResolveTrackIdentity(song, "lumiere");
        var duration = player.TotalDurationSeconds > 0 ? player.TotalDurationSeconds : (double?)null;
        var program = LumiereProgramCompiler.Compile(
            _coordinator.Lyrics.LumiereLines,
            identity,
            duration,
            seamless: true);
        var color = viewModel.LyricColor;
        var theme = PlayerTheme with
        {
            Primary = new EffectColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)
        };
        var context = new LumiereSongContext(identity, program, theme,
            new LumiereSongMetadata(song?.DisplayTitle, song?.Singer, song?.AlbumName));

        if (_scene is null)
        {
            _scene = new LumiereEffectScene(context);
            ThemeShell.Surface.Scene = _scene;
        }
        else
            _scene.SetSong(context);

        _scene.Paused = !player.IsPlayingAudio;
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

        var lowEnd = Math.Max(1, bars.Length / 3);
        var highStart = Math.Min(bars.Length - 1, bars.Length * 2 / 3);
        _scene.Audio = new LumiereAudioFrame(
            NowPlayingThemeHelper.AverageBandEnergy(bars, 0, lowEnd),
            NowPlayingThemeHelper.AverageBandEnergy(bars, highStart, bars.Length),
            NowPlayingThemeHelper.AverageBandEnergy(bars, 0, bars.Length));
    }
}
