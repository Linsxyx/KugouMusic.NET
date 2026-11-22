using System.Buffers;
using System.Runtime.InteropServices;
using ManagedBass;

namespace SimpleAudio;

// 将任意多声道 BASS Decode 流按需实时混音为目标设备声道拓扑的可 Seek 虚拟 WAV 流（零临时文件、零后台线程）。
internal sealed class BassChannelAdapterStream : Stream, IAbortableStreamSource
{
    private const int ChunkFrames = 2048;

    private readonly object _gate = new();
    private readonly int _rawHandle;
    private readonly IDisposable? _rawOwner;
    private readonly ChannelMixMatrix _matrix;
    private readonly byte[] _header;
    private readonly long _pcmDataLength;
    private readonly int _sourceFrameBytes;
    private readonly int _targetFrameBytes;
    private readonly float[] _sourceBuffer;
    private readonly float[] _targetBuffer;

    private long _position;
    private long _decodedPcmPosition;
    private int _bufferedBytes;
    private int _bufferedOffset;
    private int _aborted;
    private int _disposed;

    public BassChannelAdapterStream(
        int rawHandle,
        IDisposable? rawOwner,
        int sampleRate,
        AudioChannelLayout sourceLayout,
        AudioChannelLayout targetLayout,
        long rawByteLength)
    {
        _rawHandle = rawHandle;
        _rawOwner = rawOwner;
        _matrix = ChannelMixMatrix.Build(sourceLayout, targetLayout);
        _sourceFrameBytes = sourceLayout.Channels * sizeof(float);
        _targetFrameBytes = targetLayout.Channels * sizeof(float);

        var totalFrames = rawByteLength > 0 ? rawByteLength / _sourceFrameBytes : 0;
        _pcmDataLength = checked(totalFrames * _targetFrameBytes);
        _header = WaveHeaderBuilder.CreateFloatWaveHeader(sampleRate, targetLayout, _pcmDataLength);
        Length = _header.Length + _pcmDataLength;

        _sourceBuffer = ArrayPool<float>.Shared.Rent(ChunkFrames * sourceLayout.Channels);
        _targetBuffer = ArrayPool<float>.Shared.Rent(ChunkFrames * targetLayout.Channels);
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;
    public override bool CanSeek => Volatile.Read(ref _disposed) == 0;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get { lock (_gate) return _position; }
        set => Seek(value, SeekOrigin.Begin);
    }

    public void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0)
            return;

        if (_rawOwner is IAbortableStreamSource abortable)
        {
            abortable.Abort();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0 || buffer.IsEmpty)
                return 0;

            var totalRead = 0;

            if (_position < _header.Length)
            {
                var headerCount = (int)Math.Min(buffer.Length, _header.Length - _position);
                _header.AsSpan((int)_position, headerCount).CopyTo(buffer);
                _position += headerCount;
                totalRead += headerCount;
                buffer = buffer[headerCount..];
            }

            while (!buffer.IsEmpty && _position < Length)
            {
                if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0)
                    break;

                var pcmOffset = _position - _header.Length;
                if (pcmOffset != _decodedPcmPosition + _bufferedOffset)
                {
                    if (!SeekRawToPcmOffset(pcmOffset))
                        break;
                }

                if (_bufferedOffset >= _bufferedBytes)
                {
                    if (!FillNextChunk())
                        break;
                }

                var available = _bufferedBytes - _bufferedOffset;
                var remainingInFile = Length - _position;
                var toCopy = (int)Math.Min(buffer.Length, Math.Min(available, remainingInFile));
                if (toCopy <= 0)
                    break;

                MemoryMarshal.AsBytes(_targetBuffer.AsSpan())
                    .Slice(_bufferedOffset, toCopy)
                    .CopyTo(buffer);

                _bufferedOffset += toCopy;
                _position += toCopy;
                totalRead += toCopy;
                buffer = buffer[toCopy..];
            }

            return totalRead;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0 || target > Length)
                throw new IOException("声道适配流定位超出范围。");

            _position = target;
            return target;
        }
    }

    // 将目标 PCM 字节偏移映射到原始 BASS 解码流帧位置并精准定位。
    private bool SeekRawToPcmOffset(long pcmOffset)
    {
        var frameIndex = pcmOffset / _targetFrameBytes;
        var inFrameOffset = (int)(pcmOffset % _targetFrameBytes);
        var rawBytePos = checked(frameIndex * _sourceFrameBytes);

        _bufferedBytes = 0;
        _bufferedOffset = 0;

        if (!Bass.ChannelSetPosition(_rawHandle, rawBytePos, PositionFlags.Bytes))
        {
            return false;
        }

        _decodedPcmPosition = frameIndex * _targetFrameBytes;
        if (inFrameOffset > 0)
        {
            if (!FillNextChunk() || inFrameOffset > _bufferedBytes)
                return false;

            _bufferedOffset = inFrameOffset;
        }

        return true;
    }

    private bool FillNextChunk()
    {
        _decodedPcmPosition += _bufferedBytes;
        _bufferedBytes = 0;
        _bufferedOffset = 0;

        var requestBytes = ChunkFrames * _sourceFrameBytes;
        var readBytes = Bass.ChannelGetData(_rawHandle, _sourceBuffer, requestBytes | (int)DataFlags.Float);
        if (readBytes <= 0)
        {
            return false;
        }

        var framesRead = readBytes / _sourceFrameBytes;
        if (framesRead <= 0)
        {
            return false;
        }

        var srcSamples = framesRead * _matrix.SourceChannels;
        var dstSamples = framesRead * _matrix.TargetChannels;
        _matrix.ConvertFloat(_sourceBuffer.AsSpan(0, srcSamples), _targetBuffer.AsSpan(0, dstSamples));
        _bufferedBytes = dstSamples * sizeof(float);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Abort();
        if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            if (_rawHandle != 0)
            {
                Bass.StreamFree(_rawHandle);
            }

            _rawOwner?.Dispose();
            ArrayPool<float>.Shared.Return(_sourceBuffer);
            ArrayPool<float>.Shared.Return(_targetBuffer);
        }

        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
