namespace SimpleAudio.Codecs.Av3a;

// AV3A 自定义编解码器适配器：仅对 MP4/M4A 魔数触发容器扫描，匹配后构建按需多声道自适应流。
internal sealed class Av3aCodec : IBassCustomCodec
{
    public bool MightMatch(ReadOnlySpan<byte> header)
    {
        return header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8);
    }

    public bool TryOpen(
        Stream seekableInput,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels,
        out CustomDecodedStream? decoded,
        out string? error)
    {
        decoded = null;
        error = null;

        try
        {
            var track = Av3aMp4Track.TryRead(seekableInput);
            if (track == null)
            {
                seekableInput.Position = 0;
                return false;
            }

            var waveStream = new Av3aPcmWaveStream(
                seekableInput,
                track,
                deviceSpeakers,
                channelMode,
                maxSupportedChannels);

            decoded = new CustomDecodedStream(waveStream, waveStream.SourceLayout, waveStream.TargetLayout);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or
                                   DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or
                                   OverflowException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
