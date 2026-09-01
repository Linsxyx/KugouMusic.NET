namespace SimpleAudio;

/// <summary>Persisted, normalized controls for the optional BASS FX chain.</summary>
public sealed class AdvancedAudioEffectsSettings
{
    public bool StereoEnabled { get; set; }
    public float StereoWidth { get; set; } = 0.22f;
    public float StereoOutputGain { get; set; } = 1f;
    public bool ReverbEnabled { get; set; }
    public float ReverbAmount { get; set; } = 0.34f;
    public float ReverbTimeMs { get; set; } = 1500f;
    public float ReverbDryMix { get; set; } = 1f;
    public bool EchoEnabled { get; set; }
    public float EchoMix { get; set; } = 0.24f;
    public float EchoFeedback { get; set; } = 0.25f;
    public float EchoDelayMs { get; set; } = 180f;
    public bool ChorusEnabled { get; set; }
    public float ChorusMix { get; set; } = 0.07f;
    public float ChorusDepth { get; set; } = 4f;
    public float ChorusRate { get; set; } = 0.2f;
    public bool CompressorEnabled { get; set; }
    public float CompressorThreshold { get; set; } = -15f;
    public float CompressorRatio { get; set; } = 3f;
    public float CompressorAttackMs { get; set; } = 20f;
    public float CompressorReleaseMs { get; set; } = 200f;
    public bool DistortionEnabled { get; set; }
    public float DistortionDrive { get; set; } = 0.1f;
    public float DistortionMix { get; set; } = 0.1f;
    public bool BqfEnabled { get; set; }
    public float BqfCenterHz { get; set; } = 1000f;
    public float BqfGainDb { get; set; }
    public float BqfQ { get; set; } = 0.7f;
    public bool FlangerEnabled { get; set; }
    public float FlangerMix { get; set; } = 0.15f;
    public float FlangerDepth { get; set; } = 20f;
    public float FlangerRate { get; set; } = 0.5f;
    public bool PhaserEnabled { get; set; }
    public float PhaserMix { get; set; } = 0.15f;
    public float PhaserRate { get; set; } = 0.5f;
    public float PhaserRange { get; set; } = 2f;
    public float PhaserFrequency { get; set; } = 800f;
    public bool GargleEnabled { get; set; }
    public int GargleRateHz { get; set; } = 500;
    public bool AutoWahEnabled { get; set; }
    public float AutoWahMix { get; set; } = 0.2f;
    public float AutoWahRate { get; set; } = 2f;
    public float AutoWahRange { get; set; } = 4.3f;
    public float AutoWahFrequency { get; set; } = 500f;
    public bool DampEnabled { get; set; }
    public float DampTarget { get; set; } = 1f;
    public float DampQuiet { get; set; }
    public float DampRate { get; set; } = 1f;
    public float DampGain { get; set; } = 1f;
    public float DampDelay { get; set; }

    public AdvancedAudioEffectsSettings Clone() => (AdvancedAudioEffectsSettings)MemberwiseClone();
}
