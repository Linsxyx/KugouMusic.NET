namespace SimpleAudio;

public readonly record struct AudioAnalysisSnapshot
{
    public static AudioAnalysisSnapshot Empty => new();

    public double PositionSeconds { get; init; }

    public double DurationSeconds { get; init; }

    public double Rms { get; init; }

    public double Brightness { get; init; }

    public double SpectralCentroid { get; init; }

    public IReadOnlyList<float>? SpectrumBands { get; init; }
}
