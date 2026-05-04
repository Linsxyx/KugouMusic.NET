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
        public int PeakEqHandle { get; set; }
        public float TransitionGain { get; set; } = 1.0f;
        public float TransitionToneDepth { get; set; }
        public float ReverbAmount { get; set; }
        public float ReverbTimeMs { get; set; } = 1500f;
        public int ReverbHandle { get; set; }
        public float StereoWidth { get; set; }
        public int StereoDspHandle { get; set; }
        public int Stream { get; set; }
        public bool SurroundEnabled { get; set; }
        public float UserVolume { get; set; } = 1.0f;
    }
}
