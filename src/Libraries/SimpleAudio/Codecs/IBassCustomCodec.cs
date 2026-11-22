namespace SimpleAudio.Codecs;

// 描述自定义解码器返回的按需 PCM/WAV 流及其原始/输出声道拓扑。
internal sealed record CustomDecodedStream(
    Stream WaveStream,
    AudioChannelLayout SourceLayout,
    AudioChannelLayout TargetLayout);

// 可插拔的自定义音频格式解码扩展点，与 ManagedStreamSource 及底层缓存层完全解耦。
internal interface IBassCustomCodec
{
    // 廉价魔数预判，避免对无关格式执行完整容器扫描。
    bool MightMatch(ReadOnlySpan<byte> header);

    // 尝试打开可寻址输入流；若不匹配该格式必须将 input.Position 复位为 0 并返回 false。
    bool TryOpen(
        Stream seekableInput,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels,
        out CustomDecodedStream? decoded,
        out string? error);
}
