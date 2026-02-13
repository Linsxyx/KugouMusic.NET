using ManagedBass;

namespace SimpleAudio;

/// <summary>
///     适配客户端的增强版播放器，支持事件通知和进度跳转
/// </summary>
public class ObservableAudioPlayer : IDisposable
{
    private SyncProcedure? _endSync; // 防止委托被 GC 回收
    private int _stream;
    private int _syncHandle; // 用于保存 Sync 句柄

    public ObservableAudioPlayer()
    {
        // 保持你原有的初始化逻辑
        if (Bass.CurrentDevice == -1)
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                // 这里可以用 Debug.WriteLine 或者抛出异常，视需求而定
            }

        // 加载插件 (保持你的原有逻辑)
        Bass.PluginLoad(GetBassPluginName("bassflac"));
    }

    public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    public void Dispose()
    {
        Stop();
        Bass.Free();
    }

    // 事件：播放结束（自动切歌用）
    public event Action? PlaybackEnded;

    public bool Load(string url)
    {
        Stop(); // 清理旧流

        // 创建流
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _stream = Bass.CreateStream(url, 0, BassFlags.Default, null, IntPtr.Zero);
        }
        else
        {
            if (!File.Exists(url)) return false;
            _stream = Bass.CreateStream(url);
        }

        if (_stream != 0)
        {
            // 核心改进：设置 BASS_SYNC_END，当播放结束时触发回调
            // 这一步对于客户端播放器至关重要
            _endSync = EndSyncCallback;
            _syncHandle = Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSync, IntPtr.Zero);
            return true;
        }

        return false;
    }

    private void EndSyncCallback(int handle, int channel, int data, IntPtr user)
    {
        // BASS 的回调在后台线程，触发事件通知 ViewModel
        PlaybackEnded?.Invoke();
    }

    public void Play()
    {
        if (_stream != 0) Bass.ChannelPlay(_stream);
    }

    public void Pause()
    {
        if (_stream != 0) Bass.ChannelPause(_stream);
    }

    public void Stop()
    {
        if (_stream != 0)
        {
            // 停止时先移除 Sync，防止误触 PlaybackEnded
            if (_syncHandle != 0)
            {
                Bass.ChannelRemoveSync(_stream, _syncHandle);
                _syncHandle = 0;
            }

            Bass.ChannelStop(_stream);
            Bass.StreamFree(_stream);
            _stream = 0;
        }
    }

    // 新增：跳转进度
    public void Seek(double seconds)
    {
        if (_stream != 0)
        {
            var pos = Bass.ChannelSeconds2Bytes(_stream, seconds);
            Bass.ChannelSetPosition(_stream, pos);
        }
    }

    public void SetVolume(float volume)
    {
        if (_stream != 0)
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
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

    private static string GetBassPluginName(string baseName)
    {
        if (OperatingSystem.IsWindows()) return $"{baseName}.dll";
        if (OperatingSystem.IsMacOS()) return $"lib{baseName}.dylib";
        return $"lib{baseName}.so";
    }
}