using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SimpleAudio.Codecs.Av3a;

/// <summary>
/// Presents progressively decoded stereo float PCM as a seekable WAV file.
/// The cache retains decoder history exactly, including noise and overlap.
/// Seeking ahead of the worker waits for it; no guessed codec preroll is used.
/// </summary>
internal sealed class Av3aWaveStream : Stream
{
    // BASS checks end-of-file tags before choosing its WAV decoder. A real
    // trailing JUNK chunk lets those probes read known bytes immediately,
    // without forcing the PCM worker to decode the last audio frame first.
    private static readonly byte[] Trailer = CreateTrailer();
    private readonly object _gate = new();
    private readonly object _readGate = new();
    private readonly Stream _input;
    private readonly FileStream _cache;
    private readonly byte[] _header;
    private readonly long _dataLength;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private long _position;
    private long _available;
    private Exception? _error;
    private bool _complete;
    private int _disposed;

    public override bool CanRead => _disposed == 0;
    public override bool CanSeek => _disposed == 0;
    public override bool CanWrite => false;
    public override long Length { get; }
    public override long Position
    {
        get { lock (_readGate) return _position; }
        set => Seek(value, SeekOrigin.Begin);
    }

    public Av3aWaveStream(Stream input, Av3aMp4Track track)
    {
        _input = input;
        var packet = new byte[track.Samples.Max(s => s.Size)];
        var firstSize = track.ReadPacket(input, 0, packet);
        var decoder = new Av3aNativeDecoder(packet.AsSpan(0, firstSize));
        FileStream? cache = null;
        try
        {
            var info = decoder.Info;
            var dataLength = checked(track.Samples.LongLength * info.SamplesPerFrame * 2 * sizeof(float));
            if (dataLength > uint.MaxValue - 36 - Trailer.Length)
                throw new NotSupportedException("AV3A PCM 超过 WAV 的 4 GiB 长度限制。");
            _dataLength = dataLength;
            Length = dataLength + 44 + Trailer.Length;
            _header = CreateHeader(info.SampleRate, (uint)dataLength);
            cache = new FileStream(Path.Combine(Path.GetTempPath(), $"simpleaudio-av3a-{Guid.NewGuid():N}.pcm"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096,
                FileOptions.DeleteOnClose | FileOptions.RandomAccess);
            _cache = cache;
            var downmix = new Av3aStereoDownmix((int)info.Channels, info.ChannelConfig);
            var stereo = new float[info.SamplesPerFrame * 2];
            // Validate/decode the first frame before BASS sees a successful load.
            WriteFrame(decoder, downmix, packet.AsSpan(0, firstSize), stereo);
            _worker = Task.Run(() => DecodeRemaining(track, decoder, downmix, packet, stereo));
        }
        catch
        {
            decoder.Dispose();
            cache?.Dispose();
            _cancellation.Dispose();
            throw;
        }
    }

    private void DecodeRemaining(Av3aMp4Track track, Av3aNativeDecoder decoder,
        Av3aStereoDownmix downmix, byte[] packet, float[] stereo)
    {
        try
        {
            for (var index = 1; index < track.Samples.Length; index++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                var size = track.ReadPacket(_input, index, packet);
                WriteFrame(decoder, downmix, packet.AsSpan(0, size), stereo);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_cancellation.IsCancellationRequested)
                    _error = ex;
            }
        }
        finally
        {
            decoder.Dispose();
            lock (_gate)
            {
                _complete = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void WriteFrame(Av3aNativeDecoder decoder, Av3aStereoDownmix downmix,
        ReadOnlySpan<byte> packet, Span<float> stereo)
    {
        downmix.Convert(decoder.Decode(packet), stereo);
        var bytes = MemoryMarshal.AsBytes(stereo);
        RandomAccess.Write(_cache.SafeFileHandle, bytes, _available);
        lock (_gate)
        {
            _available += bytes.Length;
            Monitor.PulseAll(_gate);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        lock (_readGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var total = 0;
            if (_position < _header.Length)
            {
                var count = (int)Math.Min(buffer.Length, _header.Length - _position);
                _header.AsSpan((int)_position, count).CopyTo(buffer);
                _position += count;
                total += count;
                buffer = buffer[count..];
            }
            while (!buffer.IsEmpty && _position < Length)
            {
                var pcmOffset = _position - _header.Length;
                if (pcmOffset >= _dataLength)
                {
                    var trailerOffset = (int)(pcmOffset - _dataLength);
                    var trailerCount = Math.Min(buffer.Length, Trailer.Length - trailerOffset);
                    Trailer.AsSpan(trailerOffset, trailerCount).CopyTo(buffer);
                    _position += trailerCount;
                    total += trailerCount;
                    buffer = buffer[trailerCount..];
                    continue;
                }
                int count;
                lock (_gate)
                {
                    while (_available <= pcmOffset && !_complete && _disposed == 0)
                        Monitor.Wait(_gate);
                    ObjectDisposedException.ThrowIf(_disposed != 0, this);
                    if (_available <= pcmOffset)
                        throw new IOException("AV3A 后台解码未能生成完整 PCM。", _error);
                    count = (int)Math.Min(buffer.Length, _available - pcmOffset);
                }
                var read = RandomAccess.Read(_cache.SafeFileHandle, buffer[..count], pcmOffset);
                if (read == 0)
                    throw new EndOfStreamException("AV3A PCM 缓存读取不完整。");
                _position += read;
                total += read;
                buffer = buffer[read..];
            }
            return total;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_readGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (position < 0 || position > Length)
                throw new IOException("AV3A PCM 定位超出文件范围。");
            return _position = position;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            CancelPendingRead();
        }
        finally
        {
            _worker.GetAwaiter().GetResult();
            lock (_readGate)
                _cache.Dispose();
            _cancellation.Dispose();
        }
        base.Dispose(disposing);
    }

    public void CancelPendingRead()
    {
        _cancellation.Cancel();
        lock (_gate)
            Monitor.PulseAll(_gate);
        // Interrupt a network read before waiting for the BASS read callback.
        _input.Dispose();
    }

    private static byte[] CreateHeader(uint rate, uint dataLength)
    {
        var bytes = new byte[44];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), dataLength + 36 + (uint)Trailer.Length);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 3); // IEEE float
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), rate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), rate * 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 32);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), dataLength);
        return bytes;
    }

    private static byte[] CreateTrailer()
    {
        var bytes = new byte[8 + 4096];
        "JUNK"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 4096);
        return bytes;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
