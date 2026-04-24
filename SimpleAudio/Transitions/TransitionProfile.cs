namespace SimpleAudio;

public sealed record TransitionProfile
{
    public static TransitionProfile Default { get; } = new();

    public double MixEntrySec { get; init; } = 4.6;

    public double MixDurationSec { get; init; } = 6.8;

    public double OverlapSec { get; init; } = 4.6;

    public double ReleaseSec { get; init; } = 2.2;

    public double MixBreathSec { get; init; } = 2.1;

    public double IncomingSettleSec { get; init; } = 5.8;

    public float OutgoingDuckStrength { get; init; } = 0.58f;

    public float IncomingGainCap { get; init; } = 0.92f;

    public float OutgoingToneDepth { get; init; } = 0.72f;

    public float IncomingToneDepth { get; init; } = 0.32f;

    public float OutgoingReverbAmount { get; init; } = 0.26f;

    public float IncomingReverbAmount { get; init; } = 0.14f;

    public float StereoWidth { get; init; } = 0.15f;

    public float Confidence { get; init; } = 0.25f;
}
