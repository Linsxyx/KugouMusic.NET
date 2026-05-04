namespace SimpleAudio;

public sealed record TrackAnalysisSnapshot
{
    public static TrackAnalysisSnapshot Empty { get; } = new();

    public double DurationSeconds { get; init; }

    public double Bpm { get; init; }

    public double StartRms { get; init; }

    public double EndRms { get; init; }

    public double StartBrightness { get; init; }

    public double EndBrightness { get; init; }

    public double StartDynamicRms { get; init; }

    public double EndDynamicRms { get; init; }

    public double DynamicWindowSec { get; init; }

    public double IntroSilenceSec { get; init; }

    public double IntroActivityRatio { get; init; }

    public double TailSilenceSec { get; init; }

    public double InvalidTailSec { get; init; }

    public double TailActivityRatio { get; init; }

    public bool TailWindowAvailable { get; init; }

    public bool IsReliable { get; init; }
}
