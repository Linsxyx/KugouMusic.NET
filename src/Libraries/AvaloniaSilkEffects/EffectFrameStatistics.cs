using Avalonia;

namespace AvaloniaSilkEffects;

public readonly record struct EffectFrameStatistics(
    double FramesPerSecond,
    double CpuMilliseconds,
    ulong SubmittedFrames,
    ulong SkippedFrames,
    int DrawCalls,
    int Flushes,
    long UploadedBytes,
    PixelSize FramebufferSize,
    bool PostProcessingEnabled,
    string OpenGlVersion,
    string Renderer,
    int ResidentTextures = 0,
    long ResidentTextureBytes = 0);
