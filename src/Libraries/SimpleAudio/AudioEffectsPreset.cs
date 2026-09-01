namespace SimpleAudio;

public sealed class AudioEffectsPreset
{
    public string EqPreset { get; set; } = "原声";
    public float[] EqGains { get; set; } = new float[10];
    public AdvancedAudioEffectsSettings Advanced { get; set; } = new();
}
