using Avalonia;

namespace AvaloniaSilkEffects;

public readonly record struct EffectFrame(
    TimeSpan Elapsed,
    TimeSpan Delta,
    PixelSize PixelSize,
    double RenderScaling,
    ulong FrameNumber);

public enum EffectRenderMode
{
    OnDemand,
    Continuous,
}

public enum EffectBlendMode
{
    Alpha,
    Additive,
    Screen,
    Multiply,
}
