using Avalonia;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

public sealed class EffectDevice : IDisposable
{
    private readonly PostProcessPipeline _postProcessPipeline;
    private readonly EffectRenderContext _renderContext;
    private bool _disposed;

    internal EffectDevice(GL gl)
    {
        Gl = gl;
        OpenGlVersion = gl.GetStringS(StringName.Version);
        Renderer = gl.GetStringS(StringName.Renderer);
        gl.GetInteger(GetPName.MajorVersion, out var major);
        gl.GetInteger(GetPName.MinorVersion, out var minor);
        var isGles = OpenGlVersion.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        if ((!isGles && (major < 3 || major == 3 && minor < 3)) ||
            (isGles && (major < 3)))
            throw new NotSupportedException($"AvaloniaSilkEffects requires OpenGL 3.3+ or OpenGL ES 3.0+; current context is {OpenGlVersion}.");
        Primitives = new(gl);
        Textures = new(gl);
        PostProcess = new();
        _postProcessPipeline = new(gl);
        _renderContext = new(this, Primitives);
    }

    internal GL Gl { get; }
    public string OpenGlVersion { get; }
    public string Renderer { get; }
    public EffectPrimitiveRenderer Primitives { get; }
    public EffectTextureCache Textures { get; }
    public PostProcessSettings PostProcess { get; }
    public EffectDeviceFrameMetrics FrameMetrics { get; private set; }

    internal void Render(
        IEffectScene scene,
        in EffectFrame frame,
        int targetFramebuffer,
        EffectColor clearColor)
    {
        ThrowIfDisposed();
        Textures.BeginFrame();
        scene.Update(frame);
        var postProcessingEnabled = PostProcess.IsEnabled;
        _postProcessPipeline.Begin(
            frame.PixelSize.Width, frame.PixelSize.Height, targetFramebuffer,
            clearColor, postProcessingEnabled, PostProcess.ResolutionScale);
        Gl.Disable(EnableCap.DepthTest);
        Gl.Disable(EnableCap.CullFace);
        Gl.Disable(EnableCap.ScissorTest);
        Gl.Disable(EnableCap.StencilTest);
        Primitives.Begin(frame.PixelSize.Width, frame.PixelSize.Height);
        _renderContext.PixelSize = frame.PixelSize;
        scene.Render(_renderContext);
        Primitives.Flush();
        _postProcessPipeline.End(targetFramebuffer, PostProcess, postProcessingEnabled);
        Textures.Collect();
        FrameMetrics = new(
            Primitives.FrameDrawCalls + _postProcessPipeline.FrameDrawCalls,
            Primitives.FrameFlushes,
            Primitives.FrameUploadedBytes,
            postProcessingEnabled,
            Textures.Count,
            Textures.ResidentBytes);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Textures.Dispose();
        Primitives.Dispose();
        _postProcessPipeline.Dispose();
    }

    internal void Abandon()
    {
        if (_disposed)
            return;
        _disposed = true;
        Textures.Abandon();
    }
}

public readonly record struct EffectDeviceFrameMetrics(
    int DrawCalls,
    int Flushes,
    long UploadedBytes,
    bool PostProcessingEnabled,
    int ResidentTextures,
    long ResidentTextureBytes);
