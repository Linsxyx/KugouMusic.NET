using System.Buffers.Binary;

namespace SimpleAudio;

// 构造符合规范的 IEEE Float32 WAV 头：<=2 声道使用标准 44B 头，>2 声道使用带 dwChannelMask 的 68B WAVE_FORMAT_EXTENSIBLE 头。
internal static class WaveHeaderBuilder
{
    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: 00000003-0000-0010-8000-00aa00389b71
    private static ReadOnlySpan<byte> IeeeFloatSubFormatGuid =>
    [
        0x03, 0x00, 0x00, 0x00,
        0x00, 0x00,
        0x10, 0x00,
        0x80, 0x00,
        0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71
    ];

    public static byte[] CreateFloatWaveHeader(int sampleRate, AudioChannelLayout layout, long dataLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(dataLength);

        var channels = layout.Channels;
        var blockAlign = checked((ushort)(channels * sizeof(float)));
        var byteRate = checked((uint)(sampleRate * blockAlign));

        if (channels <= 2)
        {
            if (dataLength > uint.MaxValue - 36)
                throw new NotSupportedException("PCM 数据超过 WAV 4 GiB 上限。");

            var dataSize = (uint)dataLength;
            var header = new byte[44];
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), dataSize + 36);
            "WAVEfmt "u8.CopyTo(header.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);        // fmt chunk size
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 3);         // WAVE_FORMAT_IEEE_FLOAT
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)sampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), byteRate);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), blockAlign);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 32);        // bits per sample
            "data"u8.CopyTo(header.AsSpan(36));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), dataSize);
            return header;
        }
        else
        {
            if (dataLength > uint.MaxValue - 60)
                throw new NotSupportedException("多声道 PCM 数据超过 WAV 4 GiB 上限。");

            var dataSize = (uint)dataLength;
            var header = new byte[68];
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), dataSize + 60);
            "WAVEfmt "u8.CopyTo(header.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 40);        // WAVEFORMATEXTENSIBLE fmt size
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 0xFFFE);    // WAVE_FORMAT_EXTENSIBLE
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)sampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), byteRate);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), blockAlign);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 32);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(36), 22);        // cbSize
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38), 32);        // wValidBitsPerSample
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), layout.WaveChannelMask);
            IeeeFloatSubFormatGuid.CopyTo(header.AsSpan(44));
            "data"u8.CopyTo(header.AsSpan(60));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), dataSize);
            return header;
        }
    }
}
