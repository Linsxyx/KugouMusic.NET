using Xunit;

namespace SimpleAudio.Tests;

public class SeekableReadCacheTests
{
    [Fact]
    public void FragmentedNonseekableInput_CanBeReplayedAndReadToEnd()
    {
        var original = Enumerable.Range(0, 250).Select(i => (byte)i).ToArray();
        using var cache = new SeekableReadCache(new FragmentedStream(original), 0);
        Assert.Equal(0, cache.KnownLength);
        var first = new byte[8];
        cache.ReadExactly(first);
        Assert.Equal(original[..8], first);
        cache.Position = 0;
        var replay = new byte[8];
        cache.ReadExactly(replay);
        Assert.Equal(first, replay);
        Assert.Equal(original.Length, cache.Length);
        cache.Seek(-8, SeekOrigin.End);
        cache.ReadExactly(replay);
        Assert.Equal(original[^8..], replay);
    }

    [Fact]
    public void ShortHttpBody_ThrowsInsteadOfReturningFalseEndOfFile()
    {
        using var cache = new SeekableReadCache(new FragmentedStream(new byte[10]), 20);
        Assert.Throws<EndOfStreamException>(() => cache.ReadExactly(new byte[20]));
    }

    internal sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(3, count));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(3, buffer.Length)]);
    }
}
