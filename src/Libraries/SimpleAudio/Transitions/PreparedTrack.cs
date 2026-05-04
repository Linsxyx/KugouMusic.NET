namespace SimpleAudio;

public sealed record PreparedTrack
{
    public required string Id { get; init; }

    public required string Source { get; init; }

    public bool IsLocal { get; init; }

    public double DurationSeconds { get; init; }

    public TrackAnalysisSnapshot? CachedAnalysis { get; init; }

    public TailPlaybackMetrics? TailMetrics { get; init; }
}
