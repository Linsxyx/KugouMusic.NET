using Avalonia;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

public sealed class EffectRenderContext
{
    private readonly GL _gl;
    private readonly Stack<Rect> _clips = [];

    internal EffectRenderContext(EffectDevice device, EffectPrimitiveRenderer primitives)
    {
        Device = device;
        Primitives = primitives;
        _gl = device.Gl;
    }

    public EffectDevice Device { get; }
    public EffectPrimitiveRenderer Primitives { get; }
    public PixelSize PixelSize { get; internal set; }

    public void Render(EffectNode node) => node.Render(this);

    public IDisposable PushClip(Rect pixelRect)
    {
        Primitives.Flush();
        _clips.Push(pixelRect);
        ApplyClip(pixelRect);
        return new Scope(() =>
        {
            Primitives.Flush();
            _clips.Pop();
            if (_clips.TryPeek(out var previous))
                ApplyClip(previous);
            else
                _gl.Disable(EnableCap.ScissorTest);
        });
    }

    public IDisposable PushStencilMask(Action<EffectPrimitiveRenderer> drawMask)
    {
        ArgumentNullException.ThrowIfNull(drawMask);
        Primitives.Flush();
        _gl.Enable(EnableCap.StencilTest);
        _gl.Clear(ClearBufferMask.StencilBufferBit);
        _gl.ColorMask(false, false, false, false);
        _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
        _gl.StencilOp(Silk.NET.OpenGL.StencilOp.Keep, Silk.NET.OpenGL.StencilOp.Keep, Silk.NET.OpenGL.StencilOp.Replace);
        drawMask(Primitives);
        Primitives.Flush();
        _gl.ColorMask(true, true, true, true);
        _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
        _gl.StencilOp(Silk.NET.OpenGL.StencilOp.Keep, Silk.NET.OpenGL.StencilOp.Keep, Silk.NET.OpenGL.StencilOp.Keep);
        return new Scope(() =>
        {
            Primitives.Flush();
            _gl.Disable(EnableCap.StencilTest);
        });
    }

    private void ApplyClip(Rect clip)
    {
        var x = Math.Max(0, (int)Math.Floor(clip.X));
        var y = Math.Max(0, PixelSize.Height - (int)Math.Ceiling(clip.Bottom));
        var width = Math.Max(0, Math.Min(PixelSize.Width - x, (int)Math.Ceiling(clip.Width)));
        var height = Math.Max(0, Math.Min(PixelSize.Height - y, (int)Math.Ceiling(clip.Height)));
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x, y, (uint)width, (uint)height);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
