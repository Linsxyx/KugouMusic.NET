using ManagedBass;
using ManagedBass.Fx;
using SimpleAudio.Codecs;

namespace SimpleAudio;

public partial class SimpleAudioPlayer : IDisposable
{
    public string? LastErrorDetail { get; private set; }

    public bool Load(string url)
    {
        var loaded = LoadCore(
            url,
            (sourceFlags, deviceSpeakers) => BassStreamOpener.OpenPathOrUrl(url, sourceFlags, deviceSpeakers, _state.ChannelMode),
            reloadableSourcePath: url);
        return loaded;
    }

    public bool Load(
        Stream inputStream,
        long contentLength,
        string sourceDescription,
        IDisposable? owner = null,
        bool useUnbuffered = false)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        var openerInvoked = false;
        var reloadablePath = RemoteAudioCache.TryGetCachedFilePath(sourceDescription, out var cached)
            ? cached
            : File.Exists(sourceDescription)
                ? sourceDescription
                : sourceDescription.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? sourceDescription
                    : null;

        var loaded = LoadCore(
            sourceDescription,
            (sourceFlags, deviceSpeakers) =>
            {
                openerInvoked = true;
                return BassStreamOpener.OpenStream(
                    inputStream,
                    contentLength,
                    sourceFlags,
                    owner,
                    useUnbuffered,
                    deviceSpeakers,
                    _state.ChannelMode);
            },
            reloadableSourcePath: reloadablePath);

        if (!openerInvoked)
        {
            try { inputStream.Dispose(); } catch { }
            if (!ReferenceEquals(owner, inputStream))
            {
                try { owner?.Dispose(); } catch { }
            }
        }

        return loaded;
    }

    public bool Load(RemoteAudioPlaybackSource playbackSource)
    {
        ArgumentNullException.ThrowIfNull(playbackSource);

        if (!string.IsNullOrWhiteSpace(playbackSource.LocalFilePath))
        {
            return Load(playbackSource.LocalFilePath);
        }

        if (playbackSource.Stream == null)
        {
            LastErrorDetail = $"远程播放源为空: {playbackSource.SourceDescription}";
            return false;
        }

        var loaded = Load(
            playbackSource.Stream,
            playbackSource.ContentLength,
            playbackSource.SourceDescription,
            playbackSource.Owner,
            playbackSource.UseUnbufferedStream);
        if (loaded)
        {
            playbackSource.MarkDetached();
        }

        return loaded;
    }

    public void SetChannelMode(AudioChannelMode mode)
    {
        if (_state.ChannelMode == mode)
            return;

        _state.ChannelMode = mode;
        TryReloadCurrentSourceForChannelAdaptation();
    }

    private bool LoadCore(
        string sourceDescription,
        Func<BassFlags, int, OpenedBassStream> openSourceStream,
        string? reloadableSourcePath = null)
    {
        Stop();
        LastErrorDetail = null;

        var flags = BassFlags.Default | BassFlags.Float;
        var sourceFlags = flags | BassFlags.Decode;
        string? openError = null;
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

            var deviceSpeakers = Bass.GetInfo(out var bassInfo) && bassInfo.SpeakerCount > 0
                ? bassInfo.SpeakerCount
                : 2;

            using var opened = openSourceStream(sourceFlags, deviceSpeakers);
            openError = opened.Error;

            if (opened.IsSuccess)
            {
                var tempoStream = BassFx.TempoCreate(opened.Handle, flags | BassFlags.FxFreeSource);
                if (tempoStream == 0)
                {
                    openError = $"BASS_FX TempoCreate 失败(ch={opened.OutputChannels}): {Bass.LastError}";
                }
                else
                {
                    var (sourceStream, owner) = opened.Detach();
                    Stream = tempoStream;
                    SourceStream = sourceStream;
                    StreamSourceOwner = owner;
                    _state.SourceChannelLayout = opened.SourceLayout;
                    _state.SourceChannelCount = opened.SourceChannels;
                    _state.OutputChannelCount = opened.OutputChannels;
                    _state.SourceChannelLayoutName = opened.SourceLayoutName;
                    _state.OutputChannelLayoutName = opened.OutputLayoutName;
                    _state.ReloadableSourcePath = reloadableSourcePath;
                }
            }
        }

        if (Stream == 0)
        {
            var detailSuffix = string.IsNullOrWhiteSpace(openError) ? string.Empty : $", detail={openError}";
            LastErrorDetail =
                $"BASS CreateStream 失败: path={sourceDescription}, extension={Path.GetExtension(sourceDescription)}, error={Bass.LastError}{detailSuffix}";
            Console.WriteLine($"[BASS CreateStream Error] {LastErrorDetail}");
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
        LimiterDspHandle = 0;
        TransitionGain = 1.0f;
        TransitionToneDepth = 0f;

        ApplyEQ();
        ApplySpatialEffects();
        ApplyTransitionTone();
        ApplyPlaybackSpeed();
        UpdateActualVolume();

        return true;
    }

    // 切换设备或声道策略后，仅当当前在播多声道曲目且目标布局变化时无缝重载并恢复进度。
    private bool TryReloadCurrentSourceForChannelAdaptation()
    {
        if (Stream == 0 || _state.SourceChannelCount <= 2)
            return false;

        var reloadSource = _state.ReloadableSourcePath;
        if (string.IsNullOrWhiteSpace(reloadSource))
            return false;

        if (!File.Exists(reloadSource) && !RemoteAudioCache.TryGetCachedFilePath(reloadSource, out reloadSource))
            return false;

        var newSpeakers = GetCurrentOutputDeviceSpeakerCount();
        var sourceLayout = _state.SourceChannelLayout
                           ?? AudioChannelLayout.FromStandardChannelCount(_state.SourceChannelCount);
        var nextTarget = AudioChannelLayout.ResolveTargetLayout(sourceLayout, newSpeakers, _state.ChannelMode);
        if (nextTarget.Channels == _state.OutputChannelCount)
            return false;

        var wasPlaying = IsPlaying;
        var savedPosition = GetPosition();
        if (!Load(reloadSource))
            return false;

        if (savedPosition > TimeSpan.Zero)
        {
            SetPosition(savedPosition);
        }

        if (wasPlaying)
        {
            Play();
        }

        return true;
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

            // 先唤醒可能阻塞在 Read 回调里的自定义流，避免 Bass.StreamFree 等待解码线程卡住。
            if (streamSourceOwner is IAbortableStreamSource abortable)
            {
                abortable.Abort();
            }

            if (stream != 0)
            {
                Bass.ChannelStop(stream);
                if (!Bass.StreamFree(stream) && sourceStream != 0)
                    Bass.StreamFree(sourceStream);
            }
        }

        streamSourceOwner?.Dispose();

        _state.SourceChannelLayout = null;
        _state.SourceChannelCount = 0;
        _state.OutputChannelCount = 0;
        _state.SourceChannelLayoutName = string.Empty;
        _state.OutputChannelLayoutName = string.Empty;
        _state.ReloadableSourcePath = null;
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
        var switched = false;
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
                switched = true;
            }
            else
            {
                Console.WriteLine($"[BASS ChannelSetDevice Error] device={deviceId}, actualDevice={actualDeviceId}, error={Bass.LastError}");
                LastErrorDetail =
                    $"BASS ChannelSetDevice 失败: device={deviceId}, actualDevice={actualDeviceId}, error={Bass.LastError}";
                return false;
            }
        }

        if (switched)
        {
            TryReloadCurrentSourceForChannelAdaptation();
        }

        return switched;
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
            // The composite gain can exceed 1.0 (normalization x transition x user volume). Digital
            // amplification past full scale clips at the DAC, and the volume attribute applies after
            // the output limiter, so clamp it to 1.0 to keep the limiter effective.
            Bass.ChannelSetAttribute(Stream, ChannelAttribute.Volume, Math.Clamp(actualVolume, 0f, 1f));
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
