using Avalonia;
using Avalonia.Controls;
using AvaloniaSilkEffects.Tempera;
using KugouAvaloniaPlayer.ViewModels;

// 凝彩主题视图：使用 SilkThemePlaybackCoordinator 统一管理播放时钟同步与场景调度

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>凝彩: Folia's screentone lyric PV (flat tone compositions, hatch, type that inverts against the artwork).</summary>
public partial class TemperaNowPlayingThemeView : UserControl
{
    private static readonly TemperaTheme PlayerTheme = TemperaTheme.MidnightDefault with { FontFamily = NowPlayingThemeHelper.ResolveFontFamily() };

    private readonly SilkThemePlaybackCoordinator _coordinator;
    private TemperaEffectScene? _scene;

    public TemperaNowPlayingThemeView()
    {
        InitializeComponent();
        _coordinator = new SilkThemePlaybackCoordinator(
            this, ThemeShell.Surface, "凝彩", ThemeShell.DiagnosticOverlay, ThemeShell.DiagnosticText, RebuildScene);
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
        var identity = NowPlayingThemeHelper.ResolveTrackIdentity(song, "tempera");
        var lines = _coordinator.Lyrics.LumiereLines;
        // Like Folia, a track without lyrics runs on virtual ♪ lines so the compositions keep cutting.
        var program = TemperaProgramCompiler.Compile(lines.Count > 0 ? lines : TemperaProgramCompiler.InstrumentalLines(), identity);
        var color = viewModel.LyricColor;
        var theme = PlayerTheme with { Primary = new TemperaRgb(color.R, color.G, color.B) };
        var context = new TemperaSongContext(identity, program, theme,
            new TemperaSongMetadata(song?.DisplayTitle, song?.Singer, song?.AlbumName));

        if (_scene is null)
        {
            _scene = new TemperaEffectScene(context);
            ThemeShell.Surface.Scene = _scene;
        }
        else
            _scene.SetSong(context);

        _coordinator.SynchronizePlaybackClock();
        ThemeShell.Surface.RenderOnce();
    }
}
