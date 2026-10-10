using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>绘光: Folia's stage-lighting lyric visualizer (volumetric light, fog, line art, lyrics lit by the beams).</summary>
public partial class LumiereNowPlayingThemeView : UserControl
{
    // Folia's "Midnight Dream" palette as in the Sonnet theme, at Folia's fallback weight 500: heavier strokes bloom
    // into each other where lit glyphs stack in a beam.
    private static readonly LumiereTheme PlayerTheme = new(
        Background: new(0.051f, 0.071f, 0.208f, 1),
        Primary: new(1, 1, 1, 1),
        Secondary: new(0.42f, 0.45f, 0.57f, 1),
        Accent: new(0.55f, 0.59f, 0.72f, 1),
        FontFamily: ResolveFontFamily(),
        FontWeight: 500);

    private static string ResolveFontFamily()
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

    // Resync the GL scene clock only when it drifted from the audio position by more than this.
    private const double ClockResyncThresholdSeconds = 0.08;

    private readonly DispatcherTimer _diagnosticTimer;
    private NowPlayingViewModel? _viewModel;
    private PlayerViewModel? _player;
    private LumiereEffectScene? _scene;
    private bool _sceneBuildQueued;
    private double _lastSyncedPositionSeconds = double.NegativeInfinity;
    private long _lastSyncTimestamp;
    private bool _lastSyncPaused = true;

    public LumiereNowPlayingThemeView()
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
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _player = null;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.LyricColor))
            QueueSceneBuild();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentPlayingSong) or
            nameof(PlayerViewModel.DisplayedPlayingSong) or
            nameof(PlayerViewModel.TotalDurationSeconds))
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
        var duration = player.TotalDurationSeconds > 0 ? player.TotalDurationSeconds : (double?)null;
        // Folia's default tuning keeps 轨迹过渡 (seamless) on: the whole song is one unit, handing light over between paragraphs.
        var program = LumiereProgramCompiler.Compile(BuildLines(player.RenderLyricLines, player.TotalDurationSeconds), identity,
            duration, seamless: true);
        var color = _viewModel!.LyricColor;
        var theme = PlayerTheme with
        {
            Primary = new EffectColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)
        };
        var context = new LumiereSongContext(identity, program, theme,
            new LumiereSongMetadata(song?.DisplayTitle, song?.Singer, song?.AlbumName));

        if (_scene is null)
        {
            _scene = new LumiereEffectScene(context);
            EffectSurface.Scene = _scene;
        }
        else
            _scene.SetSong(context);

        SynchronizePlaybackClock();
        OnVisualizerUpdated();
        EffectSurface.RenderOnce();
    }

    private void SynchronizePlaybackClock()
    {
        var player = _player;
        if (player is null)
            return;

        var paused = !player.IsPlayingAudio;
        var position = Math.Max(0, player.CurrentPositionSeconds);
        if (_scene is not null)
            _scene.Paused = paused;

        // While playing, extrapolate where the scene clock should be by now and compare that against the audio position;
        // while paused the scene clock is frozen, so compare directly. First call always syncs.
        var drifted = true;
        if (_lastSyncPaused == paused && !double.IsNegativeInfinity(_lastSyncedPositionSeconds))
        {
            var expected = paused
                ? _lastSyncedPositionSeconds
                : _lastSyncedPositionSeconds + Stopwatch.GetElapsedTime(_lastSyncTimestamp).TotalSeconds;
            drifted = Math.Abs(position - expected) >= ClockResyncThresholdSeconds;
        }

        if (!drifted)
            return;

        _lastSyncPaused = paused;
        _lastSyncedPositionSeconds = position;
        _lastSyncTimestamp = Stopwatch.GetTimestamp();
        EffectSurface.IsPaused = paused;
        EffectSurface.Seek(TimeSpan.FromSeconds(position));
    }

    /// <summary>Spectrum bars → bass (low third), treble (high third) and overall power, each 0..1.</summary>
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

        var lowEnd = Math.Max(1, bars.Length / 3);
        var highStart = Math.Min(bars.Length - 1, bars.Length * 2 / 3);
        double power = 0;
        double bass = 0;
        double treble = 0;
        for (var i = 0; i < bars.Length; i++)
        {
            var value = Math.Clamp((bars[i].Height - 6d) / 170d, 0d, 1d);
            power += value;
            if (i < lowEnd)
                bass += value;
            if (i >= highStart)
                treble += value;
        }

        _scene.Audio = new LumiereAudioFrame(bass / lowEnd, treble / (bars.Length - highStart), power / bars.Length);
    }

    private void OnInitializationFailed(object? sender, EffectInitializationFailedEventArgs e)
    {
        DiagnosticText.Text = $"绘光 OpenGL 初始化失败\n{e.Message}";
        DiagnosticOverlay.IsVisible = true;
    }

    private void RefreshDiagnostic()
    {
        if (!string.IsNullOrWhiteSpace(EffectSurface.LastError))
        {
            DiagnosticOverlay.IsVisible = true;
            DiagnosticText.Text = $"绘光 OpenGL 渲染器不可用\n{EffectSurface.LastError}";
            return;
        }

        if (EffectSurface.FrameStatistics.SubmittedFrames > 0)
        {
            DiagnosticOverlay.IsVisible = false;
            return;
        }

        DiagnosticOverlay.IsVisible = true;
        DiagnosticText.Text =
            "绘光尚未获得 OpenGL 渲染上下文。\n请完全退出并重新启动播放器；macOS 会按 OpenGL → Metal → Software 顺序选择后端。";
    }

    /// <summary>
    /// Player lyrics → Lumiere lines. A track without lyrics compiles to an instrumental program (interlude shots only),
    /// like Folia — no virtual lines are drawn as text.
    /// </summary>
    private static IReadOnlyList<LumiereLine> BuildLines(IReadOnlyList<LyricLine> source, double durationSeconds)
    {
        var result = new List<LumiereLine>(source.Count);
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
            var words = line.Words.Select(word => new LumiereWord(
                word.Text,
                Math.Max(start, word.Start.TotalSeconds),
                Math.Max(start, word.Start.TotalSeconds) + Math.Max(0.01, word.Duration.TotalSeconds))).ToArray();
            result.Add(new LumiereLine(line.Text, start, Math.Max(start + 0.1, end), words));
        }

        return result;
    }

    private static string ResolveTrackIdentity(SongItem? song)
    {
        if (song is null)
            return "lumiere:empty";
        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"kugou:{song.Hash}";
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{song.LocalFilePath}";
        if (song.AudioId != 0)
            return $"audio:{song.AudioId}";
        return $"metadata:{song.DisplayTitle}:{song.Singer}:{song.DurationSeconds:F3}";
    }
}
