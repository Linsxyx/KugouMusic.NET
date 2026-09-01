using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

internal sealed class EffectFramebuffer : IDisposable
{
    private readonly GL _gl;

    public EffectFramebuffer(GL gl) => _gl = gl;

    public uint Framebuffer { get; private set; }
    public uint Texture { get; private set; }
    public uint DepthStencilBuffer { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public unsafe void EnsureSize(int width, int height)
    {
        if (width == Width && height == Height && Framebuffer != 0)
            return;
        DisposeHandles();
        Width = width;
        Height = height;

        Texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        Framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, Texture, 0);
        DepthStencilBuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            DisposeHandles();
            throw new InvalidOperationException("AvaloniaSilkEffects could not create a complete post-process framebuffer.");
        }
    }

    private void DisposeHandles()
    {
        if (Framebuffer != 0)
            _gl.DeleteFramebuffer(Framebuffer);
        if (Texture != 0)
            _gl.DeleteTexture(Texture);
        if (DepthStencilBuffer != 0)
            _gl.DeleteRenderbuffer(DepthStencilBuffer);
        Framebuffer = 0;
        Texture = 0;
        DepthStencilBuffer = 0;
        Width = 0;
        Height = 0;
    }

    public void Dispose() => DisposeHandles();
}
