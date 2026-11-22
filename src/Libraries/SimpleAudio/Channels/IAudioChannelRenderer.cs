namespace SimpleAudio;

// 统一声道渲染器接口：同时支持线性矩阵下混（ChannelMixMatrix）与耳机双耳虚拟环绕（BinauralSpatialRenderer）。
internal interface IAudioChannelRenderer
{
    int SourceChannels { get; }
    int TargetChannels { get; }

    void RenderInt16(ReadOnlySpan<short> input, Span<float> output);
    void RenderFloat(ReadOnlySpan<float> input, Span<float> output);
    void Reset();
}
