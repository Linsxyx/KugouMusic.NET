using System.Numerics;
using System.Runtime.InteropServices;

namespace AvaloniaSilkEffects.Gpu;

/// <summary>The graphics API an effect surface renders with.</summary>
public enum EffectGpuBackend
{
    OpenGl,
    Metal,
    Vulkan,
}

/// <summary>Which backend <see cref="SilkEffectControl"/> tries first. Every choice falls back to OpenGL.</summary>
public enum EffectGpuBackendPreference
{
    /// <summary>Metal on macOS, Vulkan on Windows and Linux, when the compositor can import their images.</summary>
    Auto,
    Metal,
    Vulkan,
    OpenGl,
}

internal enum GpuBlend
{
    None,
    /// <summary>Premultiplied source-over: One, OneMinusSrcAlpha.</summary>
    Alpha,
    Additive,
    Screen,
    Multiply,
}

internal enum GpuStencil
{
    Disabled,
    /// <summary>Always passes and writes 1 (mask drawing, usually with color writes off).</summary>
    WriteOne,
    /// <summary>Passes where the stencil is 1 and keeps it.</summary>
    EqualOne,
}

internal enum GpuFilter
{
    Nearest,
    Linear,
    /// <summary>Trilinear; the texture owns a full mip chain.</summary>
    LinearMipmap,
}

internal enum GpuTopology
{
    Triangles,
    TriangleStrip,
}

/// <summary>A rectangle in target pixels with OpenGL's bottom-left origin; every backend keeps that convention.</summary>
internal readonly record struct GpuRect(int X, int Y, int Width, int Height);

internal readonly record struct GpuVertexAttribute(int Location, int Components, int Offset);

/// <summary>Interleaved float vertex attributes. Instances are shared and compared by reference.</summary>
internal sealed class GpuVertexLayout(int stride, params GpuVertexAttribute[] attributes)
{
    public int Stride { get; } = stride;
    public IReadOnlyList<GpuVertexAttribute> Attributes { get; } = attributes;

    public static GpuVertexLayout For<T>(params GpuVertexAttribute[] attributes) where T : unmanaged =>
        new(Marshal.SizeOf<T>(), attributes);
}

/// <summary>
/// The small immediate-mode GPU interface the effects draw through. State (target, viewport, scissor, blend,
/// color writes, stencil) is sticky like OpenGL's; each draw snapshots it. Coordinates and texture orientation
/// follow OpenGL in every backend: a render target's row 0 is its bottom, and the presenter flips the final image.
/// </summary>
internal abstract class GpuDevice : IDisposable
{
    private GpuRenderTarget? _target;
    private bool _disposed;

    protected GpuDevice() => Resources = new EffectGpuResources();

    public abstract EffectGpuBackend Backend { get; }
    /// <summary>API and version, e.g. "OpenGL 4.1 Metal - 89.4" or "Metal 3".</summary>
    public abstract string ApiVersion { get; }
    public abstract string Renderer { get; }
    public abstract int MaxSamples { get; }
    /// <summary>OpenGL ES rasterizes multisampled targets without GL_MULTISAMPLE; only the GL backend cares.</summary>
    public virtual bool IsGles => false;

    public EffectGpuResources Resources { get; }
    public bool IsDisposed => _disposed;

    public GpuRenderTarget? Target => _target;
    public GpuRect Viewport { get; private set; }
    public GpuRect? Scissor { get; private set; }
    public GpuBlend Blend { get; private set; } = GpuBlend.Alpha;
    public bool ColorWrite { get; private set; } = true;
    public GpuStencil Stencil { get; private set; }

    public abstract GpuTexture CreateTexture(int width, int height, ReadOnlySpan<byte> rgba, GpuFilter filter);
    public abstract GpuBuffer CreateBuffer(ReadOnlySpan<byte> data, bool index);
    public abstract GpuShader CreateShader(string vertexSource, string fragmentSource, string name);
    /// <summary>Requests stencil-mask storage when true. Effects never test or write depth;
    /// a backend may provide a stencil-only attachment instead of a combined depth/stencil format.</summary>
    public abstract GpuRenderTarget CreateRenderTarget(bool depthStencil);

    public GpuBuffer CreateBuffer<T>(ReadOnlySpan<T> data, bool index = false) where T : unmanaged =>
        CreateBuffer(MemoryMarshal.AsBytes(data), index);

    /// <summary>Binds <paramref name="target"/> and resets the viewport to all of it.</summary>
    public void SetTarget(GpuRenderTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(_target, target))
        {
            _target = target;
            OnTargetChanged(target);
        }
        Viewport = new GpuRect(0, 0, target.Width, target.Height);
    }

    public void SetViewport(GpuRect viewport) => Viewport = viewport;
    public void SetScissor(GpuRect? scissor) => Scissor = scissor;
    public void SetBlend(GpuBlend blend) => Blend = blend;
    public void SetColorWrite(bool enabled) => ColorWrite = enabled;
    public void SetStencil(GpuStencil stencil) => Stencil = stencil;

    /// <summary>Like glClear: honours the scissor; color clears honour color writes.</summary>
    public abstract void Clear(EffectColor? color, bool stencil = false);

    public void DrawFullscreen(GpuShader shader) => Draw(shader, GpuTopology.Triangles, 3);
    public abstract void Draw(GpuShader shader, GpuTopology topology, int vertexCount);
    public abstract void Draw(GpuShader shader, GpuVertexLayout layout, ReadOnlySpan<byte> vertices, int vertexCount);
    public abstract void Draw(GpuShader shader, GpuVertexLayout layout, GpuBuffer vertices, int firstVertex, int vertexCount);
    public abstract void DrawIndexed(GpuShader shader, GpuVertexLayout layout, GpuBuffer vertices, GpuBuffer indices,
        int firstIndex, int indexCount);

    public void Draw<T>(GpuShader shader, GpuVertexLayout layout, ReadOnlySpan<T> vertices) where T : unmanaged =>
        Draw(shader, layout, MemoryMarshal.AsBytes(vertices), vertices.Length);

    /// <summary>
    /// Copies <paramref name="source"/>'s color into an equally sized single-sample target, resolving it when
    /// multisampled, and leaves the destination bound. Always covers the whole target.
    /// </summary>
    public abstract void Resolve(GpuRenderTarget source, GpuRenderTarget destination);

    /// <summary>Starts a frame that ends on <paramref name="output"/>; binds it and resets all state.</summary>
    public virtual void BeginFrame(GpuRenderTarget output)
    {
        Resources.BeginFrame();
        _target = null;
        Scissor = null;
        Blend = GpuBlend.Alpha;
        ColorWrite = true;
        Stencil = GpuStencil.Disabled;
        SetTarget(output);
    }

    /// <summary>Submits the frame's work. OpenGL leaves that to the platform's swap.</summary>
    public virtual void EndFrame() { }

    protected virtual void OnTargetChanged(GpuRenderTarget target) { }

    /// <summary>Drops every resource without touching the API, after the context or device is lost.</summary>
    public virtual void Abandon()
    {
        _disposed = true;
        Resources.Abandon();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCore();
    }

    protected abstract void DisposeCore();
}

internal abstract class GpuResource : IDisposable
{
    private static long _nextId;
    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        DisposeCore();
    }

    /// <summary>Marks the resource dead without releasing API objects (lost context).</summary>
    public void Abandon() => IsDisposed = true;

    protected abstract void DisposeCore();
}

internal abstract class GpuTexture : GpuResource
{
    public int Width { get; protected init; }
    public int Height { get; protected init; }
    public GpuFilter Filter { get; protected init; }
}

internal abstract class GpuBuffer : GpuResource
{
    public long Size { get; protected init; }
}

/// <summary>
/// A color target (RGBA8) with an optional stencil-mask attachment (combined with depth on some backends). Resizing recreates the attachments;
/// <see cref="Texture"/> is null while multisampled (resolve into a single-sample target to sample it).
/// </summary>
internal abstract class GpuRenderTarget : GpuResource
{
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public int Samples { get; protected set; } = 1;
    public bool HasDepthStencil { get; protected set; }
    public abstract GpuTexture? Texture { get; }
    public bool IsAllocated => Width > 0;

    /// <summary>Throws <see cref="InvalidOperationException"/> when the configuration is unsupported.</summary>
    public abstract void EnsureSize(int width, int height, int samples = 1);

    /// <summary>Frees the attachments; a later <see cref="EnsureSize"/> allocates them again.</summary>
    public abstract void Release();

    protected override void DisposeCore() => Release();
}

/// <summary>
/// A linked program. Uniforms and textures are stored by name and applied at draw time, so a shader keeps its
/// values between draws like an OpenGL program object. Unknown names are ignored.
/// </summary>
internal abstract class GpuShader : GpuResource
{
    private readonly float[] _scratch = new float[16];

    public string Name { get; protected init; } = "";

    public abstract void SetFloats(string name, ReadOnlySpan<float> values, int components);
    public abstract void SetInt(string name, int value);
    public abstract void SetTexture(string name, GpuTexture? texture);

    public void Set(string name, float value) => SetFloats(name, [value], 1);
    public void Set(string name, int value) => SetInt(name, value);
    public void Set(string name, float x, float y) => SetFloats(name, [x, y], 2);
    public void Set(string name, float x, float y, float z) => SetFloats(name, [x, y, z], 3);
    public void Set(string name, float x, float y, float z, float w) => SetFloats(name, [x, y, z, w], 4);
    public void Set(string name, Vector2 value) => Set(name, value.X, value.Y);
    public void Set(string name, Vector3 value) => Set(name, value.X, value.Y, value.Z);
    public void Set(string name, Vector4 value) => Set(name, value.X, value.Y, value.Z, value.W);
    public void Set(string name, EffectColor value) => Set(name, value.R, value.G, value.B, value.A);
    public void Set(string name, ReadOnlySpan<Vector3> values) => SetFloats(name, MemoryMarshal.Cast<Vector3, float>(values), 3);
    public void Set(string name, ReadOnlySpan<Vector4> values) => SetFloats(name, MemoryMarshal.Cast<Vector4, float>(values), 4);

    /// <summary>A 2D affine transform as a column-major mat3.</summary>
    public void SetMatrix(string name, Matrix3x2 m) =>
        SetFloats(name, [m.M11, m.M12, 0, m.M21, m.M22, 0, m.M31, m.M32, 1], 9);

    /// <summary>Uploads <paramref name="m"/>'s rows as the matrix columns (glUniformMatrix4fv with transpose).</summary>
    public void SetMatrixTransposed(string name, Matrix4x4 m)
    {
        var s = _scratch;
        s[0] = m.M11; s[1] = m.M21; s[2] = m.M31; s[3] = m.M41;
        s[4] = m.M12; s[5] = m.M22; s[6] = m.M32; s[7] = m.M42;
        s[8] = m.M13; s[9] = m.M23; s[10] = m.M33; s[11] = m.M43;
        s[12] = m.M14; s[13] = m.M24; s[14] = m.M34; s[15] = m.M44;
        SetFloats(name, s, 16);
    }
}
