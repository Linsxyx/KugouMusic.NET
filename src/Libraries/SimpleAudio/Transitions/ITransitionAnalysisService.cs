namespace SimpleAudio;

public interface ITransitionAnalysisService
{
    Task<TransitionProfile> AnalyzeAsync(PreparedTrack current, PreparedTrack next, CancellationToken ct = default);
}
