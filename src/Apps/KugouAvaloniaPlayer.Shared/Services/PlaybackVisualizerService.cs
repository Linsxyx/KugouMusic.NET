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
            var phase = barCount <= 1 ? 0f : (float)i / (barCount - 1);
            var band = SampleSpectrumBand(spectrumBands, phase);
            var shapedBand = MathF.Pow((float)Math.Clamp(band, 0d, 1d), 0.72f);
            var centerLift = 0.82f + MathF.Sin(phase * MathF.PI) * 0.12f;
            var ripple = 1f + MathF.Sin((float)snapshot.PositionSeconds * 4.8f + i * 0.18f) * (float)energyBoost * 0.035f;
            var target = Math.Clamp(
                (shapedBand * 0.58f + (float)energyBoost * 0.14f + (float)brightnessBoost * 0.04f) * centerLift * ripple,
                0f,
                1f);
            var targetHeight = (float)(VisualizerMinHeight + target * VisualizerHeightRange);

            ref var bar = ref Bars[i];
            var smoothing = targetHeight >= bar.Height ? 0.46f : 0.16f;
            bar.Height += (targetHeight - bar.Height) * smoothing;
            bar.Opacity = Math.Clamp(0.1f + MathF.Pow(target, 0.9f) * 0.5f, 0.1f, 0.6f);
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
