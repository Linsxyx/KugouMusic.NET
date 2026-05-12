using System.Collections.Concurrent;
using ManagedBass;
using ManagedBass.Loud;

namespace SimpleAudio;

public static class TrackVolumeNormalizer
{
    private const float TargetIntegratedLufs = -16.0f;
    private const float PeakSafetyMargin = 0.98f;
    private const float MinNormalizationGain = 0.70f;
    private const float MaxNormalizationGain = 1.35f;
    private const int MeasurementReadBufferLength = 16 * 1024;
    private static readonly ConcurrentDictionary<string, Lazy<Task<float>>> GainCache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<float> EstimateGainAsync(
        string source,
        bool isLocal,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult(1.0f);
        }

        var lazy = GainCache.GetOrAdd(source, _ => new Lazy<Task<float>>(
            () => EstimateGainInternalAsync(source, isLocal, durationSeconds, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return ResolveAsync(source, lazy);
    }

    private static async Task<float> ResolveAsync(string source, Lazy<Task<float>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            GainCache.TryRemove(source, out _);
            return 1.0f;
        }
    }

    private static Task<float> EstimateGainInternalAsync(
        string source,
        bool isLocal,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handle = CreateDecodeStream(source, isLocal, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (handle == 0)
            {
                return 1.0f;
            }

            var loudHandle = 0;
            try
            {
                var effectiveDuration = GetDurationSeconds(handle);
                if (effectiveDuration <= 0)
                {
                    effectiveDuration = durationSeconds;
                }

                loudHandle = BassLoud.Start(
                    handle,
                    BassFlags.BassLoudnessIntegrated | BassFlags.BassLoudnessTruePeak | BassFlags.BassLoudnessAutofree,
                    0);

                if (loudHandle == 0)
                {
                    return 1.0f;
                }

                DrainDecodeStream(handle, cancellationToken);

                var integratedLufs = 0f;
                if (!BassLoud.GetLevel(loudHandle, BassFlags.BassLoudnessIntegrated, ref integratedLufs))
                {
                    return 1.0f;
                }

                var gain = CalculateGainFromLufs(integratedLufs);
                var truePeakLinear = 0f;
                if (BassLoud.GetLevel(loudHandle, BassFlags.BassLoudnessTruePeak, ref truePeakLinear))
                {
                    gain = ApplyTruePeakLimit(gain, truePeakLinear);
                }

                return gain;
            }
            finally
            {
                if (loudHandle != 0)
                {
                    BassLoud.Stop(loudHandle);
                }

                Bass.StreamFree(handle);
            }
        }, cancellationToken);
    }

    private static float CalculateGainFromLufs(float integratedLufs)
    {
        if (float.IsNaN(integratedLufs) || float.IsInfinity(integratedLufs) || integratedLufs >= 0f || integratedLufs <= -120f)
        {
            return 1.0f;
        }

        var deltaDb = TargetIntegratedLufs - integratedLufs;
        var linear = MathF.Pow(10f, deltaDb / 20f);
        return Math.Clamp(linear, MinNormalizationGain, MaxNormalizationGain);
    }

    private static float ApplyTruePeakLimit(float gain, float truePeakLinear)
    {
        if (truePeakLinear <= 0f || float.IsNaN(truePeakLinear) || float.IsInfinity(truePeakLinear))
        {
            return gain;
        }

        var maxSafeGain = PeakSafetyMargin / truePeakLinear;
        return Math.Clamp(Math.Min(gain, maxSafeGain), MinNormalizationGain, MaxNormalizationGain);
    }

    private static void DrainDecodeStream(int handle, CancellationToken cancellationToken)
    {
        var buffer = new float[MeasurementReadBufferLength];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = Bass.ChannelGetData(handle, buffer, buffer.Length * sizeof(float) | (int)DataFlags.Float);
            if (bytesRead <= 0)
            {
                break;
            }
        }
    }

    private static int CreateDecodeStream(string source, bool isLocal, BassFlags flags)
    {
        if (!isLocal && source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return Bass.CreateStream(source, 0, flags, null, IntPtr.Zero);
        }

        if (File.Exists(source))
        {
            return Bass.CreateStream(source, 0, 0, flags);
        }

        return source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? Bass.CreateStream(source, 0, flags, null, IntPtr.Zero)
            : 0;
    }

    private static double GetDurationSeconds(int handle)
    {
        var length = Bass.ChannelGetLength(handle);
        return length <= 0 ? 0 : Bass.ChannelBytes2Seconds(handle, length);
    }
}
