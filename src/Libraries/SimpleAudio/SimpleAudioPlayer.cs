using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Loud;

namespace SimpleAudio;

public partial class SimpleAudioPlayer
{
    private const int NetworkBufferLengthMilliseconds = 15000;
    private const int NetworkPreBufferPercentage = 15;
    private const int NetworkReadTimeoutMilliseconds = 45000;
    private const float DefaultHighShelfCenterHz = 4800f;
    private const float DefaultLowPassCutoffHz = 18000f;
    private static readonly float[] EQFreqs = [141f, 234f, 469f, 844f, 1300f, 2200f, 3700f, 5800f, 9000f, 13800f];
    private static readonly object BassDeviceGate = new();
    private static bool _bassDeviceConfigurationApplied;
    private static int _actualSystemDefaultDeviceId = Bass.DefaultDevice;
    private static int _preferredOutputDeviceId = Bass.DefaultDevice;

    private readonly DSPProcedure _stereoDspProc;
    private readonly PlayerRuntimeState _state = new();

    private int ChorusHandle
    {
        get => _state.ChorusHandle;
        set => _state.ChorusHandle = value;
    }

    private float ChorusMix
    {
        get => _state.ChorusMix;
        set => _state.ChorusMix = value;
    }

    private float[] CurrentEq
    {
        get => _state.CurrentEQ;
        set => _state.CurrentEQ = value;
    }

    private float[] DspBuffer
    {
        get => _state.DspBuffer;
        set => _state.DspBuffer = value;
    }

    private int EchoHandle
    {
        get => _state.EchoHandle;
        set => _state.EchoHandle = value;
    }

    private float EchoMix
    {
        get => _state.EchoMix;
        set => _state.EchoMix = value;
    }

    private SyncProcedure? EndSyncProc
    {
        get => _state.EndSyncProc;
        set => _state.EndSyncProc = value;
    }

    private int HighShelfHandle
    {
        get => _state.HighShelfHandle;
        set => _state.HighShelfHandle = value;
    }

    private int LowPassHandle
    {
        get => _state.LowPassHandle;
        set => _state.LowPassHandle = value;
    }

    private int PeakEqHandle
    {
        get => _state.PeakEqHandle;
        set => _state.PeakEqHandle = value;
    }

    private float PlaybackSpeed
    {
        get => _state.PlaybackSpeed;
        set => _state.PlaybackSpeed = value;
    }

    private float TransitionGain
    {
        get => _state.TransitionGain;
        set => _state.TransitionGain = value;
    }

    private float TransitionToneDepth
    {
        get => _state.TransitionToneDepth;
        set => _state.TransitionToneDepth = value;
    }

    private int ReverbHandle
    {
        get => _state.ReverbHandle;
        set => _state.ReverbHandle = value;
    }

    private float ReverbAmount
    {
        get => _state.ReverbAmount;
        set => _state.ReverbAmount = value;
    }

    private float ReverbTimeMs
    {
        get => _state.ReverbTimeMs;
        set => _state.ReverbTimeMs = value;
    }

    private int StereoDspHandle
    {
        get => _state.StereoDspHandle;
        set => _state.StereoDspHandle = value;
    }

    private float StereoWidth
    {
        get => _state.StereoWidth;
        set => _state.StereoWidth = value;
    }

    private int Stream
    {
        get => _state.Stream;
        set => _state.Stream = value;
    }

    private bool SurroundEnabled
    {
        get => _state.SurroundEnabled;
        set => _state.SurroundEnabled = value;
    }

    private float UserVolume
    {
        get => _state.UserVolume;
        set => _state.UserVolume = value;
    }

    private float VolumeNormalizationGain
    {
        get => _state.VolumeNormalizationGain;
        set => _state.VolumeNormalizationGain = value;
    }

    public SimpleAudioPlayer()
    {
        _stereoDspProc = StereoEnhancerDSP;
    }

    public event Action? PlaybackEnded;

    public static void Initialize(int preferredDeviceId = Bass.DefaultDevice)
    {
        lock (BassDeviceGate)
        {
            ConfigureBassDeviceEnumeration();
            _preferredOutputDeviceId = preferredDeviceId;

            if (!TryInitializeOutputDevice(preferredDeviceId, out _))
            {
                _preferredOutputDeviceId = Bass.DefaultDevice;
                TryInitializeOutputDevice(Bass.DefaultDevice, out _);
            }

            TryLoadBassPlugin("bassflac");
            TryLoadBassPlugin("bassdsd");
            TryLoadBassPlugin("basswebm");
            TryLoadBassPlugin("bassape");
            if (!OperatingSystem.IsMacOS())
            {
                TryLoadBassPlugin("bass_aac");
            }

            try
            {
                _ = BassFx.Version;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BASS_FX Load Error] {ex.Message}");
            }

            try
            {
                _ = BassLoud.Version;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BASS_LOUD Load Error] {ex.Message}");
            }

            Bass.Configure(Configuration.NetBufferLength, NetworkBufferLengthMilliseconds);
            Bass.Configure(Configuration.NetPreBuffer, NetworkPreBufferPercentage);
            Bass.Configure(Configuration.NetReadTimeOut, NetworkReadTimeoutMilliseconds);
        }
    }

    public static IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        lock (BassDeviceGate)
        {
            ConfigureBassDeviceEnumeration();

            var devices = new List<AudioOutputDevice> { AudioOutputDevice.SystemDefault };
            for (var deviceId = 1; Bass.GetDeviceInfo(deviceId, out var info); deviceId++)
            {
                if (!info.IsEnabled || info.IsLoopback || IsBassDefaultDeviceEntry(deviceId, info))
                {
                    continue;
                }

                devices.Add(new AudioOutputDevice(
                    deviceId,
                    string.IsNullOrWhiteSpace(info.Name) ? $"输出设备 {deviceId}" : info.Name,
                    info.Driver,
                    false));
            }

            return devices;
        }
    }

    public static bool IsOutputDeviceAvailable(int deviceId)
    {
        if (deviceId == Bass.DefaultDevice)
        {
            return true;
        }

        lock (BassDeviceGate)
        {
            ConfigureBassDeviceEnumeration();
            return Bass.GetDeviceInfo(deviceId, out var info) &&
                   info.IsEnabled &&
                   !info.IsLoopback &&
                   !IsBassDefaultDeviceEntry(deviceId, info);
        }
    }

    public bool IsPlaying => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Playing;

    public bool IsPaused => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Paused;

    public bool IsStopped => Stream == 0 || Bass.ChannelIsActive(Stream) == PlaybackState.Stopped;

    public bool IsStalled => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Stalled;

    internal static bool TryInitializeOutputDevice(int requestedDeviceId, out int actualDeviceId)
    {
        actualDeviceId = requestedDeviceId;
        var deviceId = ResolveOutputDeviceId(requestedDeviceId);

        if (deviceId != Bass.DefaultDevice &&
            (!Bass.GetDeviceInfo(deviceId, out var info) || !info.IsEnabled || info.IsLoopback))
        {
            Console.WriteLine($"[BASS Device Error] output device {requestedDeviceId} is not available");
            return false;
        }

        var isInitialized = deviceId != Bass.DefaultDevice &&
                            Bass.GetDeviceInfo(deviceId, out var deviceInfo) &&
                            deviceInfo.IsInitialized;
        if (!isInitialized)
        {
            if (!Bass.Init(deviceId, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                Console.WriteLine($"[BASS Init Error] device={requestedDeviceId}, error={Bass.LastError}");
                return false;
            }
        }
        else
        {
            Bass.CurrentDevice = deviceId;
        }

        actualDeviceId = Bass.CurrentDevice;
        if (requestedDeviceId == Bass.DefaultDevice)
        {
            _actualSystemDefaultDeviceId = actualDeviceId;
        }

        return actualDeviceId != Bass.DefaultDevice;
    }

    private static void ConfigureBassDeviceEnumeration()
    {
        if (_bassDeviceConfigurationApplied)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Bass.Configure(Configuration.IncludeDefaultDevice, true);
            Bass.Configure(Configuration.UnicodeDeviceInformation, true);
        }

        _bassDeviceConfigurationApplied = true;
    }

    private static bool IsBassDefaultDeviceEntry(int deviceId, DeviceInfo info)
    {
        return deviceId == 1 &&
               info.IsDefault &&
               string.Equals(info.Name, "Default", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveOutputDeviceId(int requestedDeviceId)
    {
        if (requestedDeviceId != Bass.DefaultDevice)
        {
            return requestedDeviceId;
        }

        var defaultEntryDeviceId = FindBassDefaultDeviceEntry();
        if (defaultEntryDeviceId > 0)
        {
            return defaultEntryDeviceId;
        }

        return _actualSystemDefaultDeviceId > 0 ? _actualSystemDefaultDeviceId : Bass.DefaultDevice;
    }

    private static int FindBassDefaultDeviceEntry()
    {
        for (var deviceId = 1; Bass.GetDeviceInfo(deviceId, out var info); deviceId++)
        {
            if (IsBassDefaultDeviceEntry(deviceId, info))
            {
                return deviceId;
            }
        }

        return Bass.DefaultDevice;
    }

    private static void TryLoadBassPlugin(string baseName)
    {
        var pluginName = GetBassPluginName(baseName);
        var handle = Bass.PluginLoad(pluginName);
        if (handle == 0)
        {
            Console.WriteLine($"[BASS PluginLoad Error] plugin={pluginName}, error={Bass.LastError}");
        }
    }
}
