using System.Runtime.InteropServices;

namespace SimpleAudio.Codecs.Av3a;

// 按需解码的 AV3A 虚拟 WAV 流：零临时 PCM 落盘、零后台解码线程、支持毫秒级 Preroll 随机 Seek 与多声道自适配输出。
internal sealed class Av3aPcmWaveStream : Stream, IAbortableStreamSource
{
    // MDCT overlap-add 与 BWE/预测器收敛所需的默认回退预卷帧数（实测 2 帧即可完全收敛，与 4/8 帧 SNR 一致）。
    internal const int DefaultPrerollFrames = 2;

    private readonly object _gate = new();
    private readonly Stream _input;
    private readonly Av3aMp4Track _track;
    private readonly Av3aNativeDecoder _decoder;
    private readonly IAudioChannelRenderer _renderer;
    private readonly byte[] _header;
    private readonly byte[] _firstPacket;
    private readonly byte[] _packetBuffer;
    private readonly float[] _outputFrameSamples;
    private readonly int _samplesPerFrame;
    private readonly int _outputChannels;
    private readonly int _outputFrameBytes;
    private readonly long _trimStartSamples;
    private readonly long _pcmDataLength;
    private readonly int _prerollFrames;

    private long _position;
    private int _cachedFrameIndex = -1;
    private int _nextDecoderFrameIndex;
    private int _aborted;
    private int _disposed;

    public AudioChannelLayout SourceLayout { get; }
    public AudioChannelLayout TargetLayout { get; }

    public Av3aPcmWaveStream(
        Stream input,
        Av3aMp4Track track,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels,
        int prerollFrames = DefaultPrerollFrames)
    {
        _input = input;
        _track = track;
        _prerollFrames = Math.Max(0, prerollFrames);

        var maxPacketSize = 0;
        foreach (var sample in track.Samples)
        {
            if (sample.Size > maxPacketSize)
                maxPacketSize = sample.Size;
        }

        _packetBuffer = new byte[maxPacketSize];
        var firstSize = track.ReadPacket(input, 0, _packetBuffer);
        _firstPacket = _packetBuffer.AsSpan(0, firstSize).ToArray();

        _decoder = new Av3aNativeDecoder(_firstPacket);
        try
        {
            var info = _decoder.Info;
            _samplesPerFrame = checked((int)info.SamplesPerFrame);
            if (track.SamplesPerFrame > 0 && track.SamplesPerFrame != _samplesPerFrame)
                throw new InvalidDataException($"AV3A MP4 stts 帧长({track.SamplesPerFrame})与解码器帧长({_samplesPerFrame})不一致。");

            SourceLayout = AudioChannelLayout.FromAv3aConfig((int)info.Channels, info.ChannelConfig);
            TargetLayout = AudioChannelLayout.ResolveTargetLayout(
                SourceLayout,
                deviceSpeakers,
                channelMode,
                maxSupportedChannels);

            _renderer = ChannelMixMatrix.CreateRenderer(SourceLayout, TargetLayout, (int)info.SampleRate);
            _outputChannels = TargetLayout.Channels;
            _outputFrameBytes = _outputChannels * sizeof(float);
            _outputFrameSamples = new float[checked(_samplesPerFrame * _outputChannels)];

            var totalRawSamples = checked(track.Samples.LongLength * _samplesPerFrame);
            _trimStartSamples = Math.Clamp(track.TrimStartSamples, 0, totalRawSamples);
            var availableSamples = totalRawSamples - _trimStartSamples;
            var effectiveSamples = track.MediaDurationSamples > 0
                ? Math.Min(track.MediaDurationSamples, availableSamples)
                : availableSamples;

            _pcmDataLength = checked(effectiveSamples * _outputFrameBytes);
            _header = WaveHeaderBuilder.CreateFloatWaveHeader((int)info.SampleRate, TargetLayout, _pcmDataLength);
            Length = _header.Length + _pcmDataLength;

            // 初始化阶段预解第 0 帧，确保损坏/CRC 失败的 AV3A 流在创建阶段即被拒绝。
            DecodeFrameIntoCache(0);
        }
        catch
        {
            _decoder.Dispose();
            throw;
        }
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

        if (_input is IAbortableStreamSource abortable)
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
                var virtualSample = pcmOffset / _outputFrameBytes;
                var byteInSample = (int)(pcmOffset % _outputFrameBytes);
                var rawSample = _trimStartSamples + virtualSample;
                var frameIndex = (int)(rawSample / _samplesPerFrame);
                var sampleInFrame = (int)(rawSample % _samplesPerFrame);

                if (frameIndex >= _track.Samples.Length)
                    break;

                EnsureFrameDecoded(frameIndex);

                var frameByteOffset = sampleInFrame * _outputFrameBytes + byteInSample;
                var frameBytes = MemoryMarshal.AsBytes(_outputFrameSamples.AsSpan());
                var availableInFrame = frameBytes.Length - frameByteOffset;
                var remainingInFile = Length - _position;
                var toCopy = (int)Math.Min(buffer.Length, Math.Min(availableInFrame, remainingInFile));
                if (toCopy <= 0)
                    break;

                frameBytes.Slice(frameByteOffset, toCopy).CopyTo(buffer);
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
                throw new IOException("AV3A 音频流定位超出范围。");

            _position = target;
            return target;
        }
    }

    // 顺序读取时零开销步进；非顺序随机 Seek 时重置解码器并按需执行 Preroll 预卷收敛。
    private void EnsureFrameDecoded(int targetFrameIndex)
    {
        if (_cachedFrameIndex == targetFrameIndex)
            return;

        if (targetFrameIndex == _nextDecoderFrameIndex)
        {
            DecodeFrameIntoCache(targetFrameIndex);
            return;
        }

        // 若向前小幅跳跃（<= Preroll + 2 帧），直接从当前状态继续解码比重置更省开销且绝对无断层。
        if (targetFrameIndex > _nextDecoderFrameIndex &&
            targetFrameIndex - _nextDecoderFrameIndex <= _prerollFrames + 2)
        {
            for (var i = _nextDecoderFrameIndex; i < targetFrameIndex; i++)
            {
                DecodeFrameSilently(i);
            }

            DecodeFrameIntoCache(targetFrameIndex);
            return;
        }

        // 随机跳转（向前大跳或向后跳）：重置原生解码器与空间渲染器延迟线，回退 PrerollFrames 帧预热内部状态后输出目标帧。
        var startFrame = Math.Max(0, targetFrameIndex - _prerollFrames);
        var seedSize = _track.ReadPacket(_input, startFrame, _packetBuffer);
        _decoder.Reset(_packetBuffer.AsSpan(0, seedSize));
        _renderer.Reset();
        _cachedFrameIndex = -1;
        _nextDecoderFrameIndex = startFrame;

        for (var i = startFrame; i < targetFrameIndex; i++)
        {
            DecodeFrameSilently(i);
        }

        DecodeFrameIntoCache(targetFrameIndex);
    }

    private void DecodeFrameSilently(int frameIndex)
    {
        var size = _track.ReadPacket(_input, frameIndex, _packetBuffer);
        _ = _decoder.Decode(_packetBuffer.AsSpan(0, size));
        _nextDecoderFrameIndex = frameIndex + 1;
    }

    private void DecodeFrameIntoCache(int frameIndex)
    {
        var size = _track.ReadPacket(_input, frameIndex, _packetBuffer);
        var pcm = _decoder.Decode(_packetBuffer.AsSpan(0, size));
        _renderer.RenderInt16(pcm, _outputFrameSamples);
        _cachedFrameIndex = frameIndex;
        _nextDecoderFrameIndex = frameIndex + 1;
    }

    protected override void Dispose(bool disposing)
    {
        Abort();
        if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            _decoder.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
