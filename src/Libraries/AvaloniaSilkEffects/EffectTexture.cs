using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

public sealed class EffectTexture : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    internal EffectTexture(GL gl, uint handle, int width, int height, Vector2 logicalSize)
    {
        _gl = gl;
        Handle = handle;
        Width = width;
        Height = height;
        LogicalSize = logicalSize;
    }

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public Vector2 LogicalSize { get; }
    public bool IsDisposed => _disposed;

    internal void Abandon() => _disposed = true;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gl.DeleteTexture(Handle);
    }
}

public readonly record struct TextTextureKey(
    string Text,
    string FontFamily,
    float FontSize,
    int FontWeight,
    EffectColor Color,
    float RasterScale);
