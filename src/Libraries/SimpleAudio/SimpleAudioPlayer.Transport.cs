using ManagedBass;
using ManagedBass.Fx;

namespace SimpleAudio;

public partial class SimpleAudioPlayer : IDisposable
{
    private const int RemoteStreamLoadAttempts = 3;
    private const int RemoteStreamRetryDelayMilliseconds = 300;

    public string? LastErrorDetail { get; private set; }

    public bool Load(string url)
    {
        return LoadCore(url, sourceFlags => CreateSourceStream(url, sourceFlags));
    }

    public bool Load(Stream inputStream, long contentLength, string sourceDescription, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        var managedSource = new ManagedStreamSource(inputStream, contentLength, owner);
        var loaded = LoadCore(sourceDescription, sourceFlags =>
            Bass.CreateStream(StreamSystem.Buffer, sourceFlags, managedSource.CreateProcedures(), IntPtr.Zero),
            managedSource);
        if (!loaded)
            managedSource.Dispose();

        return loaded;
    }

    private bool LoadCore(
        string sourceDescription,
        Func<BassFlags, int> createSourceStream,
        IDisposable? streamSourceOwner = null)
    {
        Stop();
        LastErrorDetail = null;

        var flags = BassFlags.Default | BassFlags.Float;
        var sourceFlags = flags | BassFlags.Decode;
        var sourceStream = 0;
        lock (BassDeviceGate)
        {
            if (!TryInitializeOutputDevice(_preferredOutputDeviceId, out _))
            {
                _preferredOutputDeviceId = Bass.DefaultDevice;
                if (!TryInitializeOutputDevice(Bass.DefaultDevice, out _))
                {
                    LastErrorDetail = $"音频输出设备初始化失败，preferredDevice={_preferredOutputDeviceId}";
                    return false;
                }
            }

            sourceStream = createSourceStream(sourceFlags);

            if (sourceStream != 0)
            {
                Stream = BassFx.TempoCreate(sourceStream, flags | BassFlags.FxFreeSource);
                if (Stream == 0)
                    Bass.StreamFree(sourceStream);
                else
                {
                    SourceStream = sourceStream;
                    StreamSourceOwner = streamSourceOwner;
                }
            }
        }

        if (Stream == 0)
        {
            LastErrorDetail =
                $"BASS CreateStream 失败: path={sourceDescription}, extension={Path.GetExtension(sourceDescription)}, error={Bass.LastError}";
            Console.WriteLine($"[BASS CreateStream Error] path={sourceDescription}, extension={Path.GetExtension(sourceDescription)}, error={Bass.LastError}");
            return false;
        }

        SyncProcedure endSync = EndSync;
        EndSyncProc = endSync;
        Bass.ChannelSetSync(Stream, SyncFlags.End, 0, endSync, IntPtr.Zero);

        PeakEqHandle = 0;
        StereoDspHandle = 0;
        ReverbHandle = 0;
        ChorusHandle = 0;
        EchoHandle = 0;
        HighShelfHandle = 0;
        LowPassHandle = 0;
        TransitionGain = 1.0f;
        TransitionToneDepth = 0f;

        ApplyEQ();
        ApplySpatialEffects();
        ApplyTransitionTone();
        ApplyPlaybackSpeed();
        UpdateActualVolume();

        return true;
    }

    private static int CreateSourceStream(string source, BassFlags sourceFlags)
    {
        if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            for (var attempt = 1; attempt <= RemoteStreamLoadAttempts; attempt++)
            {
                var stream = Bass.CreateStream(source, 0, sourceFlags, null, IntPtr.Zero);
                if (stream != 0)
                    return stream;

                if (attempt < RemoteStreamLoadAttempts)
                    Thread.Sleep(RemoteStreamRetryDelayMilliseconds * attempt);
            }

            return 0;
        }

        return File.Exists(source)
            ? Bass.CreateStream(source, 0, 0, sourceFlags)
            : 0;
    }

    public void Play()
    {
        if (Stream != 0)
        {
            if (!Bass.ChannelPlay(Stream))
            {
                LastErrorDetail = $"BASS ChannelPlay 失败: error={Bass.LastError}";
                Console.WriteLine($"[Play Error] {Bass.LastError}");
            }
        }
    }

    public void Pause()
    {
        if (Stream != 0 && IsPlaying)
        {
            Bass.ChannelPause(Stream);
        }
    }

    public void Stop()
    {
        IDisposable? streamSourceOwner;
        lock (BassDeviceGate)
        {
            var stream = Stream;
            var sourceStream = SourceStream;
            Stream = 0;
            SourceStream = 0;
            streamSourceOwner = StreamSourceOwner;
            StreamSourceOwner = null;

            if (stream != 0)
            {
                Bass.ChannelStop(stream);
                if (!Bass.StreamFree(stream) && sourceStream != 0)
                    Bass.StreamFree(sourceStream);
            }
        }

        streamSourceOwner?.Dispose();

        TransitionGain = 1.0f;
        TransitionToneDepth = 0f;
    }

    public static void Free()
    {
        lock (BassDeviceGate)
        {
            for (var deviceId = 1; Bass.GetDeviceInfo(deviceId, out var info); deviceId++)
            {
                if (!info.IsInitialized)
                {
                    continue;
                }

                try
                {
                    Bass.CurrentDevice = deviceId;
                    Bass.Free();
                }
                catch
                {
                    // Best-effort shutdown; process exit will release any remaining native handles.
                }
            }
        }
    }

    public bool SetOutputDevice(int deviceId)
    {
        lock (BassDeviceGate)
        {
            if (!TryInitializeOutputDevice(deviceId, out var actualDeviceId))
            {
                LastErrorDetail = $"音频输出设备切换失败，device={deviceId}";
                return false;
            }

            _preferredOutputDeviceId = deviceId;
            if (Stream == 0)
            {
                return true;
            }

            if (Bass.ChannelGetDevice(Stream) == actualDeviceId)
            {
                return true;
            }

            if (Bass.ChannelSetDevice(Stream, actualDeviceId))
            {
                UpdateActualVolume();
                return true;
            }

            Console.WriteLine($"[BASS ChannelSetDevice Error] device={deviceId}, actualDevice={actualDeviceId}, error={Bass.LastError}");
            LastErrorDetail =
                $"BASS ChannelSetDevice 失败: device={deviceId}, actualDevice={actualDeviceId}, error={Bass.LastError}";
            return false;
        }
    }

    public void SetVolume(float volume)
    {
        if (Stream != 0)
        {
            UserVolume = volume;
        }

        UpdateActualVolume();
    }

    public float GetVolume()
    {
        if (Stream != 0 && Bass.ChannelGetAttribute(Stream, ChannelAttribute.Volume, out var vol))
        {
            return vol;
        }

        return 0f;
    }

    public void SetNormalizationGain(float gain)
    {
        VolumeNormalizationGain = Math.Clamp(
            gain,
            TrackVolumeNormalizer.MinNormalizationGain,
            TrackVolumeNormalizer.MaxNormalizationGain);
        UpdateActualVolume();
    }

    public void SetPlaybackSpeed(float speed)
    {
        PlaybackSpeed = Math.Clamp(speed, 0.5f, 2.0f);
        ApplyPlaybackSpeed();
    }

    public float GetPlaybackSpeed()
    {
        return PlaybackSpeed;
    }

    public void SetPosition(TimeSpan time)
    {
        if (Stream == 0)
        {
            return;
        }

        var positionBytes = Bass.ChannelSeconds2Bytes(Stream, time.TotalSeconds);
        Bass.ChannelSetPosition(Stream, positionBytes);
    }

    public void SetPosition(double percentage)
    {
        if (Stream == 0)
        {
            return;
        }

        var len = Bass.ChannelGetLength(Stream);
        if (len > 0)
        {
            var pos = (long)(len * Math.Clamp(percentage, 0.0, 1.0));
            Bass.ChannelSetPosition(Stream, pos);
        }
    }

    public TimeSpan GetDuration()
    {
        if (Stream == 0)
        {
            return TimeSpan.Zero;
        }

        var len = Bass.ChannelGetLength(Stream);
        return len < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(Stream, len));
    }

    public TimeSpan GetPosition()
    {
        if (Stream == 0)
        {
            return TimeSpan.Zero;
        }

        var pos = Bass.ChannelGetPosition(Stream);
        return pos < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(Stream, pos));
    }

    public void Dispose()
    {
        Stop();
    }

    private void UpdateActualVolume()
    {
        if (Stream != 0)
        {
            var hasSpatialFx = StereoWidth > 0.001f || ReverbAmount > 0.001f || ChorusMix > 0.001f || EchoMix > 0.001f;
            var toneHeadroom = TransitionToneDepth > 0.001f ? 0.9f : 1.0f;
            var headroom = hasSpatialFx || CurrentEq.Any(g => g > 3f) ? 0.8f : 1.0f;
            var actualVolume = (float)Math.Pow(UserVolume, 2) * VolumeNormalizationGain * headroom * toneHeadroom *
                               Math.Clamp(TransitionGain, 0f, 1.25f);
            // BASS_ATTRIB_VOL explicitly supports values above 1.0 for amplification.
            // The per-track true-peak measurement has already limited unsafe positive gain.
            Bass.ChannelSetAttribute(Stream, ChannelAttribute.Volume, Math.Max(actualVolume, 0f));
        }
    }

    private void ApplyPlaybackSpeed()
    {
        if (Stream == 0)
        {
            return;
        }

        var tempoPercent = (PlaybackSpeed - 1.0f) * 100.0f;
        Bass.ChannelSetAttribute(Stream, ChannelAttribute.Tempo, tempoPercent);
    }

    private void EndSync(int handle, int channel, int data, IntPtr user)
    {
        PlaybackEnded?.Invoke();
    }

    private static string GetBassPluginName(string baseName)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{baseName}.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"lib{baseName}.dylib";
        }

        return $"lib{baseName}.so";
    }
}
