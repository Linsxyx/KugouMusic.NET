using System.Buffers.Binary;

namespace SimpleAudio.Codecs.Av3a;

internal sealed class Av3aMp4Track
{
    internal readonly record struct Sample(long Offset, int Size);
    private readonly record struct Box(string Type, long Payload, long End);
    public required Sample[] Samples { get; init; }

    public static Av3aMp4Track? TryRead(Stream input)
    {
        var originalPosition = input.Position;
        var isAv3a = false;
        try
        {
            Span<byte> signature = stackalloc byte[8];
            input.Position = 0;
            var read = 0;
            while (read < signature.Length)
            {
                var n = input.Read(signature[read..]);
                if (n == 0)
                    return null;
                read += n;
            }
            if (!signature[4..].SequenceEqual("ftyp"u8))
                return null;

            var length = input.Length;
            var moov = Boxes(input, 0, length).FirstOrDefault(b => b.Type == "moov");
            if (moov == default)
                return null;
            foreach (var trak in Boxes(input, moov.Payload, moov.End).Where(b => b.Type == "trak"))
            {
                var mdia = Child(input, trak, "mdia");
                var minf = Child(input, mdia, "minf");
                var stbl = Child(input, minf, "stbl");
                var stsd = Child(input, stbl, "stsd");
                var descriptions = Boxes(input, stsd.Payload + 8, stsd.End).ToArray();
                if (!descriptions.Any(b => b.Type == "av3a"))
                    continue;
                isAv3a = true;
                if (descriptions.Length != 1 || U32(input, stsd.Payload + 4) != 1)
                    throw new NotSupportedException("AV3A 多采样描述或加密 MP4 暂不支持。");

                ValidateEdits(input, trak);
                var stts = Child(input, stbl, "stts");
                var timingCount = TableCount(input, stts, 8);
                ulong timingSamples = 0;
                for (var i = 0; i < timingCount; i++)
                {
                    timingSamples += U32(input, stts.Payload + 8 + i * 8L);
                    if (U32(input, stts.Payload + 12 + i * 8L) != 1024)
                        throw new NotSupportedException("AV3A MP4 帧时长不是 1024 个采样。");
                }
                var stsz = Child(input, stbl, "stsz");
                var fixedSize = U32(input, stsz.Payload + 4);
                var count = U32(input, stsz.Payload + 8);
                if (count == 0 || count > 10_000_000 || count > (ulong)length || timingSamples != count)
                    throw new InvalidDataException("AV3A MP4 采样数量无效。");
                if (fixedSize == 0 && count > (ulong)Math.Max(0, stsz.End - stsz.Payload - 12) / 4)
                    throw new InvalidDataException("AV3A MP4 采样尺寸表不完整。");

                var stsc = Child(input, stbl, "stsc");
                var mappingCount = TableCount(input, stsc, 12);
                if (mappingCount == 0)
                    throw new InvalidDataException("AV3A MP4 分块表为空。");
                var mappings = new (uint First, uint Count)[mappingCount];
                for (var i = 0; i < mappingCount; i++)
                {
                    var offset = stsc.Payload + 8 + i * 12L;
                    mappings[i] = (U32(input, offset), U32(input, offset + 4));
                    if (mappings[i].Count == 0 || U32(input, offset + 8) != 1 ||
                        (i == 0 ? mappings[i].First != 1 : mappings[i].First <= mappings[i - 1].First))
                        throw new InvalidDataException("AV3A MP4 分块映射无效。");
                }

                var children = Boxes(input, stbl.Payload, stbl.End).ToArray();
                var offsets = children.FirstOrDefault(b => b.Type == "stco");
                var wide = offsets == default;
                if (wide)
                    offsets = children.FirstOrDefault(b => b.Type == "co64");
                if (offsets == default)
                    throw new InvalidDataException("AV3A MP4 缺少分块偏移表。");
                var width = wide ? 8 : 4;
                var chunks = TableCount(input, offsets, width);
                if (mappings[^1].First > chunks)
                    throw new InvalidDataException("AV3A MP4 分块映射超出偏移表。");
                var samples = new Sample[count];
                int sampleIndex = 0, mapping = 0;
                for (var chunk = 1; chunk <= chunks; chunk++)
                {
                    while (mapping + 1 < mappings.Length && mappings[mapping + 1].First <= chunk)
                        mapping++;
                    if (mappings[mapping].Count > count - sampleIndex)
                        throw new InvalidDataException("AV3A MP4 分块超出采样表。");
                    var offset = wide
                        ? checked((long)U64(input, offsets.Payload + 8 + (chunk - 1L) * width))
                        : U32(input, offsets.Payload + 8 + (chunk - 1L) * width);
                    for (var j = 0; j < mappings[mapping].Count; j++)
                    {
                        var size = fixedSize > 0 ? fixedSize : U32(input, stsz.Payload + 12 + sampleIndex * 4L);
                        if (size < 9 || size > 1_048_576 || offset < 0 || offset > length - size)
                            throw new InvalidDataException("AV3A MP4 采样超出文件范围。");
                        samples[sampleIndex++] = new Sample(offset, (int)size);
                        offset += size;
                    }
                }
                if (sampleIndex != samples.Length)
                    throw new InvalidDataException("AV3A MP4 采样数量与分块不一致。");
                return new Av3aMp4Track { Samples = samples };
            }
            return null;
        }
        catch (Exception ex) when (!isAv3a && ex is IOException or InvalidDataException or NotSupportedException or OverflowException)
        {
            // An unfamiliar container must still be offered to BASS normally.
            return null;
        }
        finally
        {
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

    private static void ValidateEdits(Stream input, Box trak)
    {
        var edts = Boxes(input, trak.Payload, trak.End).FirstOrDefault(b => b.Type == "edts");
        if (edts == default)
            return;
        var elst = Child(input, edts, "elst");
        var version = U32(input, elst.Payload) >> 24;
        var count = U32(input, elst.Payload + 4);
        var mediaTime = version == 0 ? U32(input, elst.Payload + 12) : U64(input, elst.Payload + 16);
        if (version > 1 || count != 1 || mediaTime != 0)
            throw new NotSupportedException("AV3A MP4 的裁剪或空白 edit list 暂不支持。");
    }

    private static int TableCount(Stream input, Box box, int width)
    {
        var count = U32(input, box.Payload + 4);
        if (box.End - box.Payload < 8 || count > (ulong)(box.End - box.Payload - 8) / (uint)width)
            throw new InvalidDataException("MP4 表格长度无效。");
        return checked((int)count);
    }

    private static Box Child(Stream input, Box parent, string type) =>
        Boxes(input, parent.Payload, parent.End).FirstOrDefault(b => b.Type == type) is var box && box != default
            ? box : throw new InvalidDataException($"MP4 缺少 {type}。");

    private static IEnumerable<Box> Boxes(Stream input, long start, long end)
    {
        var header = new byte[16];
        for (var offset = start; offset < end;)
        {
            if (end - offset < 8)
                throw new InvalidDataException("MP4 box 头不完整。");
            input.Position = offset;
            input.ReadExactly(header.AsSpan(0, 8));
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
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
                size = end - offset;
            if (size < headerSize || size > end - offset)
                throw new InvalidDataException("MP4 box 长度无效。");
            yield return new Box(type, offset + headerSize, offset + size);
            offset += size;
        }
    }

    private static uint U32(Stream input, long offset)
    {
        Span<byte> bytes = stackalloc byte[4];
        input.Position = offset;
        input.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static ulong U64(Stream input, long offset)
    {
        Span<byte> bytes = stackalloc byte[8];
        input.Position = offset;
        input.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }
}
