using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AvaloniaSilkEffects;

public sealed class EffectInitializationFailedEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}

public class SilkEffectControl : OpenGlControlBase
{
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

    public static readonly DirectProperty<SilkEffectControl, string?> LastErrorProperty =
        AvaloniaProperty.RegisterDirect<SilkEffectControl, string?>(nameof(LastError), control => control.LastError);

    private readonly EffectFrameClock _clock = new();
    private readonly EffectFramePacer _pacer = new();
    private readonly Stopwatch _renderStopwatch = Stopwatch.StartNew();
    private IDisposable? _nextFrameRequest;
    private GL? _gl;
    private EffectDevice? _device;
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

    public string? LastError
    {
        get => _lastError;
        private set => SetAndRaise(LastErrorProperty, ref _lastError, value);
    }

    public EffectFrameStatistics FrameStatistics { get; private set; }

    public event EventHandler<EffectInitializationFailedEventArgs>? InitializationFailed;

    public SilkEffectControl()
    {
        // The GL surface is sized to this control by OpenGlControlBase. Keep the
        // Avalonia composition visual clipped to the same arranged bounds.
        ClipToBounds = true;
    }

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
        if (change.Property == IsPausedProperty)
            _clock.SetPaused(IsPaused);
        if (change.Property == TargetFrameRateProperty || change.Property == IsPausedProperty ||
            change.Property == RenderModeProperty || change.Property == SceneProperty)
        {
            CancelNextFrameRequest();
            _pacer.Reset();
        }
        if (change.Property == SceneProperty || change.Property == IsPausedProperty ||
            change.Property == RenderModeProperty || change.Property == ClearColorProperty ||
            change.Property == TargetFrameRateProperty)
            RenderOnce();
    }

    protected override void OnOpenGlInit(GlInterface avaloniaGl)
    {
        if (IsRenderingActive)
            InitializeDevice(avaloniaGl);
    }

    [MemberNotNull(nameof(_device), nameof(_gl))]
    private void InitializeDevice(GlInterface avaloniaGl)
    {
        try
        {
            _gl = GL.GetApi(avaloniaGl.GetProcAddress);
            _device = new EffectDevice(_gl);
            SetError(null);
        }
        catch (Exception exception)
        {
            SetError($"OpenGL initialization failed: {exception.Message}", exception);
            _device?.Dispose();
            _device = null;
            _gl?.Dispose();
            _gl = null;
            throw;
        }
    }

    protected override void OnOpenGlRender(GlInterface avaloniaGl, int framebuffer)
    {
        CancelNextFrameRequest();
        if (!IsRenderingActive)
        {
            // Visibility notifications do not make the GL context current.
            // Use one final callback for cleanup, without updating the scene.
            ReleaseDevice();
            return;
        }
        if (_device is null)
            InitializeDevice(avaloniaGl);

        SwapSceneIfNeeded();
        if (_activeScene is null)
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        if (pixelSize != _lastPixelSize || Math.Abs(scaling - _lastRenderScaling) > 0.001)
        {
            _lastPixelSize = pixelSize;
            _lastRenderScaling = scaling;
            _activeScene.Resize(pixelSize, scaling);
        }

        var presentationTimestamp = _renderStopwatch.Elapsed;
        // BeginDraw has already acquired a compositor surface. It may be a different
        // swapchain image from the last callback, so skipping here can present stale
        // contents. Always draw this frame; apply the cap to the next request instead.
        _pacer.ShouldPresent(presentationTimestamp, TargetFrameRate);

        var (elapsed, delta, frameNumber) = _clock.Step();
        var frame = new EffectFrame(elapsed, delta, pixelSize, scaling, frameNumber);
        var renderStarted = Stopwatch.GetTimestamp();
        _device.Render(_activeScene, frame, framebuffer, EffectColor.FromAvalonia(ClearColor));
        var cpuMilliseconds = Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;
        _submittedFrames++;
        var presentationDelta = presentationTimestamp - _lastPresentationTimestamp;
        _lastPresentationTimestamp = presentationTimestamp;
        var fps = presentationDelta > TimeSpan.Zero ? 1d / presentationDelta.TotalSeconds : 0;
        var metrics = _device.FrameMetrics;
        FrameStatistics = new EffectFrameStatistics(
            fps, cpuMilliseconds, _submittedFrames, 0,
            metrics.DrawCalls, metrics.Flushes, metrics.UploadedBytes, pixelSize,
            metrics.PostProcessingEnabled, _device.OpenGlVersion, _device.Renderer,
            metrics.ResidentTextures, metrics.ResidentTextureBytes)
        {
            MultisampleCount = metrics.MultisampleCount,
        };

        if (IsRenderingActive && !IsPaused && RenderMode == EffectRenderMode.Continuous)
            ScheduleNextFrameRequest();
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
        CancelNextFrameRequest();
        _pacer.Reset();
        // Keep the playback clock advancing while hidden. IsPaused remains
        // caller-owned, so showing a paused scene does not resume playback.
        OnRenderingActivityChanged();
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
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnOpenGlDeinit(GlInterface avaloniaGl)
    {
        CancelNextFrameRequest();
        ReleaseDevice();
        _pacer.Reset();
    }

    private void ReleaseDevice()
    {
        var scene = _activeScene;
        var device = _device;
        var gl = _gl;
        _activeScene = null;
        _device = null;
        _gl = null;
        _lastPixelSize = default;
        _lastRenderScaling = 0;
        try { scene?.DisposeGpuResources(); }
        finally
        {
            try { device?.Dispose(); }
            finally { gl?.Dispose(); }
        }
    }

    protected override void OnOpenGlLost()
    {
        CancelNextFrameRequest();
        _device?.Abandon();
        _activeScene = null;
        _device = null;
        _gl = null;
        _pacer.Reset();
        SetError("The OpenGL context was lost. Avalonia will recreate the effect resources.");
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
