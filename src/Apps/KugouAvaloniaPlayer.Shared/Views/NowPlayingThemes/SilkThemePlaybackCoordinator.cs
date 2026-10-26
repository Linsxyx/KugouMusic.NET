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
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

// 统一的 Silk 特效主题播放与视图协调器：封装播放时钟同步、ViewModel/Player 生命周期订阅、场景重建队列与 OpenGL 诊断逻辑

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>
/// 提供 NowPlaying 播放器与 SilkEffectControl 特效画布之间的通用生命周期与时钟协同机制。
/// </summary>
public sealed class SilkThemePlaybackCoordinator
{
    private const double ClockResyncThresholdSeconds = 0.08;

    private readonly UserControl _view;
    private readonly SilkEffectControl _surface;
    private readonly string _themeDisplayName;
    private readonly Border? _diagnosticOverlay;
    private readonly TextBlock? _diagnosticText;
    private readonly Action<PlayerViewModel, NowPlayingViewModel> _rebuildScene;
    private readonly Action? _onVisualizerUpdated;
    private readonly Action? _onDiagnosticRefresh;
    private readonly DispatcherTimer _diagnosticTimer;

    private NowPlayingViewModel? _viewModel;
    private PlayerViewModel? _player;
    private bool _sceneBuildQueued;
    private NowPlayingLyricSnapshot? _lyricsSnapshot;
    private double _lastSyncedPositionSeconds = double.NegativeInfinity;
    private long _lastSyncTimestamp;
    private bool _lastSyncPaused = true;

    public SilkThemePlaybackCoordinator(
        UserControl view,
        SilkEffectControl surface,
        string themeDisplayName,
        Border? diagnosticOverlay,
        TextBlock? diagnosticText,
        Action<PlayerViewModel, NowPlayingViewModel> rebuildScene,
        Action? onVisualizerUpdated = null,
        Action? onDiagnosticRefresh = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _themeDisplayName = themeDisplayName;
        _diagnosticOverlay = diagnosticOverlay;
        _diagnosticText = diagnosticText;
        _rebuildScene = rebuildScene ?? throw new ArgumentNullException(nameof(rebuildScene));
        _onVisualizerUpdated = onVisualizerUpdated;
        _onDiagnosticRefresh = onDiagnosticRefresh;

        _view.DataContextChanged += OnDataContextChanged;
        _surface.InitializationFailed += OnInitializationFailed;
        _diagnosticTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background,
            (_, _) => RefreshDiagnostic());
    }

    internal NowPlayingLyricSnapshot Lyrics => _lyricsSnapshot ??= new(
        _player?.RenderLyricLines.ToArray() ?? [], _player?.TotalDurationSeconds ?? 0);

    public PlayerViewModel? Player => _player;
    public NowPlayingViewModel? ViewModel => _viewModel;

    public void OnAttachedToVisualTree()
    {
        HookViewModel();
        QueueSceneBuild();
        _diagnosticTimer.Start();
    }

    public void OnDetachedFromVisualTree()
    {
        _diagnosticTimer.Stop();
        UnhookViewModel();
        _surface.Scene = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnhookViewModel();
        HookViewModel();
        QueueSceneBuild();
    }

    private void HookViewModel()
    {
        var viewModel = _view.DataContext as NowPlayingViewModel;
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
        if (_onVisualizerUpdated != null)
            _player.VisualizerUpdated += OnVisualizerUpdated;

        SynchronizePlaybackClock();
    }

    private void UnhookViewModel()
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
            _player.RenderLyricLines.CollectionChanged -= OnLyricsCollectionChanged;
            if (_onVisualizerUpdated != null)
                _player.VisualizerUpdated -= OnVisualizerUpdated;
        }

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _player = null;
        _viewModel = null;
        _lyricsSnapshot = null;
    }

    private void OnVisualizerUpdated() => _onVisualizerUpdated?.Invoke();

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
            _lyricsSnapshot = null;
            QueueSceneBuild();
            return;
        }

        if (e.PropertyName is nameof(PlayerViewModel.CurrentPositionSeconds) or
            nameof(PlayerViewModel.IsPlayingAudio))
        {
            SynchronizePlaybackClock();
        }
    }

    private void OnLyricsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _lyricsSnapshot = null;
        QueueSceneBuild();
    }

    public void QueueSceneBuild()
    {
        if (_sceneBuildQueued || !_view.IsAttachedToVisualTree())
            return;

        _sceneBuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _sceneBuildQueued = false;
            if (_view.IsAttachedToVisualTree() && _player is not null && _viewModel is not null)
            {
                using var timing = _surface.SceneBuildPerformance.Measure();
                _rebuildScene(_player, _viewModel);
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 当音频播放位置与特效场景时钟漂移超过阈值时进行对齐纠偏，暂停时直接冻结同步。
    /// </summary>
    public void SynchronizePlaybackClock()
    {
        var player = _player;
        if (player is null)
            return;

        var paused = !player.IsPlayingAudio;
        var position = Math.Max(0, player.CurrentPositionSeconds);

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
        _surface.IsPaused = paused;
        _surface.Seek(TimeSpan.FromSeconds(position));
    }

    private void OnInitializationFailed(object? sender, EffectInitializationFailedEventArgs e)
    {
        if (_diagnosticOverlay is not null)
            _diagnosticOverlay.IsVisible = true;
        if (_diagnosticText is not null)
            _diagnosticText.Text = $"{_themeDisplayName} OpenGL 初始化失败\n{e.Message}";
    }

    private void RefreshDiagnostic()
    {
        _onDiagnosticRefresh?.Invoke();

        if (_diagnosticOverlay is null || _diagnosticText is null)
            return;

        if (!string.IsNullOrWhiteSpace(_surface.LastError))
        {
            _diagnosticOverlay.IsVisible = true;
            _diagnosticText.Text = $"{_themeDisplayName} OpenGL 渲染器不可用\n{_surface.LastError}";
            return;
        }

        if (_surface.FrameStatistics.SubmittedFrames > 0)
        {
            _diagnosticOverlay.IsVisible = false;
            return;
        }

        _diagnosticOverlay.IsVisible = true;
        _diagnosticText.Text =
            $"{_themeDisplayName}尚未获得 OpenGL 渲染上下文。\n请完全退出并重新启动播放器；macOS 会按 OpenGL → Metal → Software 顺序选择后端。";
    }
}

/// <summary>
/// NowPlaying 特效主题通用辅助类（歌曲唯一标识、字体解析、歌词行序列化）。
/// </summary>
public static class NowPlayingThemeHelper
{
    public static string ResolveFontFamily()
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

    public static string ResolveTrackIdentity(SongItem? song, string prefix = "")
    {
        var tag = string.IsNullOrEmpty(prefix) ? "" : $"{prefix}:";
        if (song is null)
            return $"{tag}empty";
        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"kugou:{song.Hash}";
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{song.LocalFilePath}";
        if (song.AudioId != 0)
            return $"audio:{song.AudioId}";
        return $"metadata:{song.DisplayTitle}:{song.Singer}:{song.DurationSeconds:F3}";
    }

    /// <summary>
    /// 将可视化频谱柱高度归一化为 0..1 能量值。
    /// </summary>
    public static double NormalizeBarHeight(double height) =>
        Math.Clamp((height - 6d) / 170d, 0d, 1d);

    /// <summary>
    /// 计算指定频段区间的平均能量（0..1）。
    /// </summary>
    public static double AverageBandEnergy(IReadOnlyList<VisualizerBandState> bars, int start, int end)
    {
        if (bars.Count == 0 || start >= end)
            return 0;
        var limit = Math.Min(end, bars.Count);
        var total = 0d;
        var count = 0;
        for (var index = Math.Max(0, start); index < limit; index++)
        {
            total += NormalizeBarHeight(bars[index].Height);
            count++;
        }
        return count == 0 ? 0 : total / count;
    }
}
