using System.Buffers;
using System.Collections.Concurrent;
using ManagedBass;
using ManagedBass.Loud;
using SimpleAudio.Codecs;

namespace SimpleAudio;

public static class TrackVolumeNormalizer
{
    private const float TargetIntegratedLufs = -16.0f;
    private const float TargetTruePeakDbtp = -1.0f;
    internal const float MinNormalizationGain = 0f;
    internal const float MaxNormalizationGain = 4.0f;
    private const int MeasurementReadBufferLength = 16 * 1024;
    private const int MaxGainCacheEntries = 128;
    private static readonly float MaxTruePeakLinear = MathF.Pow(10f, TargetTruePeakDbtp / 20f);
    private static readonly ConcurrentDictionary<string, Lazy<Task<float>>> GainCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCachedGain(string source, bool isLocal, out float gain)
    {
        gain = 1.0f;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (!isLocal && RemoteAudioCache.IsKnownLiveStream(source))
        {
            return true;
        }

        var cacheKey = PersistentAudioAnalysisCache.GetNormalizationGainMemoryKey(source, isLocal)
                       ?? source;
        if (GainCache.TryGetValue(cacheKey, out var lazy) &&
            lazy.IsValueCreated &&
            lazy.Value.IsCompletedSuccessfully)
        {
            gain = lazy.Value.Result;
            return true;
        }

        return PersistentAudioAnalysisCache.TryLoadNormalizationGain(source, isLocal, out gain);
    }

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

        if (!isLocal && RemoteAudioCache.IsKnownLiveStream(source))
        {
            return Task.FromResult(1.0f);
        }

        var cacheKey = PersistentAudioAnalysisCache.GetNormalizationGainMemoryKey(source, isLocal)
                       ?? source;
        if (GainCache.Count > MaxGainCacheEntries)
        {
            GainCache.Clear();
        }

        var lazy = GainCache.GetOrAdd(cacheKey, _ => new Lazy<Task<float>>(
            () => EstimateGainInternalAsync(source, isLocal, durationSeconds, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return ResolveAsync(cacheKey, lazy);
    }

    private static async Task<float> ResolveAsync(string cacheKey, Lazy<Task<float>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            GainCache.TryRemove(cacheKey, out _);
            return 1.0f;
        }
    }

    private static async Task<float> EstimateGainInternalAsync(
        string source,
        bool isLocal,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isLocal && RemoteAudioCache.IsKnownLiveStream(source))
        {
            return 1.0f;
        }

        if (PersistentAudioAnalysisCache.TryLoadNormalizationGain(source, isLocal, out var cachedGain))
        {
            return cachedGain;
        }

        var effectiveSource = source;
        var effectiveIsLocal = isLocal;
        if (!effectiveIsLocal && RemoteAudioCache.IsHttpUrl(effectiveSource))
        {
            var resolved = await RemoteAudioCache
                .ResolveCompletedFileAsync(effectiveSource, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            {
                return 1.0f;
            }

            effectiveSource = resolved;
            effectiveIsLocal = true;
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var opened = BassStreamOpener.OpenForAnalysis(
                effectiveSource,
                effectiveIsLocal,
                BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (!opened.IsSuccess)
            {
                return 1.0f;
            }

            var handle = opened.Handle;
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

                PersistentAudioAnalysisCache.SaveNormalizationGain(source, isLocal, gain);
                return gain;
            }
            finally
            {
                if (loudHandle != 0)
                {
                    BassLoud.Stop(loudHandle);
                }
            }
        }, cancellationToken).ConfigureAwait(false);
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

        var maxSafeGain = MaxTruePeakLinear / truePeakLinear;
        return Math.Clamp(Math.Min(gain, maxSafeGain), 0f, MaxNormalizationGain);
    }

    private static void DrainDecodeStream(int handle, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<float>.Shared.Rent(MeasurementReadBufferLength);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = Bass.ChannelGetData(handle, buffer, MeasurementReadBufferLength * sizeof(float) | (int)DataFlags.Float);
                if (bytesRead <= 0)
                {
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private static double GetDurationSeconds(int handle)
    {
        var length = Bass.ChannelGetLength(handle);
        return length <= 0 ? 0 : Bass.ChannelBytes2Seconds(handle, length);
    }
}
