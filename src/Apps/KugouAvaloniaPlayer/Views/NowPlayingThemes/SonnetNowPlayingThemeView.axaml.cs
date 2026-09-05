using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Sonnet;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

public partial class SonnetNowPlayingThemeView : UserControl
{
    private static readonly SonnetTheme PlayerTheme = new(
        Background: new(0.051f, 0.071f, 0.208f, 1),
        Primary: new(0.9f, 0.91f, 0.95f, 1),
        Accent: new(0.55f, 0.59f, 0.72f, 1),
        Secondary: new(0.42f, 0.45f, 0.57f, 1),
        FontFamily: ResolveSonnetFontFamily(),
        FontWeight: 600,
        AnimationIntensity: SonnetAnimationIntensity.Normal,
        Name: "Midnight Dream",
        Description: "SONNET / FOLIA v0.7.2");

    private static string ResolveSonnetFontFamily()
    {
#if KUGOU_WINDOWS
        return "Microsoft YaHei UI";
#elif KUGOU_LINUX
        return "Noto Sans CJK SC";
#elif KUGOU_MACOS
        return "PingFang SC";
#else
        return "Noto Sans CJK SC";
#endif
    }

    private readonly DispatcherTimer _diagnosticTimer;
    private NowPlayingViewModel? _viewModel;
    private PlayerViewModel? _player;
    private SonnetScene? _scene;
    private bool _sceneBuildQueued;

    public SonnetNowPlayingThemeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        EffectSurface.InitializationFailed += OnInitializationFailed;
        _diagnosticTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background,
            (_, _) => RefreshDiagnostic());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookViewModel();
        QueueSceneBuild();
        _diagnosticTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _diagnosticTimer.Stop();
        UnhookViewModel();
        EffectSurface.Scene = null;
        _scene = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnhookViewModel();
        HookViewModel();
        QueueSceneBuild();
    }

    private void HookViewModel()
    {
        var viewModel = DataContext as NowPlayingViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        _viewModel = viewModel;
        _player = viewModel?.Player;
        if (_player is null)
            return;

        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.RenderLyricLines.CollectionChanged += OnLyricsCollectionChanged;
        _player.VisualizerUpdated += OnVisualizerUpdated;
        SynchronizePlaybackClock();
    }

    private void UnhookViewModel()
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
            _player.RenderLyricLines.CollectionChanged -= OnLyricsCollectionChanged;
            _player.VisualizerUpdated -= OnVisualizerUpdated;
        }

        _player = null;
        _viewModel = null;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentPlayingSong) or
            nameof(PlayerViewModel.DisplayedPlayingSong))
        {
            QueueSceneBuild();
            return;
        }

        if (e.PropertyName is nameof(PlayerViewModel.CurrentPositionSeconds) or
            nameof(PlayerViewModel.IsPlayingAudio))
            SynchronizePlaybackClock();
    }

    private void OnLyricsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueSceneBuild();

    private void QueueSceneBuild()
    {
        if (_sceneBuildQueued || !this.IsAttachedToVisualTree())
            return;

        _sceneBuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _sceneBuildQueued = false;
            if (this.IsAttachedToVisualTree())
                RebuildScene();
        }, DispatcherPriority.Background);
    }

    private void RebuildScene()
    {
        var player = _player;
        if (player is null)
            return;

        var song = player.DisplayedPlayingSong ?? player.CurrentPlayingSong;
        var identity = ResolveTrackIdentity(song);
        var lines = BuildLines(player.RenderLyricLines, song, player.TotalDurationSeconds);
        var program = SonnetProgramCompiler.Compile(lines, identity);
        var context = new SonnetSongContext(
            identity,
            identity,
            program,
            PlayerTheme,
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
                    ShowChromaticSplit = false,
                    PostProcessRgbShift = 0,
                    PostProcessLensDispersion = 0,
                },
            });
            EffectSurface.Scene = _scene;
        }
        else
        {
            var mode = context.TrackIdentity == _scene.CurrentSong.TrackIdentity
                ? SonnetSongSwapMode.Immediate
                : SonnetSongSwapMode.Animated;
            _scene.SetSong(context, mode);
        }

        SynchronizePlaybackClock();
        OnVisualizerUpdated();
    }

    private void SynchronizePlaybackClock()
    {
        var player = _player;
        if (player is null)
            return;

        EffectSurface.IsPaused = !player.IsPlayingAudio;
        EffectSurface.Seek(TimeSpan.FromSeconds(Math.Max(0, player.CurrentPositionSeconds)));
    }

    private void OnVisualizerUpdated()
    {
        if (_scene is null || _player is null)
            return;

        var bars = _player.NowPlayingVisualizerBars;
        if (bars.Length == 0)
        {
            _scene.Audio = default;
            return;
        }

        var bassEnd = Math.Max(1, bars.Length / 3);
        var vocalStart = bars.Length / 3;
        var vocalEnd = Math.Max(vocalStart + 1, bars.Length * 2 / 3);
        double power = 0;
        double bass = 0;
        double vocal = 0;
        for (var i = 0; i < bars.Length; i++)
        {
            var value = Math.Clamp((bars[i].Height - 6d) / 170d, 0d, 1d);
            power += value;
            if (i < bassEnd)
                bass += value;
            if (i >= vocalStart && i < vocalEnd)
                vocal += value;
        }

        _scene.Audio = new(
            (float)(power / bars.Length),
            (float)(bass / bassEnd),
            (float)(vocal / (vocalEnd - vocalStart)));
    }

    private void OnInitializationFailed(object? sender, EffectInitializationFailedEventArgs e)
    {
        DiagnosticText.Text = $"Sonnet OpenGL 初始化失败\n{e.Message}";
        DiagnosticOverlay.IsVisible = true;
    }

    private void RefreshDiagnostic()
    {
        if (!string.IsNullOrWhiteSpace(EffectSurface.LastError))
        {
            DiagnosticOverlay.IsVisible = true;
            DiagnosticText.Text = $"Sonnet OpenGL 渲染器不可用\n{EffectSurface.LastError}";
            return;
        }

        if (EffectSurface.FrameStatistics.SubmittedFrames > 0)
        {
            DiagnosticOverlay.IsVisible = false;
            return;
        }

        DiagnosticOverlay.IsVisible = true;
        DiagnosticText.Text =
            "Sonnet 尚未获得 OpenGL 渲染上下文。\n请完全退出并重新启动播放器；macOS 会按 OpenGL → Metal → Software 顺序选择后端。";
    }

    private static IReadOnlyList<SonnetLine> BuildLines(
        IReadOnlyList<LyricLine> source,
        SongItem? song,
        double durationSeconds)
    {
        if (source.Count == 0)
        {
            var title = string.IsNullOrWhiteSpace(song?.DisplayTitle) ? "SONNET" : song.DisplayTitle;
            var end = Math.Max(8, durationSeconds);
            return [new(title, 0, end, [])];
        }

        var result = new List<SonnetLine>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var line = source[index];
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;

            var start = Math.Max(0, line.Start.TotalSeconds);
            var end = line.Duration > TimeSpan.Zero
                ? start + line.Duration.TotalSeconds
                : index + 1 < source.Count
                    ? Math.Max(start + 0.1, source[index + 1].Start.TotalSeconds)
                    : Math.Max(start + 4, durationSeconds);
            var words = line.Words.Select(word => new SonnetWordTiming(
                word.Text,
                Math.Max(start, word.Start.TotalSeconds),
                Math.Max(start, word.Start.TotalSeconds) + Math.Max(0.01, word.Duration.TotalSeconds))).ToArray();
            result.Add(new(line.Text, start, Math.Max(start + 0.1, end), words));
        }

        return result.Count > 0
            ? result
            : [new SonnetLine(song?.DisplayTitle ?? "SONNET", 0, Math.Max(8, durationSeconds), [])];
    }

    private static string ResolveTrackIdentity(SongItem? song)
    {
        if (song is null)
            return "sonnet:empty";
        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"kugou:{song.Hash}";
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{song.LocalFilePath}";
        if (song.AudioId != 0)
            return $"audio:{song.AudioId}";
        return $"metadata:{song.DisplayTitle}:{song.Singer}:{song.DurationSeconds:F3}";
    }
}