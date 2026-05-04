using ManagedBass;
using ManagedBass.Fx;

namespace SimpleAudio;

public partial class SimpleAudioPlayer
{
    private const float DefaultHighShelfCenterHz = 4800f;
    private const float DefaultLowPassCutoffHz = 18000f;
    private static readonly float[] EQFreqs = [141f, 234f, 469f, 844f, 1300f, 2200f, 3700f, 5800f, 9000f, 13800f];

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

    public SimpleAudioPlayer()
    {
        _stereoDspProc = StereoEnhancerDSP;
    }

    public event Action? PlaybackEnded;

    public static void Initialize()
    {
        if (Bass.CurrentDevice == -1)
        {
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                Console.WriteLine($"[BASS Init Error] {Bass.LastError}");
            }
        }

        Bass.PluginLoad(GetBassPluginName("bassflac"));
        if (!OperatingSystem.IsMacOS())
        {
            Bass.PluginLoad(GetBassPluginName("bass_aac"));
        }

        try
        {
            _ = BassFx.Version;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS_FX Load Error] {ex.Message}");
        }

        Bass.Configure(Configuration.NetBufferLength, 5000);
        Bass.Configure(Configuration.NetPreBuffer, 20);
        Bass.Configure(Configuration.NetReadTimeOut, 10000);
    }

    public bool IsPlaying => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Playing;

    public bool IsPaused => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Paused;

    public bool IsStopped => Stream == 0 || Bass.ChannelIsActive(Stream) == PlaybackState.Stopped;

    public bool IsStalled => Stream != 0 && Bass.ChannelIsActive(Stream) == PlaybackState.Stalled;
}
