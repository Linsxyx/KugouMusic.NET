using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

internal sealed class EffectFramebuffer(GL gl) : IDisposable
{
    public uint Framebuffer { get; private set; }
    public uint Texture { get; private set; }
    public uint ColorBuffer { get; private set; }
    public uint DepthStencilBuffer { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Samples { get; private set; } = 1;
    private int _requestedSamples;

    public unsafe void EnsureSize(int width, int height, int samples = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        if (width == Width && height == Height && samples == _requestedSamples && Framebuffer != 0)
            return;
        DisposeHandles();
        Width = width;
        Height = height;
        _requestedSamples = samples;

        Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        if (samples > 1)
        {
            ColorBuffer = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ColorBuffer);
            gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)samples,
                InternalFormat.Rgba8, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, ColorBuffer);
        }
        else
        {
            Texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, Texture);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, null);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, Texture, 0);
        }
        DepthStencilBuffer = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        if (samples > 1)
            gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)samples,
                InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        else
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            DisposeHandles();
            throw new InvalidOperationException($"AvaloniaSilkEffects framebuffer {width}x{height}, {samples} samples is incomplete: {status}.");
        }
        gl.GetInteger(GetPName.Samples, out var actualSamples);
        Samples = Math.Max(1, actualSamples);
    }

    public void ResolveTo(EffectFramebuffer destination)
    {
        if (Framebuffer == 0 || destination.Framebuffer == 0 || Samples <= 1 || destination.Samples != 1 ||
            Width != destination.Width || Height != destination.Height)
            throw new InvalidOperationException("MSAA resolve requires an equally sized single-sample destination.");

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination.Framebuffer);
        gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, destination.Framebuffer);
    }

    private void DisposeHandles()
    {
        if (Framebuffer != 0)
            gl.DeleteFramebuffer(Framebuffer);
        if (Texture != 0)
            gl.DeleteTexture(Texture);
        if (ColorBuffer != 0)
            gl.DeleteRenderbuffer(ColorBuffer);
        if (DepthStencilBuffer != 0)
            gl.DeleteRenderbuffer(DepthStencilBuffer);
        Framebuffer = 0;
        Texture = 0;
        ColorBuffer = 0;
        DepthStencilBuffer = 0;
        Width = 0;
        Height = 0;
        Samples = 1;
        _requestedSamples = 0;
    }

    public void Dispose() => DisposeHandles();
}
