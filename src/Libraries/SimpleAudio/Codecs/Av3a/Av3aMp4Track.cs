using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace SimpleAudio.Codecs.Av3a;

// 轻量批量解析 MP4/M4A 容器中的 AV3A 音轨索引表（支持跳过封面视频轨、批量读表与 elst 起始裁剪）。
internal sealed class Av3aMp4Track
{
    internal readonly record struct Sample(long Offset, int Size);
    private readonly record struct Box(string Type, long Payload, long End);

    public required Sample[] Samples { get; init; }
    public int SamplesPerFrame { get; init; }
    public uint Timescale { get; init; }
    public long TrimStartSamples { get; init; }
    public long MediaDurationSamples { get; init; }

    public static Av3aMp4Track? TryRead(Stream input)
    {
        var originalPosition = input.Position;
        var isAv3a = false;
        try
        {
            Span<byte> signature = stackalloc byte[8];
            input.Position = 0;
            if (!TryReadExactly(input, signature) || !signature[4..].SequenceEqual("ftyp"u8))
                return null;

            var length = input.Length;
            if (!TryFindBox(input, 0, length, "moov", out var moov))
                return null;

            var mvhdTimescale = ReadMovieTimescale(input, moov);

            foreach (var trak in EnumerateBoxes(input, moov.Payload, moov.End))
            {
                if (trak.Type != "trak")
                    continue;

                // 非音频轨（如 mjpeg 封面轨、章节轨）可能不具备标准音频盒结构，安全跳过即可。
                if (!TryFindBox(input, trak.Payload, trak.End, "mdia", out var mdia) ||
                    !TryFindBox(input, mdia.Payload, mdia.End, "minf", out var minf) ||
                    !TryFindBox(input, minf.Payload, minf.End, "stbl", out var stbl) ||
                    !TryFindBox(input, stbl.Payload, stbl.End, "stsd", out var stsd))
                {
                    continue;
                }

                if (stsd.End - stsd.Payload < 8)
                    continue;

                var entryCount = ReadUInt32(input, stsd.Payload + 4);
                var descriptions = EnumerateBoxes(input, stsd.Payload + 8, stsd.End).ToArray();
                if (!descriptions.Any(static b => b.Type == "av3a"))
                    continue;

                isAv3a = true;
                if (descriptions.Length != 1 || entryCount != 1)
                    throw new NotSupportedException("AV3A 多采样描述或加密 MP4 暂不支持。");

                var trackTimescale = ReadMediaTimescale(input, mdia);
                var (samplesPerFrame, timingSamples) = ReadStts(input, stbl);
                var (fixedSize, sampleCount, sampleSizesBuffer, sampleSizesRented) = ReadStsz(input, stbl, length, timingSamples);
                try
                {
                    var samples = BuildSampleTable(
                        input,
                        stbl,
                        length,
                        fixedSize,
                        sampleCount,
                        sampleSizesBuffer);

                    var (trimStart, mediaDuration) = ReadEditList(input, trak, mvhdTimescale, trackTimescale);

                    return new Av3aMp4Track
                    {
                        Samples = samples,
                        SamplesPerFrame = samplesPerFrame,
                        Timescale = trackTimescale,
                        TrimStartSamples = trimStart,
                        MediaDurationSamples = mediaDuration
                    };
                }
                finally
                {
                    if (sampleSizesRented != null)
                        ArrayPool<byte>.Shared.Return(sampleSizesRented);
                }
            }

            return null;
        }
        catch (Exception ex) when (!isAv3a && ex is IOException or InvalidDataException or NotSupportedException or OverflowException)
        {
            // 非 AV3A 或未知容器变体交还 BASS 正常处理。
            return null;
        }
        finally
        {
            if (input.CanSeek)
                input.Position = originalPosition;
        }
    }

    public int ReadPacket(Stream input, int index, Span<byte> buffer)
    {
        var sample = Samples[index];
        input.Position = sample.Offset;
        input.ReadExactly(buffer[..sample.Size]);
        return sample.Size;
    }

    // 批量解析 stts，返回统一帧长与总帧数。
    private static (int SamplesPerFrame, ulong TotalSamples) ReadStts(Stream input, Box stbl)
    {
        var stts = RequireBox(input, stbl, "stts");
        var entryCount = ReadTableCount(input, stts, entryWidth: 8);
        if (entryCount == 0)
            throw new InvalidDataException("AV3A MP4 缺少有效的 stts 时间表。");

        var byteLength = checked(entryCount * 8);
        var rented = ArrayPool<byte>.Shared.Rent(byteLength);
        try
        {
            var span = rented.AsSpan(0, byteLength);
            input.Position = stts.Payload + 8;
            input.ReadExactly(span);

            ulong totalSamples = 0;
            uint frameDelta = 0;
            for (var i = 0; i < entryCount; i++)
            {
                var count = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 8, 4));
                var delta = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 8 + 4, 4));
                if (count == 0)
                    continue;

                if (delta == 0)
                    throw new InvalidDataException("AV3A MP4 帧时长无效。");

                if (frameDelta == 0)
                    frameDelta = delta;
                else if (delta != frameDelta)
                    throw new NotSupportedException("AV3A MP4 包含变长帧时长，暂不支持。");

                totalSamples += count;
            }

            if (frameDelta == 0 || totalSamples == 0)
                throw new InvalidDataException("AV3A MP4 stts 采样计数无效。");

            return (checked((int)frameDelta), totalSamples);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // 批量读取 stsz 采样尺寸表，避免逐样本 Seek/Read。
    private static (uint FixedSize, int Count, ReadOnlyMemory<byte> TableBytes, byte[]? Rented) ReadStsz(
        Stream input,
        Box stbl,
        long fileLength,
        ulong expectedTimingSamples)
    {
        var stsz = RequireBox(input, stbl, "stsz");
        if (stsz.End - stsz.Payload < 12)
            throw new InvalidDataException("AV3A MP4 stsz 盒长度无效。");

        Span<byte> header = stackalloc byte[8];
        input.Position = stsz.Payload + 4;
        input.ReadExactly(header);
        var fixedSize = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);

        if (count == 0 || count > 10_000_000 || count > (ulong)fileLength || count != expectedTimingSamples)
            throw new InvalidDataException("AV3A MP4 采样数量无效。");

        var sampleCount = checked((int)count);
        if (fixedSize > 0)
        {
            return (fixedSize, sampleCount, ReadOnlyMemory<byte>.Empty, null);
        }

        var requiredBytes = checked(sampleCount * 4L);
        if (requiredBytes > stsz.End - stsz.Payload - 12)
            throw new InvalidDataException("AV3A MP4 采样尺寸表不完整。");

        var rented = ArrayPool<byte>.Shared.Rent((int)requiredBytes);
        try
        {
            input.Position = stsz.Payload + 12;
            input.ReadExactly(rented.AsSpan(0, (int)requiredBytes));
            return (0, sampleCount, rented.AsMemory(0, (int)requiredBytes), rented);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented);
            throw;
        }
    }

    // 批量读取 stsc 与 stco/co64，在内存中一次性构建全部帧偏移索引。
    private static Sample[] BuildSampleTable(
        Stream input,
        Box stbl,
        long fileLength,
        uint fixedSize,
        int sampleCount,
        ReadOnlyMemory<byte> stszTable)
    {
        var stsc = RequireBox(input, stbl, "stsc");
        var mappingCount = ReadTableCount(input, stsc, entryWidth: 12);
        if (mappingCount == 0)
            throw new InvalidDataException("AV3A MP4 分块表为空。");

        var mappings = new (uint FirstChunk, uint SamplesPerChunk)[mappingCount];
        var stscBytes = checked(mappingCount * 12);
        var stscRented = ArrayPool<byte>.Shared.Rent(stscBytes);
        try
        {
            var span = stscRented.AsSpan(0, stscBytes);
            input.Position = stsc.Payload + 8;
            input.ReadExactly(span);

            for (var i = 0; i < mappingCount; i++)
            {
                var off = i * 12;
                var firstChunk = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(off, 4));
                var samplesPerChunk = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(off + 4, 4));
                var descIndex = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(off + 8, 4));
                if (samplesPerChunk == 0 || descIndex != 1 ||
                    (i == 0 ? firstChunk != 1 : firstChunk <= mappings[i - 1].FirstChunk))
                {
                    throw new InvalidDataException("AV3A MP4 分块映射无效。");
                }

                mappings[i] = (firstChunk, samplesPerChunk);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(stscRented);
        }

        var isWide = false;
        if (!TryFindBox(input, stbl.Payload, stbl.End, "stco", out var offsetBox))
        {
            isWide = true;
            if (!TryFindBox(input, stbl.Payload, stbl.End, "co64", out offsetBox))
                throw new InvalidDataException("AV3A MP4 缺少分块偏移表。");
        }

        var entryWidth = isWide ? 8 : 4;
        var chunkCount = ReadTableCount(input, offsetBox, entryWidth);
        if (mappings[^1].FirstChunk > chunkCount)
            throw new InvalidDataException("AV3A MP4 分块映射超出偏移表。");

        var offsetBytesLen = checked(chunkCount * entryWidth);
        var offsetRented = ArrayPool<byte>.Shared.Rent(offsetBytesLen);
        try
        {
            var offsetSpan = offsetRented.AsSpan(0, offsetBytesLen);
            input.Position = offsetBox.Payload + 8;
            input.ReadExactly(offsetSpan);

            var sizesSpan = stszTable.Span;
            var samples = new Sample[sampleCount];
            var sampleIndex = 0;
            var mappingIndex = 0;

            for (var chunk = 1; chunk <= chunkCount; chunk++)
            {
                while (mappingIndex + 1 < mappings.Length && mappings[mappingIndex + 1].FirstChunk <= chunk)
                    mappingIndex++;

                var countInChunk = mappings[mappingIndex].SamplesPerChunk;
                if (countInChunk > sampleCount - sampleIndex)
                    throw new InvalidDataException("AV3A MP4 分块超出采样表。");

                var chunkOffIdx = (chunk - 1) * entryWidth;
                var offset = isWide
                    ? checked((long)BinaryPrimitives.ReadUInt64BigEndian(offsetSpan.Slice(chunkOffIdx, 8)))
                    : BinaryPrimitives.ReadUInt32BigEndian(offsetSpan.Slice(chunkOffIdx, 4));

                for (var j = 0; j < countInChunk; j++)
                {
                    var size = fixedSize > 0
                        ? fixedSize
                        : BinaryPrimitives.ReadUInt32BigEndian(sizesSpan.Slice(sampleIndex * 4, 4));
                    if (size < 9 || size > 1_048_576 || offset < 0 || offset > fileLength - size)
                        throw new InvalidDataException("AV3A MP4 采样超出文件范围。");

                    samples[sampleIndex++] = new Sample(offset, (int)size);
                    offset += size;
                }
            }

            if (sampleIndex != samples.Length)
                throw new InvalidDataException("AV3A MP4 采样数量与分块不一致。");

            return samples;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(offsetRented);
        }
    }

    // 解析常见单段 edit list（含 encoder priming 延迟偏移），遇到复杂多段拼接或空白段仍显式报错。
    private static (long TrimStartSamples, long MediaDurationSamples) ReadEditList(
        Stream input,
        Box trak,
        uint movieTimescale,
        uint trackTimescale)
    {
        if (!TryFindBox(input, trak.Payload, trak.End, "edts", out var edts) ||
            !TryFindBox(input, edts.Payload, edts.End, "elst", out var elst))
        {
            return (0, 0);
        }

        if (elst.End - elst.Payload < 8)
            return (0, 0);

        var version = ReadUInt32(input, elst.Payload) >> 24;
        var count = ReadUInt32(input, elst.Payload + 4);
        if (version > 1 || count == 0)
            return (0, 0);

        if (count > 1)
            throw new NotSupportedException("AV3A MP4 的多段 edit list 暂不支持。");

        var entrySize = version == 0 ? 12 : 20;
        if (elst.End - elst.Payload - 8 < entrySize)
            throw new InvalidDataException("AV3A MP4 elst 盒长度不完整。");

        Span<byte> entry = stackalloc byte[20];
        input.Position = elst.Payload + 8;
        input.ReadExactly(entry[..entrySize]);

        long segmentDuration;
        long mediaTime;
        if (version == 0)
        {
            segmentDuration = BinaryPrimitives.ReadUInt32BigEndian(entry[..4]);
            mediaTime = BinaryPrimitives.ReadInt32BigEndian(entry.Slice(4, 4));
        }
        else
        {
            segmentDuration = checked((long)BinaryPrimitives.ReadUInt64BigEndian(entry[..8]));
            mediaTime = BinaryPrimitives.ReadInt64BigEndian(entry.Slice(8, 8));
        }

        if (mediaTime < 0)
            throw new NotSupportedException("AV3A MP4 的空白 edit list 暂不支持。");

        long mediaDurationSamples = 0;
        if (segmentDuration > 0 && movieTimescale > 0 && trackTimescale > 0)
        {
            mediaDurationSamples = (long)Math.Round(segmentDuration * (double)trackTimescale / movieTimescale);
        }

        return (mediaTime, mediaDurationSamples);
    }

    private static uint ReadMovieTimescale(Stream input, Box moov)
    {
        if (!TryFindBox(input, moov.Payload, moov.End, "mvhd", out var mvhd) || mvhd.End - mvhd.Payload < 20)
            return 0;

        var version = ReadUInt32(input, mvhd.Payload) >> 24;
        var timescaleOffset = version == 1 ? 20 : 12;
        return mvhd.End - mvhd.Payload >= timescaleOffset + 4
            ? ReadUInt32(input, mvhd.Payload + timescaleOffset)
            : 0;
    }

    private static uint ReadMediaTimescale(Stream input, Box mdia)
    {
        if (!TryFindBox(input, mdia.Payload, mdia.End, "mdhd", out var mdhd) || mdhd.End - mdhd.Payload < 20)
            return 0;

        var version = ReadUInt32(input, mdhd.Payload) >> 24;
        var timescaleOffset = version == 1 ? 20 : 12;
        return mdhd.End - mdhd.Payload >= timescaleOffset + 4
            ? ReadUInt32(input, mdhd.Payload + timescaleOffset)
            : 0;
    }

    private static int ReadTableCount(Stream input, Box box, int entryWidth)
    {
        if (box.End - box.Payload < 8)
            throw new InvalidDataException("MP4 表格长度无效。");

        var count = ReadUInt32(input, box.Payload + 4);
        if (count > (ulong)(box.End - box.Payload - 8) / (uint)entryWidth)
            throw new InvalidDataException("MP4 表格长度无效。");

        return checked((int)count);
    }

    private static Box RequireBox(Stream input, Box parent, string type)
    {
        return TryFindBox(input, parent.Payload, parent.End, type, out var box)
            ? box
            : throw new InvalidDataException($"MP4 缺少 {type}。");
    }

    private static bool TryFindBox(Stream input, long start, long end, string type, out Box found)
    {
        foreach (var box in EnumerateBoxes(input, start, end))
        {
            if (box.Type == type)
            {
                found = box;
                return true;
            }
        }

        found = default;
        return false;
    }

    private static IEnumerable<Box> EnumerateBoxes(Stream input, long start, long end)
    {
        var header = new byte[16];
        for (var offset = start; offset < end;)
        {
            if (end - offset < 8)
                throw new InvalidDataException("MP4 box 头不完整。");

            input.Position = offset;
            input.ReadExactly(header.AsSpan(0, 8));
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8;
            long size = size32;

            if (size32 == 1)
            {
                if (end - offset < 16)
                    throw new InvalidDataException("MP4 扩展 box 头不完整。");

                input.ReadExactly(header.AsSpan(8, 8));
                size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)));
                headerSize = 16;
            }
            else if (size32 == 0)
            {
                size = end - offset;
            }

            if (size < headerSize || size > end - offset)
                throw new InvalidDataException("MP4 box 长度无效。");

            yield return new Box(type, offset + headerSize, offset + size);
            offset += size;
        }
    }

    private static bool TryReadExactly(Stream input, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = input.Read(buffer[total..]);
            if (n == 0)
                return false;

            total += n;
        }

        return true;
    }

    private static uint ReadUInt32(Stream input, long offset)
    {
        Span<byte> bytes = stackalloc byte[4];
        input.Position = offset;
        input.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
