using ManagedBass;

namespace SimpleAudio;

public partial class SimpleAudioPlayer
{
    private sealed class PlayerRuntimeState
    {
        public float ChorusMix { get; set; }
        public int ChorusHandle { get; set; }
        public float[] CurrentEQ { get; set; } = new float[10];
        public float[] DspBuffer { get; set; } = Array.Empty<float>();
        public float EchoMix { get; set; }
        public int EchoHandle { get; set; }
        public SyncProcedure? EndSyncProc { get; set; }
        public int HighShelfHandle { get; set; }
        public int LowPassHandle { get; set; }
        public int LimiterDspHandle { get; set; }
        public int PeakEqHandle { get; set; }
        public float PlaybackSpeed { get; set; } = 1.0f;
        public float[] RealtimeFftBuffer { get; } = new float[1024];
        public float[] RealtimeSpectrumBands { get; } = new float[RealtimeSpectrumBandCount];
        public float TransitionGain { get; set; } = 1.0f;
        public float TransitionToneDepth { get; set; }
        public float ReverbAmount { get; set; }
        public float ReverbTimeMs { get; set; } = 1500f;
        public int ReverbHandle { get; set; }
        public float StereoWidth { get; set; }
        public int StereoDspHandle { get; set; }
        public int Stream { get; set; }
        public int SourceStream { get; set; }
        public IDisposable? StreamSourceOwner { get; set; }
        public AudioChannelMode ChannelMode { get; set; } = AudioChannelMode.Auto;
        public AudioChannelLayout? SourceChannelLayout { get; set; }
        public AudioChannelLayout? OutputChannelLayout { get; set; }
        public int SourceChannelCount { get; set; }
        public int OutputChannelCount { get; set; }
        public string SourceChannelLayoutName { get; set; } = string.Empty;
        public string OutputChannelLayoutName { get; set; } = string.Empty;
        public string? ReloadableSourcePath { get; set; }
        public bool SurroundEnabled { get; set; }
        public float VolumeNormalizationGain { get; set; } = 1.0f;
        public float UserVolume { get; set; } = 1.0f;
    }
}
