using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaSilkEffects.Gpu;
using AvaloniaSilkEffects.Gpu.Presentation;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AvaloniaSilkEffects;

public sealed class EffectBackendSelectedEventArgs(EffectGpuBackend backend, string? fallbackReason) : EventArgs
{
    public EffectGpuBackend Backend { get; } = backend;
    /// <summary>Why the preferred native backend was not used, when this is a fallback.</summary>
    public string? FallbackReason { get; } = fallbackReason;
}

public sealed class EffectInitializationFailedEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}

/// <summary>
/// Hosts an <see cref="IEffectScene"/>. Frames render with Metal (macOS) or Vulkan (Windows, Linux) when the
/// compositor can import their images, and otherwise with OpenGL through Avalonia's <c>OpenGlControlBase</c>.
/// </summary>
public class SilkEffectControl : Control, IEffectPresenterHost
{
    /// <summary>
    /// Which graphics API new controls try first; set before the first control attaches. Every choice falls
    /// back to OpenGL. The environment variable <c>AVALONIA_SILK_EFFECTS_BACKEND</c> (auto, metal, vulkan, opengl)
    /// provides the default.
    /// </summary>
    public static EffectGpuBackendPreference BackendPreference { get; set; } = ParsePreference(
        Environment.GetEnvironmentVariable("AVALONIA_SILK_EFFECTS_BACKEND"));

    /// <summary>Raised on the UI thread when a control settles on a backend (including after a fallback).</summary>
    public static event EventHandler<EffectBackendSelectedEventArgs>? BackendSelected;

    public static readonly StyledProperty<IEffectScene?> SceneProperty =
        AvaloniaProperty.Register<SilkEffectControl, IEffectScene?>(nameof(Scene));

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<SilkEffectControl, bool>(nameof(IsPaused));

    public static readonly StyledProperty<EffectRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<SilkEffectControl, EffectRenderMode>(nameof(RenderMode), EffectRenderMode.Continuous);

    public static readonly StyledProperty<int> TargetFrameRateProperty =
        AvaloniaProperty.Register<SilkEffectControl, int>(nameof(TargetFrameRate), 0, coerce: (_, value) => Math.Clamp(value, 0, 240));

    public static readonly StyledProperty<Color> ClearColorProperty =
        AvaloniaProperty.Register<SilkEffectControl, Color>(nameof(ClearColor), Colors.Transparent);

    /// <summary>
    /// Extra content scale on top of the window's DPI scaling. A host that cancels an
    /// ancestor's scale transform (so the GL surface keeps native resolution) sets this
    /// to the cancelled factor; scenes then lay out against a correspondingly smaller
    /// logical viewport and render it at full resolution.
    /// </summary>
    public static readonly AttachedProperty<double> ContentScaleProperty =
        AvaloniaProperty.RegisterAttached<SilkEffectControl, Visual, double>(
            "ContentScale", 1d, inherits: true);

    public static readonly DirectProperty<SilkEffectControl, string?> LastErrorProperty =
        AvaloniaProperty.RegisterDirect<SilkEffectControl, string?>(nameof(LastError), control => control.LastError);

    private readonly EffectFrameClock _clock = new();
    private readonly EffectFramePacer _pacer = new();
    private readonly Stopwatch _renderStopwatch = Stopwatch.StartNew();
    private IDisposable? _nextFrameRequest;
    private EffectDevice? _device;
    private IEffectPresenter? _presenter;
    private bool _presenterPending;
    private bool _nativeFailed;
    private IEffectScene? _activeScene;
    private PixelSize _lastPixelSize;
    private double _lastRenderScaling;
    private TimeSpan _lastPresentationTimestamp;
    private ulong _submittedFrames;
    private string? _lastError;
    private readonly List<Visual> _visibilityAncestors = [];
    private bool _isAttached;

    // Includes ancestor visibility and the native window state. IsVisible alone
    // stays true on children when a window is hidden to the tray.
    protected bool IsRenderingActive { get; private set; }

    bool IEffectPresenterHost.IsRenderingActive => IsRenderingActive;

    /// <summary>The graphics API frames are rendered with, once a presenter is ready.</summary>
    public EffectGpuBackend? ActiveBackend => _presenter?.Backend;

    public IEffectScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public EffectRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public int TargetFrameRate
    {
        get => GetValue(TargetFrameRateProperty);
        set => SetValue(TargetFrameRateProperty, value);
    }

    public Color ClearColor
    {
        get => GetValue(ClearColorProperty);
        set => SetValue(ClearColorProperty, value);
    }

    public static double GetContentScale(Visual visual) => visual.GetValue(ContentScaleProperty);

    public static void SetContentScale(Visual visual, double value) => visual.SetValue(ContentScaleProperty, value);

    public double ContentScale
    {
        get
        {
            var value = GetValue(ContentScaleProperty);
            return double.IsFinite(value) && value > 0 ? value : 1;
        }
    }

    /// <summary>The control's size in the scene's logical units (bounds divided by <see cref="ContentScale"/>).</summary>
    protected Size LogicalSize => Bounds.Size / ContentScale;

    public string? LastError
    {
        get => _lastError;
        private set => SetAndRaise(LastErrorProperty, ref _lastError, value);
    }

    private readonly object _statisticsGate = new();
    private EffectFrameStatistics _frameStatistics;
    public EffectFrameStatistics FrameStatistics
    {
        get { lock (_statisticsGate) return _frameStatistics; }
        private set { lock (_statisticsGate) _frameStatistics = value; }
    }
    public EffectPerformanceHistory Performance { get; } = new();
    public EffectCpuWorkMeter SceneBuildPerformance { get; } = new();

    public event EventHandler<EffectInitializationFailedEventArgs>? InitializationFailed;

    public SilkEffectControl()
    {
        // The GPU surface is sized to this control. Keep the Avalonia composition
        // visual clipped to the same arranged bounds.
        ClipToBounds = true;
    }

    private static EffectGpuBackendPreference ParsePreference(string? value) =>
        Enum.TryParse<EffectGpuBackendPreference>(value?.Replace("gl", "Gl", StringComparison.OrdinalIgnoreCase),
            ignoreCase: true, out var preference) ? preference : EffectGpuBackendPreference.Auto;

    private void RequestNextFrameRendering() => _presenter?.RequestFrame();

    public void Seek(TimeSpan elapsed)
    {
        _clock.Seek(elapsed);
        RenderOnce();
    }

    public void RenderOnce()
    {
        if (IsRenderingActive)
            RequestNextFrameRendering();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            UpdateRenderingActivity();
        // OpenGlControlBase redraws on resize by itself; a composition surface needs a frame at the new size.
        if (change.Property == BoundsProperty && _presenter is { Child: null })
            RenderOnce();
        if (change.Property == IsPausedProperty)
            _clock.SetPaused(IsPaused);
        if (change.Property == TargetFrameRateProperty || change.Property == IsPausedProperty ||
            change.Property == RenderModeProperty || change.Property == SceneProperty)
        {
            CancelNextFrameRequest();
            _pacer.Reset();
        }
        if (change.Property == SceneProperty)
            Performance.Reset();
        if (change.Property == SceneProperty || change.Property == IsPausedProperty)
            _lastPresentationTimestamp = default;
        if (change.Property == SceneProperty || change.Property == IsPausedProperty ||
            change.Property == RenderModeProperty || change.Property == ClearColorProperty ||
            change.Property == TargetFrameRateProperty || change.Property == ContentScaleProperty)
            RenderOnce();
    }

    bool IEffectPresenterHost.RenderFrame(GpuDevice gpu, Func<PixelSize, GpuRenderTarget?> acquire)
    {
        CancelNextFrameRequest();
        if (!IsRenderingActive)
        {
            ReleaseDeviceResources();
            return false;
        }
        var prepareStarted = Stopwatch.GetTimestamp();
        var prepareAllocated = GC.GetAllocatedBytesForCurrentThread();
        if (_device is not null && !ReferenceEquals(_device.Gpu, gpu))
            ReleaseDeviceResources();
        if (_device is null)
            InitializeDevice(gpu);

        SwapSceneIfNeeded();
        if (_activeScene is null)
            return false;

        var dpiScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * dpiScaling)),
            Math.Max(1, (int)(Bounds.Height * dpiScaling)));
        // Scenes derive their logical viewport as pixels / scaling.
        var scaling = dpiScaling * ContentScale;
        if (pixelSize != _lastPixelSize || Math.Abs(scaling - _lastRenderScaling) > 0.001)
        {
            _lastPixelSize = pixelSize;
            _lastRenderScaling = scaling;
            _activeScene.Resize(pixelSize, scaling);
        }

        var presentationTimestamp = _renderStopwatch.Elapsed;
        // The presenter has already acquired (or is about to acquire) a compositor surface. It may be a different
        // swapchain image from the last callback, so skipping here can present stale contents. Always draw this
        // frame; apply the cap to the next request instead.
        _pacer.ShouldPresent(presentationTimestamp, TargetFrameRate);
        var output = acquire(pixelSize);
        if (output is null)
        {
            RenderOnce();
            return false;
        }

        var (elapsed, delta, frameNumber) = _clock.Step();
        var frame = new EffectFrame(elapsed, delta, pixelSize, scaling, frameNumber);
        var prepareMilliseconds = Stopwatch.GetElapsedTime(prepareStarted).TotalMilliseconds;
        var prepareBytes = GC.GetAllocatedBytesForCurrentThread() - prepareAllocated;
        var interval = _lastPresentationTimestamp == default || IsPaused
            ? 0 : (presentationTimestamp - _lastPresentationTimestamp).TotalMilliseconds;
        _device.Render(_activeScene, frame, output, EffectColor.FromAvalonia(ClearColor),
            prepareMilliseconds, prepareBytes);
        _submittedFrames++;
        _lastPresentationTimestamp = presentationTimestamp;
        var fps = interval > 0 ? 1000d / interval : 0;
        var metrics = _device.FrameMetrics;
        FrameStatistics = new EffectFrameStatistics(
            fps, metrics.Timings.CpuMilliseconds, _submittedFrames, 0,
            metrics.DrawCalls, metrics.Flushes, metrics.UploadedBytes, pixelSize,
            metrics.PostProcessingEnabled, _device.OpenGlVersion, _device.Renderer,
            metrics.ResidentTextures, metrics.ResidentTextureBytes)
        {
            MultisampleCount = metrics.MultisampleCount,
            Timings = metrics.Timings,
            GpuResources = metrics.GpuResources,
            CreatedTextures = metrics.CreatedTextures,
            ReleasedTextures = metrics.ReleasedTextures,
        };

        Performance.Record(metrics.Timings, interval);

        if (IsRenderingActive && !IsPaused && RenderMode == EffectRenderMode.Continuous)
            ScheduleNextFrameRequest();
        return true;
    }

    [MemberNotNull(nameof(_device))]
    private void InitializeDevice(GpuDevice gpu)
    {
        try
        {
            _device = new EffectDevice(gpu, ownsGpu: false);
            SetError(null);
        }
        catch (Exception exception)
        {
            SetError($"{gpu.Backend} initialization failed: {exception.Message}", exception);
            _device?.Dispose();
            _device = null;
            throw;
        }
    }

    private void ScheduleNextFrameRequest()
    {
        var delay = _pacer.GetNextFrameDelay(_renderStopwatch.Elapsed, TargetFrameRate);
        if (delay == TimeSpan.Zero)
        {
            RequestNextFrameRendering();
            return;
        }

        _nextFrameRequest = DispatcherTimer.RunOnce(() =>
        {
            _nextFrameRequest = null;
            if (_device is not null && !IsPaused && RenderMode == EffectRenderMode.Continuous)
                RenderOnce();
        }, delay, DispatcherPriority.Render);
    }

    private void CancelNextFrameRequest()
    {
        _nextFrameRequest?.Dispose();
        _nextFrameRequest = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        foreach (var ancestor in this.GetVisualAncestors())
        {
            _visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
        UpdateRenderingActivity();
        base.OnAttachedToVisualTree(e);
        if (IsRenderingActive && _presenter is null && !_presenterPending)
            _ = CreatePresenterAsync();
    }

    private async Task CreatePresenterAsync()
    {
        _presenterPending = true;
        try
        {
            string? reason = null;
            var preference = BackendPreference;
            if (!_nativeFailed && EffectPresenterFactory.Native(preference) is { } backend)
            {
                (var presenter, reason) = await EffectPresenterFactory.TryCreateNativeAsync(this, this, backend);
                if (!_isAttached)
                {
                    presenter?.Dispose();
                    return;
                }
                if (presenter is not null)
                {
                    Use(presenter, null);
                    return;
                }
                reason = $"{backend} unavailable: {reason}";
            }
            if (_isAttached) Use(new GlEffectPresenter(this), reason);
        }
        finally
        {
            _presenterPending = false;
        }
    }

    private void Use(IEffectPresenter presenter, string? fallbackReason)
    {
        _presenter = presenter;
        if (presenter.Child is { } child)
        {
            VisualChildren.Add(child);
            LogicalChildren.Add(child);
            InvalidateMeasure();
        }
        BackendSelected?.Invoke(this, new EffectBackendSelectedEventArgs(presenter.Backend, fallbackReason));
        _lastPixelSize = default;
        RenderOnce();
    }

    void IEffectPresenterHost.PresenterFailed(IEffectPresenter presenter, Exception exception)
    {
        if (!ReferenceEquals(presenter, _presenter)) return;
        SetError($"{presenter.Backend} rendering failed, falling back to OpenGL: {exception.Message}", exception);
        _presenter = null;
        try { presenter.Dispose(); }
        catch { /* the device is already broken */ AbandonDeviceResources(null); }
        if (presenter is GlEffectPresenter) return;
        _nativeFailed = true;
        if (_isAttached) Use(new GlEffectPresenter(this), exception.Message);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _presenter?.Child?.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter?.Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty)
            UpdateRenderingActivity();
    }

    private void UpdateRenderingActivity()
    {
        var active = _isAttached && IsVisible;
        foreach (var ancestor in _visibilityAncestors)
            active &= ancestor.IsVisible && ancestor is not Window { WindowState: WindowState.Minimized };
        if (active == IsRenderingActive)
            return;

        IsRenderingActive = active;
        _lastPresentationTimestamp = default;
        CancelNextFrameRequest();
        _pacer.Reset();
        // Keep the playback clock advancing while hidden. IsPaused remains
        // caller-owned, so showing a paused scene does not resume playback.
        OnRenderingActivityChanged();
        if (active && _isAttached && _presenter is null && !_presenterPending && VisualRoot is not null)
            _ = CreatePresenterAsync();
        if (active || _device is not null)
            RequestNextFrameRendering();
    }

    protected virtual void OnRenderingActivityChanged() { }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        foreach (var ancestor in _visibilityAncestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        UpdateRenderingActivity();
        CancelNextFrameRequest();
        _pacer.Reset();
        // A native presenter's swapchain belongs to this compositor; OpenGL cleans up when its surface deinitializes.
        if (_presenter is { Child: null } native)
        {
            _presenter = null;
            native.Dispose();
        }
        base.OnDetachedFromVisualTree(e);
    }

    void IEffectPresenterHost.ReleaseDeviceResources() => ReleaseDeviceResources();

    /// <summary>Releases the scene's and the device's objects; the presenter keeps its GPU device.</summary>
    private void ReleaseDeviceResources()
    {
        var scene = _activeScene;
        var device = _device;
        _activeScene = null;
        _device = null;
        _lastPixelSize = default;
        _lastRenderScaling = 0;
        try { scene?.DisposeGpuResources(); }
        finally { device?.Dispose(); }
    }

    void IEffectPresenterHost.AbandonDeviceResources(string reason) => AbandonDeviceResources(reason);

    private void AbandonDeviceResources(string? reason)
    {
        CancelNextFrameRequest();
        _device?.Abandon();
        _activeScene = null;
        _device = null;
        _pacer.Reset();
        if (reason is not null) SetError(reason);
    }

    private void SwapSceneIfNeeded()
    {
        if (ReferenceEquals(_activeScene, Scene))
            return;
        _activeScene?.DisposeGpuResources();
        _activeScene = Scene;
        _lastPixelSize = default;
        if (_activeScene is not null)
            _activeScene.Initialize(_device!);
    }

    private void SetError(string? message, Exception? exception = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LastError = message;
            if (message is not null)
                InitializationFailed?.Invoke(this, new EffectInitializationFailedEventArgs(message, exception));
        });
    }
}
