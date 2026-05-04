namespace SimpleAudio;

public readonly record struct TailPlaybackMetrics
{
    public double TailRms { get; init; }

    public double TailBrightness { get; init; }

    public double TailSilenceSec { get; init; }
}
