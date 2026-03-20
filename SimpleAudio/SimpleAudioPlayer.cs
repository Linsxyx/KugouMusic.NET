using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.DirectX8;
using ManagedBass.Fx;

namespace SimpleAudio;

public class SimpleAudioPlayer : IDisposable
{
    private SyncProcedure _endSyncProc;
    private int _stream;

    
    private int _peakEqHandle = 0; 
    
    private readonly DSPProcedure _stereoDspProc;
    private int _stereoDspHandle = 0;
    
    private int _reverbHandle = 0;
    private int _chorusHandle = 0;
    private int _echoHandle = 0;
    
    private float[] _dspBuffer = Array.Empty<float>();
    
    private float[] _currentEQ = new float[10];
    private bool _surroundEnabled = false;
    private float _userVolume = 1.0f; 

    
    private static readonly float[] EQFreqs = [141f, 234f, 469f, 844f, 1300f, 2200f, 3700f, 5800f, 9000f, 13800f];

    public SimpleAudioPlayer()
    {
        _stereoDspProc = StereoEnhancerDSP;
    }

    public event Action PlaybackEnded;

    public static void Initialize()
    {
        if (Bass.CurrentDevice == -1)
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                Console.WriteLine($"[BASS Init Error] {Bass.LastError}");
        
        Bass.PluginLoad(GetBassPluginName("bassflac"));
        if (!OperatingSystem.IsMacOS())
            Bass.PluginLoad(GetBassPluginName("bass_aac"));
        
        try
        {
            var fxVersion = BassFx.Version; 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS_FX Load Error] {ex.Message}");
        }
        
        Bass.Configure(Configuration.NetBufferLength, 5000); 
        Bass.Configure(Configuration.NetPreBuffer, 20); 
        Bass.Configure(Configuration.NetReadTimeOut, 10000);
    }

    #region 状态属性

    public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;


    public bool IsPaused => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Paused;

    public bool IsStopped => _stream == 0 || Bass.ChannelIsActive(_stream) == PlaybackState.Stopped;
    
    public bool IsStalled => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Stalled;

    #endregion

    #region 核心控制

    /// <summary>
    ///     加载音频
    /// </summary>
    public bool Load(string url)
    {
        Stop();
        
        var flags = BassFlags.Default | BassFlags.Float;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            _stream = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
        else if (File.Exists(url))
            _stream = Bass.CreateStream(url, 0, 0, flags);

        if (_stream != 0)
        {
            _endSyncProc = EndSync;
            Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSyncProc, IntPtr.Zero);
    
            _peakEqHandle = 0;
            _stereoDspHandle = 0;
            _reverbHandle = 0; 
            _chorusHandle = 0; 
            _echoHandle = 0; 
    
            ApplyEQ();
            ApplySurround();
            UpdateActualVolume();

            return true;
        }

        return false;
    }
    
    private void UpdateActualVolume()
    {
        if (_stream != 0)
        {
            float headroom = (_surroundEnabled || _currentEQ.Any(g => g > 3f)) ? 0.8f : 1.0f;
            
            
            float actualVolume = (float)Math.Pow(_userVolume, 2) * headroom;
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, Math.Clamp(actualVolume, 0f, 1f));
        }
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
    
    public static void Free()
    {
        Bass.Free();
    }

    #endregion

    #region 进度与音量

    /// <summary>
    ///     设置音量 (0.0 - 1.0)
    /// </summary>
    public void SetVolume(float volume)
    {
        if (_stream != 0) _userVolume = volume;;
        UpdateActualVolume();
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
    
    #region 音效引擎控制

    /// <summary>
    /// 设置10段EQ (长度必须为10, 取值范围一般在 -15 到 15 之间，代表增益 dB)
    /// </summary>
    public void SetEQ(float[] gains)
    {
        if (gains == null || gains.Length != 10) return;
        _currentEQ = gains;
        ApplyEQ();
        UpdateActualVolume(); 
    }

    public void SetSurround(bool enable)
    {
        _surroundEnabled = enable;
        ApplySurround();
        UpdateActualVolume();
    }

    private void ApplyEQ()
    {
        if (_stream == 0) return;
        
        if (_peakEqHandle == 0)
        {
            _peakEqHandle = Bass.ChannelSetFX(_stream, EffectType.PeakEQ, 0);
        }

        if (_peakEqHandle != 0)
        {
            for (int i = 0; i < 10; i++)
            {
                var eq = new PeakEQParameters
                {
                    lBand = i,             
                    fCenter = EQFreqs[i],  
                    fGain = _currentEQ[i], 
                    fBandwidth = 1f,       
                    fQ = 0f                
                };
                Bass.FXSetParameters(_peakEqHandle, eq);
            }
        }
    }

    private void ApplySurround()
    {
        if (_stream == 0) return;

        
        if (_stereoDspHandle != 0) 
        { 
            Bass.ChannelRemoveDSP(_stream, _stereoDspHandle); 
            _stereoDspHandle = 0; 
        }
        if (_reverbHandle != 0)
        {
            Bass.ChannelRemoveFX(_stream, _reverbHandle);
            _reverbHandle = 0;
        }
        if (_chorusHandle != 0)
        {
            Bass.ChannelRemoveFX(_stream, _chorusHandle);
            _chorusHandle = 0;
        }
        if (_echoHandle != 0)
        {
            Bass.ChannelRemoveFX(_stream, _echoHandle);
            _echoHandle = 0;
        }

        if (_surroundEnabled)
        {
            Bass.ChannelGetInfo(_stream, out var info);
            if (info.Channels == 2)
            {
                _stereoDspHandle = Bass.ChannelSetDSP(_stream, _stereoDspProc, IntPtr.Zero, 0);
                
                _reverbHandle = Bass.ChannelSetFX(_stream, EffectType.DXReverb, 1);
                var reverb = new DXReverbParameters
                {
                    fInGain = 0f,
                    fReverbMix = -6f,     
                    fReverbTime = 1500f,   
                    fHighFreqRTRatio = 0.25f
                };
                Bass.FXSetParameters(_reverbHandle, reverb);
                
                _chorusHandle = Bass.ChannelSetFX(_stream, EffectType.DXChorus, 2);
                var chorus = new DXChorusParameters
                {
                    fDelay = 10f,          
                    fDepth = 8f,           
                    fFeedback = 10f,        
                    fFrequency = 0.3f,     
                    lWaveform = DXWaveform.Sine,         
                    fWetDryMix = 20f,      
                    lPhase = DXPhase.Positive180 
                };
                Bass.FXSetParameters(_chorusHandle, chorus);
            }
            
            _echoHandle = Bass.ChannelSetFX(_stream, EffectType.Echo, 3);
            Bass.FXSetParameters(_echoHandle, new EchoParameters
            {
                fDryMix = 0.9f,    
                fWetMix = 0.15f,   
                fFeedback = 0.2f,  
                fDelay = 0.25f,    
                bStereo = 0
            });
        }
    }
    
    
    private void StereoEnhancerDSP(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length == 0 || buffer == IntPtr.Zero) return;
        
        int floatCount = length / 4; 
        
        if (_dspBuffer.Length < floatCount) _dspBuffer = new float[floatCount];
        
        Marshal.Copy(buffer, _dspBuffer, 0, floatCount);
        
        float width = 0.20f; 
        
        for (int i = 0; i < floatCount - 1; i += 2)
        {
            float l = _dspBuffer[i];
            float r = _dspBuffer[i + 1];
            
            _dspBuffer[i]     = l + (l - r) * width;
            _dspBuffer[i + 1] = r + (r - l) * width;
        }
        
        Marshal.Copy(_dspBuffer, 0, buffer, floatCount);
    }
    #endregion

}