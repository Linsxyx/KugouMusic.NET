using System;
using System.Collections.Generic;
using KugouAvaloniaPlayer.Models;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

public sealed class PlaybackVisualizerService
{
    private const int VisualizerBarCount = 96;
    private const double VisualizerMinHeight = 6;
    private const double VisualizerHeightRange = 170;

    public PlaybackVisualizerService()
    {
        Reset();
    }

    public VisualizerBandState[] Bars { get; } = new VisualizerBandState[VisualizerBarCount];

    public event Action? Updated;

    public void Reset()
    {
        for (var i = 0; i < Bars.Length; i++)
        {
            Bars[i].Height = VisualizerMinHeight;
            Bars[i].Opacity = 0.1;
        }

        Updated?.Invoke();
    }

    public void Update(AudioAnalysisSnapshot snapshot)
    {
        var spectrumBands = snapshot.SpectrumBands;
        if (spectrumBands == null || spectrumBands.Count == 0)
        {
            Reset();
            return;
        }

        var energyBoost = Math.Clamp(snapshot.Rms * 8.5, 0d, 1d);
        var brightnessBoost = Math.Clamp(snapshot.Brightness * 1.25, 0d, 1d);
        var barCount = Bars.Length;

        for (var i = 0; i < barCount; i++)
        {
            var phase = barCount <= 1 ? 0d : i / (barCount - 1d);
            var band = SampleSpectrumBand(spectrumBands, phase);
            var shapedBand = Math.Pow(Math.Clamp(band, 0d, 1d), 0.72d);
            var centerLift = 0.82d + Math.Sin(phase * Math.PI) * 0.12d;
            var ripple = 1d + Math.Sin(snapshot.PositionSeconds * 4.8d + i * 0.18d) * energyBoost * 0.035d;
            var target = Math.Clamp(
                (shapedBand * 0.58d + energyBoost * 0.14d + brightnessBoost * 0.04d) * centerLift * ripple,
                0d,
                1d);
            var targetHeight = VisualizerMinHeight + target * VisualizerHeightRange;

            ref var bar = ref Bars[i];
            var smoothing = targetHeight >= bar.Height ? 0.46d : 0.16d;
            bar.Height += (targetHeight - bar.Height) * smoothing;
            bar.Opacity = Math.Clamp(0.1d + Math.Pow(target, 0.9d) * 0.5d, 0.1d, 0.6d);
        }

        Updated?.Invoke();
    }

    private static double SampleSpectrumBand(IReadOnlyList<float> spectrumBands, double phase)
    {
        if (spectrumBands.Count == 1)
            return spectrumBands[0];

        var position = Math.Clamp(phase, 0d, 1d) * (spectrumBands.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = Math.Min(lowerIndex + 1, spectrumBands.Count - 1);
        var mix = position - lowerIndex;
        var lower = Math.Clamp(spectrumBands[lowerIndex], 0f, 1f);
        var upper = Math.Clamp(spectrumBands[upperIndex], 0f, 1f);

        return lower + (upper - lower) * mix;
    }
}
