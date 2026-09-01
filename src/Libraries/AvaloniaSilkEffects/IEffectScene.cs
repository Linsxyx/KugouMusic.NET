using Avalonia;

namespace AvaloniaSilkEffects;

public interface IEffectScene
{
    void Initialize(EffectDevice device);
    void Resize(PixelSize pixelSize, double renderScaling);
    void Update(in EffectFrame frame);
    void Render(EffectRenderContext context);
    void DisposeGpuResources();
}

public abstract class EffectScene : IEffectScene
{
    protected EffectDevice Device { get; private set; } = null!;

    public virtual void Initialize(EffectDevice device) => Device = device;
    public virtual void Resize(PixelSize pixelSize, double renderScaling) { }
    public virtual void Update(in EffectFrame frame) { }
    public abstract void Render(EffectRenderContext context);
    public virtual void DisposeGpuResources() { }
}
