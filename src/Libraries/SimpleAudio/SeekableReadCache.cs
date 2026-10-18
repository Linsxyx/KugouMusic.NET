namespace SimpleAudio;

/// <summary>A read-through disk cache for probing and seeking a nonseekable HTTP body.</summary>
internal sealed class SeekableReadCache : Stream
{
    private readonly Stream _input;
    private readonly FileStream _cache;
    private readonly long _expectedLength;
    private readonly byte[] _copyBuffer = new byte[64 * 1024];
    private long _cached;
    private long _position;
    private bool _end;
    private int _disposed;

    public SeekableReadCache(Stream input, long expectedLength)
    {
        _input = input;
        _expectedLength = Math.Max(0, expectedLength);
        _cache = new FileStream(Path.Combine(Path.GetTempPath(), $"simpleaudio-input-{Guid.NewGuid():N}.cache"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096,
            FileOptions.RandomAccess | FileOptions.DeleteOnClose);
    }

    public override bool CanRead => _disposed == 0;
    public override bool CanSeek => _disposed == 0;
    public override bool CanWrite => false;
    public long KnownLength => _expectedLength > 0 ? _expectedLength : _end ? _cached : 0;
    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_expectedLength > 0)
                return _expectedLength;
            Ensure(long.MaxValue);
            return _cached;
        }
    }
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    private void Ensure(long end)
    {
        while (!_end && _cached < end)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var wanted = (int)Math.Min(_copyBuffer.Length, end - _cached);
            var read = _input.Read(_copyBuffer, 0, wanted);
            if (read == 0)
            {
                _end = true;
                if (_expectedLength > 0 && _cached != _expectedLength)
                    throw new EndOfStreamException("音频流长度与 Content-Length 不一致。");
                break;
            }
            RandomAccess.Write(_cache.SafeFileHandle, _copyBuffer.AsSpan(0, read), _cached);
            _cached += read;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (buffer.IsEmpty)
            return 0;
        Ensure(checked(_position + buffer.Length));
        var count = (int)Math.Min(buffer.Length, Math.Max(0, _cached - _position));
        if (count == 0)
            return 0;
        var read = RandomAccess.Read(_cache.SafeFileHandle, buffer[..count], _position);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0)
            throw new IOException("音频流定位不能为负数。");
        return _position = position;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try { _input.Dispose(); }
            finally { _cache.Dispose(); }
        }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
