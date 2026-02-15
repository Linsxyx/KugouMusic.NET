using ManagedBass;

namespace SimpleAudio;

public class SimpleAudioPlayer : IDisposable
{
    private SyncProcedure _endSyncProc;
    private int _stream;

    public SimpleAudioPlayer()
    {
        InitializeBass();
    }

    public event Action PlaybackEnded;

    private void InitializeBass()
    {
        if (Bass.CurrentDevice == -1)
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                Console.WriteLine($"[BASS Init Error] {Bass.LastError}");


        try
        {
            Bass.PluginLoad(GetBassPluginName("bassflac"));
        }
        catch
        {
            /* 忽略插件加载错误，防止崩溃 */
        }
    }

    #region 状态属性

    public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;


    public bool IsPaused => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Paused;

    public bool IsStopped => _stream == 0 || Bass.ChannelIsActive(_stream) == PlaybackState.Stopped;

    #endregion

    #region 核心控制

    /// <summary>
    ///     加载音频
    /// </summary>
    public bool Load(string url)
    {
        Stop();

        var flags = BassFlags.Default;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _stream = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
        }
        else
        {
            if (!File.Exists(url)) return false;
            _stream = Bass.CreateStream(url, 0, 0, flags);
        }

        if (_stream != 0)
        {
            _endSyncProc = EndSync;
            Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSyncProc, IntPtr.Zero);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     播放或继续播放
    /// </summary>
    public void Play()
    {
        if (_stream != 0)
            if (!Bass.ChannelPlay(_stream))
                Console.WriteLine($"[Play Error] {Bass.LastError}");
    }

    /// <summary>
    ///     暂停播放
    /// </summary>
    public void Pause()
    {
        if (_stream != 0 && IsPlaying) Bass.ChannelPause(_stream);
    }

    /// <summary>
    ///     停止播放并释放当前流资源
    /// </summary>
    public void Stop()
    {
        if (_stream != 0)
        {
            Bass.ChannelStop(_stream);
            Bass.StreamFree(_stream);
            _stream = 0;
        }
    }

    #endregion

    #region 进度与音量

    /// <summary>
    ///     设置音量 (0.0 - 1.0)
    /// </summary>
    public void SetVolume(float volume)
    {
        if (_stream != 0) Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
    }

    /// <summary>
    ///     获取当前音量
    /// </summary>
    public float GetVolume()
    {
        if (_stream != 0 && Bass.ChannelGetAttribute(_stream, ChannelAttribute.Volume, out var vol)) return vol;
        return 0f;
    }

    /// <summary>
    ///     跳转进度 (Seek)
    /// </summary>
    /// <param name="time">目标时间点</param>
    public void SetPosition(TimeSpan time)
    {
        if (_stream == 0) return;

        // 将时间转换为字节位置
        var positionBytes = Bass.ChannelSeconds2Bytes(_stream, time.TotalSeconds);

        // 执行跳转
        Bass.ChannelSetPosition(_stream, positionBytes);
    }

    /// <summary>
    ///     跳转进度 (按百分比 0.0 - 1.0)
    /// </summary>
    public void SetPosition(double percentage)
    {
        if (_stream == 0) return;

        var len = Bass.ChannelGetLength(_stream);
        if (len > 0)
        {
            var pos = (long)(len * Math.Clamp(percentage, 0.0, 1.0));
            Bass.ChannelSetPosition(_stream, pos);
        }
    }

    public TimeSpan GetDuration()
    {
        if (_stream == 0) return TimeSpan.Zero;
        var len = Bass.ChannelGetLength(_stream);
        return len < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, len));
    }

    public TimeSpan GetPosition()
    {
        if (_stream == 0) return TimeSpan.Zero;
        var pos = Bass.ChannelGetPosition(_stream);
        return pos < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, pos));
    }

    #endregion

    #region 内部逻辑

    private void EndSync(int handle, int channel, int data, IntPtr user)
    {
        PlaybackEnded?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }

    private static string GetBassPluginName(string baseName)
    {
        if (OperatingSystem.IsWindows()) return $"{baseName}.dll";
        if (OperatingSystem.IsMacOS()) return $"lib{baseName}.dylib";
        return $"lib{baseName}.so";
    }

    #endregion
}