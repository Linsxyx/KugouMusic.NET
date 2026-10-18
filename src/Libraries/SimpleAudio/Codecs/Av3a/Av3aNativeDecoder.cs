using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SimpleAudio.Codecs.Av3a;

internal sealed unsafe partial class Av3aNativeDecoder : IDisposable
{
    private readonly DecoderHandle _handle;
    private readonly short[] _pcm;

    public Av3aStreamInfo Info { get; }

    public Av3aNativeDecoder(ReadOnlySpan<byte> firstFrame)
    {
        fixed (byte* frame = firstFrame)
        {
            var result = Create(frame, (nuint)firstFrame.Length, out var info, out var handle);
            if (result != 0 || handle == 0)
                throw new InvalidDataException($"AV3A 解码器初始化失败，code={result}。");
            _handle = new DecoderHandle(handle);
            Info = info;
        }

        if (Info.SampleRate is not (44100 or 48000) || Info.Channels is < 1 or > 12 || Info.SamplesPerFrame != 1024)
        {
            _handle.Dispose();
            throw new InvalidDataException("AV3A 解码器返回了不支持的 PCM 格式。");
        }

        _pcm = new short[checked((int)(Info.Channels * Info.SamplesPerFrame))];
    }

    public ReadOnlySpan<short> Decode(ReadOnlySpan<byte> packet)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        fixed (byte* frame = packet)
        fixed (short* pcm = _pcm)
        {
            var result = DecodeFrame(_handle, frame, (nuint)packet.Length, pcm, (nuint)_pcm.Length, out var written);
            if (result != 0 || written != (nuint)_pcm.Length)
                throw new InvalidDataException($"AV3A 帧解码或 CRC 校验失败，code={result}。");
        }
        return _pcm;
    }

    public void Dispose() => _handle.Dispose();

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
