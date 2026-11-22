using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SimpleAudio.Codecs.Av3a;

// AV3A 原生 Rust 解码库 P/Invoke 封装，支持单帧解码与无临时文件的状态重置（用于随机 Seek）。
internal sealed unsafe partial class Av3aNativeDecoder : IDisposable
{
    private DecoderHandle _handle;
    private readonly short[] _pcm;
    private bool _disposed;

    public Av3aStreamInfo Info { get; }

    public Av3aNativeDecoder(ReadOnlySpan<byte> firstFrame)
    {
        (_handle, Info) = CreateHandle(firstFrame);

        if (Info.SampleRate is not (44100 or 48000) || Info.Channels is < 1 or > 12 || Info.SamplesPerFrame == 0)
        {
            _handle.Dispose();
            throw new InvalidDataException($"AV3A 解码器返回了不支持的 PCM 格式（rate={Info.SampleRate}, ch={Info.Channels}, spf={Info.SamplesPerFrame}）。");
        }

        _pcm = new short[checked((int)(Info.Channels * Info.SamplesPerFrame))];
    }

    public ReadOnlySpan<short> Decode(ReadOnlySpan<byte> packet)
    {
        ObjectDisposedException.ThrowIf(_disposed || _handle.IsClosed, this);
        fixed (byte* frame = packet)
        fixed (short* pcm = _pcm)
        {
            var result = DecodeFrame(_handle, frame, (nuint)packet.Length, pcm, (nuint)_pcm.Length, out var written);
            if (result != 0 || written != (nuint)_pcm.Length)
                throw new InvalidDataException($"AV3A 帧解码或 CRC 校验失败，code={result}。");
        }

        return _pcm;
    }

    // 重置解码器内部 MDCT/BWE/噪声填充状态，以便随机 Seek 后从 Preroll 帧干净收敛。
    public void Reset(ReadOnlySpan<byte> seedFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (nextHandle, nextInfo) = CreateHandle(seedFrame);
        if (nextInfo.SampleRate != Info.SampleRate ||
            nextInfo.Channels != Info.Channels ||
            nextInfo.SamplesPerFrame != Info.SamplesPerFrame)
        {
            nextHandle.Dispose();
            throw new InvalidDataException("AV3A 解码器重置后返回了不一致的流参数。");
        }

        var old = _handle;
        _handle = nextHandle;
        old.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _handle.Dispose();
    }

    private static (DecoderHandle Handle, Av3aStreamInfo Info) CreateHandle(ReadOnlySpan<byte> frameData)
    {
        fixed (byte* frame = frameData)
        {
            var result = Create(frame, (nuint)frameData.Length, out var info, out var rawHandle);
            if (result != 0 || rawHandle == 0)
                throw new InvalidDataException($"AV3A 解码器初始化失败，code={result}。");

            return (new DecoderHandle(rawHandle), info);
        }
    }

    [LibraryImport("av3a_decoder", EntryPoint = "av3a_rs_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int Create(byte* frame, nuint length, out Av3aStreamInfo info, out nint handle);

    [LibraryImport("av3a_decoder", EntryPoint = "av3a_rs_decode_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int DecodeFrame(DecoderHandle handle, byte* frame, nuint length, short* pcm,
        nuint capacity, out nuint written);

    [LibraryImport("av3a_decoder", EntryPoint = "av3a_rs_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void Destroy(nint handle);

    private sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DecoderHandle(nint value) : base(true) => SetHandle(value);

        protected override bool ReleaseHandle()
        {
            Destroy(handle);
            return true;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Av3aStreamInfo
{
    public uint SampleRate;
    public uint Channels;
    public uint SamplesPerFrame;
    public uint ChannelConfig;
}
