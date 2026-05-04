using ManagedBass;

namespace SimpleAudio;

public partial class SimpleAudioPlayer : IDisposable
{
    public bool Load(string url)
    {
        Stop();

        var flags = BassFlags.Default | BassFlags.Float;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Stream = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
        }
        else if (File.Exists(url))
        {
            Stream = Bass.CreateStream(url, 0, 0, flags);
        }

        if (Stream == 0)
        {
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
        UpdateActualVolume();

        return true;
    }

    public void Play()
    {
        if (Stream != 0)
        {
            if (!Bass.ChannelPlay(Stream))
            {
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
        if (Stream != 0)
        {
            Bass.ChannelStop(Stream);
            Bass.StreamFree(Stream);
            Stream = 0;
        }

        TransitionGain = 1.0f;
        TransitionToneDepth = 0f;
    }

    public static void Free()
    {
        Bass.Free();
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
            var actualVolume = (float)Math.Pow(UserVolume, 2) * headroom * toneHeadroom * Math.Clamp(TransitionGain, 0f, 1.25f);
            Bass.ChannelSetAttribute(Stream, ChannelAttribute.Volume, Math.Clamp(actualVolume, 0f, 1f));
        }
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
