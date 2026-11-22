using ManagedBass;
using SimpleAudio.Codecs.Av3a;

namespace SimpleAudio.Codecs;

// 统一的 BASS 源流打开结果，封装原生句柄、托管资源所有权、声道适配元信息与明确错误原因。
internal sealed class OpenedBassStream : IDisposable
{
    public int Handle { get; private set; }
    public IDisposable? Owner { get; private set; }
    public AudioChannelLayout? SourceLayout { get; init; }
    public AudioChannelLayout? TargetLayout { get; init; }
    public int SourceChannels => SourceLayout?.Channels ?? 0;
    public int OutputChannels => TargetLayout?.Channels ?? 0;
    public string SourceLayoutName => SourceLayout?.Name ?? string.Empty;
    public string OutputLayoutName => TargetLayout?.Name ?? string.Empty;
    public string? Error { get; init; }
    public bool IsSuccess => Handle != 0;

    public static OpenedBassStream Failure(string? error) => new() { Error = error };

    public static OpenedBassStream Success(
        int handle,
        IDisposable? owner,
        AudioChannelLayout sourceLayout,
        AudioChannelLayout targetLayout) => new()
    {
        Handle = handle,
        Owner = owner,
        SourceLayout = sourceLayout,
        TargetLayout = targetLayout
    };

    // 将句柄与资源所有权移交给 SimpleAudioPlayer 等调用方，防止后续 Dispose 误释放。
    public (int Handle, IDisposable? Owner) Detach()
    {
        var handle = Handle;
        var owner = Owner;
        Handle = 0;
        Owner = null;
        return (handle, owner);
    }

    public void Dispose()
    {
        var handle = Handle;
        var owner = Owner;
        Handle = 0;
        Owner = null;

        if (owner is IAbortableStreamSource abortable)
        {
            abortable.Abort();
        }

        if (handle != 0)
        {
            Bass.StreamFree(handle);
        }

        owner?.Dispose();
    }
}

// 统一音频流打开管线：负责魔数预判自定义编解码器（如 AV3A）、复用 RemoteAudioCache，并对所有格式执行统一多声道自适配。
internal static class BassStreamOpener
{
    private const int RemoteStreamLoadAttempts = 3;
    private const int RemoteStreamRetryDelayMilliseconds = 300;

    // BASS_FX TempoCreate 等下游管线安全支持的最大多声道数（7.1 = 8 声道）。
    internal const int DefaultMaxSupportedChannels = 8;

    private static readonly IBassCustomCodec[] CustomCodecs = [new Av3aCodec()];

    // 打开本地文件路径或直连 URL，并按设备扬声器能力执行声道自适配。
    public static OpenedBassStream OpenPathOrUrl(
        string source,
        BassFlags sourceFlags,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels = DefaultMaxSupportedChannels)
    {
        if (RemoteAudioCache.TryGetCachedFilePath(source, out var cachedPath))
        {
            var cached = OpenLocalFile(cachedPath, sourceFlags, deviceSpeakers, channelMode, maxSupportedChannels);
            if (cached.IsSuccess || !string.IsNullOrWhiteSpace(cached.Error))
            {
                return cached;
            }
        }

        if (File.Exists(source))
        {
            return OpenLocalFile(source, sourceFlags, deviceSpeakers, channelMode, maxSupportedChannels);
        }

        if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var remoteFlags = RemoteAudioCache.IsKnownLiveStream(source)
                ? sourceFlags | BassFlags.StreamDownloadBlocks
                : sourceFlags;

            for (var attempt = 1; attempt <= RemoteStreamLoadAttempts; attempt++)
            {
                var handle = Bass.CreateStream(source, 0, remoteFlags, null, IntPtr.Zero);
                if (handle != 0)
                {
                    return AdaptRawHandleIfNeeded(
                        handle,
                        rawOwner: null,
                        sourceFlags,
                        deviceSpeakers,
                        channelMode,
                        maxSupportedChannels);
                }

                if (attempt < RemoteStreamLoadAttempts)
                {
                    Thread.Sleep(RemoteStreamRetryDelayMilliseconds * attempt);
                }
            }

            return OpenedBassStream.Failure($"BASS 远程流打开失败: {Bass.LastError}");
        }

        return OpenedBassStream.Failure($"音频源不存在: {source}");
    }

    // 打开托管流（含 ProgressiveRemoteAudioStream 渐进流、实时流及本地自定义流），支持自定义编解码器探测与多声道自适配。
    public static OpenedBassStream OpenStream(
        Stream inputStream,
        long contentLength,
        BassFlags sourceFlags,
        IDisposable? owner,
        bool useUnbuffered,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels = DefaultMaxSupportedChannels)
    {
        var externalOwner = ReferenceEquals(owner, inputStream) ? null : owner;

        // 1. 仅对可寻址流（如 ProgressiveRemoteAudioStream / FileStream）执行廉价 12B 魔数预检，避免干扰实时直播流。
        if (inputStream.CanSeek)
        {
            var custom = TryOpenWithCustomCodecs(
                inputStream,
                externalOwner,
                sourceFlags,
                deviceSpeakers,
                channelMode,
                maxSupportedChannels,
                out var codecError);

            if (custom != null)
            {
                return custom;
            }

            if (!string.IsNullOrWhiteSpace(codecError))
            {
                DisposeSafely(inputStream, externalOwner);
                return OpenedBassStream.Failure(codecError);
            }
        }

        // 2. 普通格式交由 BASS 原生解码。
        var managedSource = new ManagedStreamSource(inputStream, contentLength, externalOwner);
        var streamSystem = useUnbuffered ? StreamSystem.NoBuffer : StreamSystem.Buffer;
        var extraFlags = !useUnbuffered && contentLength <= 0
            ? BassFlags.StreamDownloadBlocks
            : BassFlags.Default;

        var rawHandle = Bass.CreateStream(
            streamSystem,
            sourceFlags | extraFlags,
            managedSource.CreateProcedures(),
            IntPtr.Zero);

        if (rawHandle == 0)
        {
            var err = $"BASS CreateStream 失败: {Bass.LastError}";
            managedSource.Dispose();
            return OpenedBassStream.Failure(err);
        }

        // 3. 若 BASS 解出的普通格式（如 5.1 FLAC / 7.1 AAC）超出输出设备声道能力，自动套上按需矩阵适配层。
        return AdaptRawHandleIfNeeded(
            rawHandle,
            managedSource,
            sourceFlags,
            deviceSpeakers,
            channelMode,
            maxSupportedChannels);
    }

    // 供响度归一化、过渡分析等纯后台分析模块使用：支持本地文件、缓存文件、AV3A 与多声道统一适配。
    public static OpenedBassStream OpenForAnalysis(string source, bool isLocal, BassFlags flags)
    {
        // 分析路径统一适配为 <=2 声道（立体声/单声道），确保 BassLoud(BS.1770) 与过渡窗口能量测算对 7.1/7.1.4 一致准确。
        if (RemoteAudioCache.TryGetCachedFilePath(source, out var cachedPath))
        {
            var cached = OpenLocalFile(cachedPath, flags, deviceSpeakers: 2, AudioChannelMode.ForceStereo, 2);
            if (cached.IsSuccess || !string.IsNullOrWhiteSpace(cached.Error))
            {
                return cached;
            }
        }

        if (!isLocal && source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return OpenPathOrUrl(source, flags, deviceSpeakers: 2, AudioChannelMode.ForceStereo, 2);
        }

        if (File.Exists(source))
        {
            return OpenLocalFile(source, flags, deviceSpeakers: 2, AudioChannelMode.ForceStereo, 2);
        }

        return source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? OpenPathOrUrl(source, flags, deviceSpeakers: 2, AudioChannelMode.ForceStereo, 2)
            : OpenedBassStream.Failure($"分析音频源不存在: {source}");
    }

    private static OpenedBassStream OpenLocalFile(
        string filePath,
        BassFlags sourceFlags,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels)
    {
        FileStream? probeStream = null;
        try
        {
            probeStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.RandomAccess);

            if (MightMatchAnyCustomCodec(probeStream))
            {
                var custom = TryOpenWithCustomCodecs(
                    probeStream,
                    externalOwner: null,
                    sourceFlags,
                    deviceSpeakers,
                    channelMode,
                    maxSupportedChannels,
                    out var codecError);

                if (custom != null)
                {
                    probeStream = null; // 所有权已转移给自定义流
                    return custom;
                }

                if (!string.IsNullOrWhiteSpace(codecError))
                {
                    return OpenedBassStream.Failure(codecError);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OpenedBassStream.Failure(ex.Message);
        }
        finally
        {
            probeStream?.Dispose();
        }

        var rawHandle = Bass.CreateStream(filePath, 0, 0, sourceFlags);
        if (rawHandle == 0)
        {
            return OpenedBassStream.Failure($"BASS CreateStream 失败: {Bass.LastError}");
        }

        return AdaptRawHandleIfNeeded(
            rawHandle,
            rawOwner: null,
            sourceFlags,
            deviceSpeakers,
            channelMode,
            maxSupportedChannels);
    }

    private static bool MightMatchAnyCustomCodec(Stream seekableInput)
    {
        Span<byte> header = stackalloc byte[12];
        var originalPos = seekableInput.Position;
        seekableInput.Position = 0;
        var read = 0;
        while (read < header.Length)
        {
            var n = seekableInput.Read(header[read..]);
            if (n == 0)
                break;

            read += n;
        }

        seekableInput.Position = originalPos;
        if (read == 0)
            return false;

        var slice = header[..read];
        foreach (var codec in CustomCodecs)
        {
            if (codec.MightMatch(slice))
                return true;
        }

        return false;
    }

    private static OpenedBassStream? TryOpenWithCustomCodecs(
        Stream seekableInput,
        IDisposable? externalOwner,
        BassFlags sourceFlags,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels,
        out string? error)
    {
        error = null;
        Span<byte> header = stackalloc byte[12];
        seekableInput.Position = 0;
        var read = 0;
        while (read < header.Length)
        {
            var n = seekableInput.Read(header[read..]);
            if (n == 0)
                break;

            read += n;
        }

        seekableInput.Position = 0;
        if (read == 0)
            return null;

        var headerSlice = header[..read];
        foreach (var codec in CustomCodecs)
        {
            if (!codec.MightMatch(headerSlice))
                continue;

            if (codec.TryOpen(
                    seekableInput,
                    deviceSpeakers,
                    channelMode,
                    maxSupportedChannels,
                    out var decoded,
                    out error))
            {
                var managed = new ManagedStreamSource(decoded!.WaveStream, decoded.WaveStream.Length, externalOwner);
                var handle = Bass.CreateStream(
                    StreamSystem.NoBuffer,
                    sourceFlags,
                    managed.CreateProcedures(),
                    IntPtr.Zero);

                if (handle == 0)
                {
                    error = $"BASS 打开自定义解码流失败: {Bass.LastError}";
                    managed.Dispose();
                    return null;
                }

                return OpenedBassStream.Success(handle, managed, decoded.SourceLayout, decoded.TargetLayout);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                return null;
            }
        }

        return null;
    }

    // 检查 BASS 原生流声道数；若超出设备能力或用户策略，自动包装一层零落盘按需矩阵适配流。
    private static OpenedBassStream AdaptRawHandleIfNeeded(
        int rawHandle,
        IDisposable? rawOwner,
        BassFlags sourceFlags,
        int deviceSpeakers,
        AudioChannelMode channelMode,
        int maxSupportedChannels)
    {
        if (!Bass.ChannelGetInfo(rawHandle, out var info))
        {
            var fallbackLayout = AudioChannelLayout.Stereo;
            return OpenedBassStream.Success(rawHandle, rawOwner, fallbackLayout, fallbackLayout);
        }

        var sourceLayout = AudioChannelLayout.FromStandardChannelCount(info.Channels);
        var targetLayout = AudioChannelLayout.ResolveTargetLayout(
            sourceLayout,
            deviceSpeakers,
            channelMode,
            maxSupportedChannels);

        // 绝大多数普通立体声/单声道，或设备原生支持的多声道拓扑：直接零开销直通。
        if (sourceLayout.HasSameRoleOrder(targetLayout))
        {
            return OpenedBassStream.Success(rawHandle, rawOwner, sourceLayout, targetLayout);
        }

        // 仅对已知有限长度的 Decode 流启用虚拟 WAV 适配层；未知长度的持续流直接放行避免破坏直播流。
        var rawLength = Bass.ChannelGetLength(rawHandle, PositionFlags.Bytes);
        if (rawLength <= 0 || !sourceFlags.HasFlag(BassFlags.Decode))
        {
            return OpenedBassStream.Success(rawHandle, rawOwner, sourceLayout, sourceLayout);
        }

        BassChannelAdapterStream? adapterStream = null;
        ManagedStreamSource? managedAdapter = null;
        try
        {
            adapterStream = new BassChannelAdapterStream(
                rawHandle,
                rawOwner,
                info.Frequency > 0 ? info.Frequency : 44100,
                sourceLayout,
                targetLayout,
                rawLength);

            managedAdapter = new ManagedStreamSource(adapterStream, adapterStream.Length, owner: null);
            var adaptedHandle = Bass.CreateStream(
                StreamSystem.NoBuffer,
                sourceFlags,
                managedAdapter.CreateProcedures(),
                IntPtr.Zero);

            if (adaptedHandle == 0)
            {
                var err = $"BASS 创建多声道适配流失败: {Bass.LastError}";
                managedAdapter.Dispose();
                return OpenedBassStream.Failure(err);
            }

            return OpenedBassStream.Success(adaptedHandle, managedAdapter, sourceLayout, targetLayout);
        }
        catch (Exception ex)
        {
            if (managedAdapter != null)
            {
                managedAdapter.Dispose();
            }
            else if (adapterStream != null)
            {
                adapterStream.Dispose();
            }
            else
            {
                Bass.StreamFree(rawHandle);
                rawOwner?.Dispose();
            }

            return OpenedBassStream.Failure(ex.Message);
        }
    }

    private static void DisposeSafely(Stream stream, IDisposable? externalOwner)
    {
        try { stream.Dispose(); } catch { }
        try { externalOwner?.Dispose(); } catch { }
    }
}
