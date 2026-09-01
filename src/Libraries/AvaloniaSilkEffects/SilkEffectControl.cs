using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.OpenGL;
using System.Diagnostics;

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
    private GL? _gl;
    private EffectDevice? _device;
    private IEffectScene? _activeScene;
    private PixelSize _lastPixelSize;
    private double _lastRenderScaling;
    private TimeSpan _lastPresentationTimestamp;
    private ulong _submittedFrames;
    private ulong _skippedFrames;
    private string? _lastError;

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
        RequestNextFrameRendering();
    }

    public void RenderOnce() => RequestNextFrameRendering();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPausedProperty)
            _clock.SetPaused(IsPaused);
        if (change.Property == TargetFrameRateProperty || change.Property == IsPausedProperty)
            _pacer.Reset();
        if (change.Property == SceneProperty || change.Property == IsPausedProperty ||
            change.Property == RenderModeProperty || change.Property == ClearColorProperty)
            RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface avaloniaGl)
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
        if (_device is null)
            return;

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
        if (!IsPaused && !_pacer.ShouldPresent(presentationTimestamp, TargetFrameRate))
        {
            _skippedFrames++;
            RequestNextFrameRendering();
            return;
        }

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
        FrameStatistics = new(
            fps, cpuMilliseconds, _submittedFrames, _skippedFrames,
            metrics.DrawCalls, metrics.Flushes, metrics.UploadedBytes, pixelSize,
            metrics.PostProcessingEnabled, _device.OpenGlVersion, _device.Renderer,
            metrics.ResidentTextures, metrics.ResidentTextureBytes);

        if (!IsPaused && RenderMode == EffectRenderMode.Continuous)
            RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface avaloniaGl)
    {
        _activeScene?.DisposeGpuResources();
        _activeScene = null;
        _device?.Dispose();
        _device = null;
        _gl?.Dispose();
        _gl = null;
        _pacer.Reset();
    }

    protected override void OnOpenGlLost()
    {
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
                InitializationFailed?.Invoke(this, new(message, exception));
        });
    }
}
