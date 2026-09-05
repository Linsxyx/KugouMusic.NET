using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

internal sealed class EffectFramebuffer(GL gl) : IDisposable
{
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

        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, Texture, 0);
        DepthStencilBuffer = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, DepthStencilBuffer);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            DisposeHandles();
            throw new InvalidOperationException("AvaloniaSilkEffects could not create a complete post-process framebuffer.");
        }
    }

    private void DisposeHandles()
    {
        if (Framebuffer != 0)
            gl.DeleteFramebuffer(Framebuffer);
        if (Texture != 0)
            gl.DeleteTexture(Texture);
        if (DepthStencilBuffer != 0)
            gl.DeleteRenderbuffer(DepthStencilBuffer);
        Framebuffer = 0;
        Texture = 0;
        DepthStencilBuffer = 0;
        Width = 0;
        Height = 0;
    }

    public void Dispose() => DisposeHandles();
}
