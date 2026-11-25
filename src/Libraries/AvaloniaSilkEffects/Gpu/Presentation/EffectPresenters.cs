using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using AvaloniaSilkEffects.Gpu.OpenGl;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Gpu.Presentation;

/// <summary>Puts a <see cref="SilkEffectControl"/>'s frames on screen with one graphics API.</summary>
internal interface IEffectPresenter : IDisposable
{
    EffectGpuBackend Backend { get; }
    /// <summary>The control that must be a visual child of the host, if any.</summary>
    Control? Child { get; }
    void RequestFrame();
}

/// <summary>The callbacks a presenter drives on its host.</summary>
internal interface IEffectPresenterHost
{
    bool IsRenderingActive { get; }
    /// <summary>Renders one frame on <paramref name="gpu"/>; <paramref name="acquire"/> yields the output for the pixel size,
    /// or null to skip the frame. Returns whether a frame was drawn into the acquired output.</summary>
    bool RenderFrame(GpuDevice gpu, Func<PixelSize, GpuRenderTarget?> acquire);
    /// <summary>Releases the scene and device objects created on the presenter's GPU device (which the presenter keeps).</summary>
    void ReleaseDeviceResources();
    /// <summary>Forgets those objects without touching the API (lost context).</summary>
    void AbandonDeviceResources(string reason);
    /// <summary>A native presenter failed; the host falls back to OpenGL.</summary>
    void PresenterFailed(IEffectPresenter presenter, Exception exception);
}

/// <summary>Chooses a presenter for the platform, the preference and what the compositor can import.</summary>
internal static class EffectPresenterFactory
{
    public static EffectGpuBackend? Native(EffectGpuBackendPreference preference) => preference switch
    {
        EffectGpuBackendPreference.OpenGl => null,
        EffectGpuBackendPreference.Metal => OperatingSystem.IsMacOS() ? EffectGpuBackend.Metal : null,
        EffectGpuBackendPreference.Vulkan => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? EffectGpuBackend.Vulkan : null,
        _ => OperatingSystem.IsMacOS() ? EffectGpuBackend.Metal
            : OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? EffectGpuBackend.Vulkan : null,
    };

    /// <summary>Creates the native presenter, or returns null with the reason it cannot run here.</summary>
    public static async Task<(IEffectPresenter? Presenter, string? Reason)> TryCreateNativeAsync(
        IEffectPresenterHost host, Visual visual, EffectGpuBackend backend)
    {
        if (ElementComposition.GetElementVisual(visual)?.Compositor is not { } compositor)
            return (null, "the control has no compositor");
        var interop = await compositor.TryGetCompositionGpuInterop();
        if (interop is null) return (null, "the compositor has no GPU interop");
        try
        {
            if (backend == EffectGpuBackend.Metal && OperatingSystem.IsMacOS())
                return MetalEffectPresenter.CanImport(interop, out var why)
                    ? (new MetalEffectPresenter(host, visual, compositor, interop), null)
                    : (null, why);
            if (backend == EffectGpuBackend.Vulkan)
                return VulkanEffectPresenter.CanImport(interop, out var why)
                    ? (new VulkanEffectPresenter(host, visual, compositor, interop), null)
                    : (null, why);
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
        return (null, $"{backend} is unavailable on this platform");
    }
}

/// <summary>The OpenGL path: Avalonia's <see cref="OpenGlControlBase"/> as the host's only visual child.</summary>
internal sealed class GlEffectPresenter : IEffectPresenter
{
    private readonly Surface _surface;

    public GlEffectPresenter(IEffectPresenterHost host) => _surface = new Surface(host);

    public EffectGpuBackend Backend => EffectGpuBackend.OpenGl;
    public Control? Child => _surface;
    public void RequestFrame() => _surface.RequestNextFrameRendering();
    public void Dispose() { }

    private sealed class Surface(IEffectPresenterHost host) : OpenGlControlBase
    {
        private GL? _gl;
        private GlGpuDevice? _gpu;

        protected override void OnOpenGlInit(GlInterface avaloniaGl)
        {
            if (host.IsRenderingActive) EnsureDevice(avaloniaGl);
        }

        protected override void OnOpenGlRender(GlInterface avaloniaGl, int framebuffer)
        {
            if (!host.IsRenderingActive)
            {
                // Visibility notifications do not make the GL context current.
                // Use one final callback for cleanup, without updating the scene.
                Release();
                return;
            }
            var gpu = EnsureDevice(avaloniaGl);
            host.RenderFrame(gpu, size => gpu.WrapFramebuffer(framebuffer, size.Width, size.Height));
        }

        protected override void OnOpenGlDeinit(GlInterface avaloniaGl) => Release();

        protected override void OnOpenGlLost()
        {
            host.AbandonDeviceResources("The OpenGL context was lost. Avalonia will recreate the effect resources.");
            _gpu?.Abandon();
            _gpu = null;
            _gl = null;
        }

        private GlGpuDevice EnsureDevice(GlInterface avaloniaGl)
        {
            if (_gpu is not null) return _gpu;
            _gl = GL.GetApi(avaloniaGl.GetProcAddress);
            try
            {
                return _gpu = new GlGpuDevice(_gl);
            }
            catch
            {
                _gl.Dispose();
                _gl = null;
                throw;
            }
        }

        private void Release()
        {
            try { host.ReleaseDeviceResources(); }
            finally
            {
                try { _gpu?.Dispose(); }
                finally { _gl?.Dispose(); }
                _gpu = null;
                _gl = null;
            }
        }
    }
}

/// <summary>
/// A native presenter: frames are drawn into a small swapchain of images shared with the compositor and shown
/// through a <see cref="CompositionSurfaceVisual"/>. Images are reused once the compositor has finished with them.
/// </summary>
internal abstract class CompositionEffectPresenter : IEffectPresenter
{
    private readonly Visual _visual;
    private readonly Compositor _compositor;
    private readonly CompositionSurfaceVisual _surfaceVisual;
    private readonly Action _update;
    private readonly EffectSwapchain _images;
    private SwapchainImage? _drawing;
    private bool _queued;
    private bool _cleanupScheduled;
    private bool _disposed;
    protected bool DeviceFaulted { get; private set; }

    protected CompositionEffectPresenter(IEffectPresenterHost host, Visual visual, Compositor compositor,
        ICompositionGpuInterop interop)
    {
        Host = host;
        _visual = visual;
        _compositor = compositor;
        Interop = interop;
        Surface = compositor.CreateDrawingSurface();
        _surfaceVisual = compositor.CreateSurfaceVisual();
        _surfaceVisual.Surface = Surface;
        _surfaceVisual.Size = new(visual.Bounds.Width, visual.Bounds.Height);
        ElementComposition.SetElementChildVisual(visual, _surfaceVisual);
        _update = Update;
        _images = new EffectSwapchain(CreateImage);
    }

    protected IEffectPresenterHost Host { get; }
    protected ICompositionGpuInterop Interop { get; }
    protected CompositionDrawingSurface Surface { get; }
    protected abstract GpuDevice Gpu { get; }
    public abstract EffectGpuBackend Backend { get; }
    public Control? Child => null;

    public void RequestFrame()
    {
        if (_queued || _disposed) return;
        _queued = true;
        _compositor.RequestCompositionUpdate(_update);
    }

    private void Update()
    {
        _queued = false;
        if (_disposed) return;
        try
        {
            _surfaceVisual.Size = new(_visual.Bounds.Width, _visual.Bounds.Height);
            if (Interop.IsLost) throw new InvalidOperationException("The compositor's GPU device was lost.");
            if (!Host.IsRenderingActive)
            {
                // Hidden: keep the device, give back the scene's objects and the swapchain memory.
                Host.ReleaseDeviceResources();
                _images.Release(force: false);
                ReclaimIdleMemory();
                if (_images.FirstPendingPresent() is { } pending)
                    ScheduleHiddenCleanup(pending);
                return;
            }
            _drawing = null;
            var drawn = Host.RenderFrame(Gpu, Acquire);
            if (_drawing is { } image)
            {
                // An acquired image has its synchronization queued on the GPU; it must be handed over even if
                // nothing was drawn, so the compositor's wait and our next reuse stay balanced.
                image.Present(drawn);
            }
        }
        catch (Exception exception)
        {
            DeviceFaulted = true;
            Host.PresenterFailed(this, exception);
        }
    }

    private void ScheduleHiddenCleanup(Task pending)
    {
        if (_cleanupScheduled) return;
        _cleanupScheduled = true;
        _ = pending.ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _cleanupScheduled = false;
                if (!_disposed && !Host.IsRenderingActive) RequestFrame();
            });
        }, TaskScheduler.Default);
    }

    private GpuRenderTarget? Acquire(PixelSize size)
    {
        var next = _images.Acquire(size);
        if (next is null) return null;
        next.BeginDraw();
        _drawing = next;
        return next.Target;
    }

    protected abstract SwapchainImage CreateImage(PixelSize size);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ElementComposition.SetElementChildVisual(_visual, null);
        try { Host.ReleaseDeviceResources(); }
        finally
        {
            // The compositor may still read in-flight images; wait for the GPU before freeing them.
            WaitIdle();
            _images.Release(force: true);
            Surface.Dispose();
            DisposeDevice();
        }
    }

    protected virtual void ReclaimIdleMemory() => WaitIdle();
    protected abstract void WaitIdle();
    protected abstract void DisposeDevice();
}
