using ManagedBass;

namespace KuGou.Audio;

public class SimpleAudioPlayer : IDisposable
{
    private int _stream;

    public SimpleAudioPlayer()
    {
        // 初始化 BASS
        if (Bass.CurrentDevice == -1)
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                Console.WriteLine($"[BASS Init Error] {Bass.LastError}");


        Bass.PluginLoad(GetBassPluginName("bassflac"));
    }

    // 检查是否正在播放
    public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    // 检查是否已停止 (包括播放结束)
    public bool IsStopped => _stream == 0 || Bass.ChannelIsActive(_stream) == PlaybackState.Stopped;

    public void Dispose()
    {
        Stop();
        Bass.Free();
    }

    public bool Load(string url)
    {
        Stop(); // 加载前先停止并释放旧流

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _stream = Bass.CreateStream(url, 0, BassFlags.Default, null, IntPtr.Zero);
        }
        else
        {
            if (!File.Exists(url)) return false;
            _stream = Bass.CreateStream(url);
        }

        return _stream != 0;
    }

    public void Play()
    {
        if (_stream != 0) Bass.ChannelPlay(_stream);
    }

    public void Stop()
    {
        if (_stream != 0)
        {
            Bass.ChannelStop(_stream);
            Bass.StreamFree(_stream);
            _stream = 0;
        }
    }

    public void SetVolume(float volume)
    {
        if (_stream != 0) Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
    }

    // 获取总时长
    public TimeSpan GetDuration()
    {
        if (_stream == 0) return TimeSpan.Zero;
        var len = Bass.ChannelGetLength(_stream);
        return len < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, len));
    }

    // 获取当前进度
    public TimeSpan GetPosition()
    {
        if (_stream == 0) return TimeSpan.Zero;
        var pos = Bass.ChannelGetPosition(_stream);
        return pos < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, pos));
    }

    private static string GetBassPluginName(string baseName)
    {
        if (OperatingSystem.IsWindows()) return $"{baseName}.dll";
        if (OperatingSystem.IsMacOS()) return $"lib{baseName}.dylib";
        return $"lib{baseName}.so";
    }
}