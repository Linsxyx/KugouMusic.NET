using System.Collections.Concurrent;
using ManagedBass;
using ManagedBass.Fx;

namespace SimpleAudio;

public sealed class ManagedBassTransitionAnalysisService : ITransitionAnalysisService
{
    private const double BpmScanWindowSec = 18.0;
    private const double IntroWindowSec = 10.0;
    private const double OutroWindowSec = 12.0;
    private const double TailScanWindowSec = 40.0;
    private readonly ConcurrentDictionary<string, Lazy<Task<TrackAnalysisSnapshot>>> _analysisCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<TransitionProfile> AnalyzeAsync(PreparedTrack current, PreparedTrack next, CancellationToken ct = default)
    {
        var currentAnalysisTask = GetTrackAnalysisAsync(current, ct);
        var nextAnalysisTask = GetTrackAnalysisAsync(next, ct);
        await Task.WhenAll(currentAnalysisTask, nextAnalysisTask);

        var currentAnalysis = MergeCurrentTrackMetrics(await currentAnalysisTask, current.TailMetrics);
        var nextAnalysis = await nextAnalysisTask;

        var currentTailRms = Math.Max(0.0001, currentAnalysis.EndDynamicRms > 0 ? currentAnalysis.EndDynamicRms : currentAnalysis.EndRms);
        var nextIntroRms = Math.Max(0.0001, nextAnalysis.StartDynamicRms > 0 ? nextAnalysis.StartDynamicRms : nextAnalysis.StartRms);
        var loudnessRatio = nextIntroRms / currentTailRms;
        var loudnessDiffDb = 20.0 * Math.Log10(loudnessRatio);
        var loudnessDifficulty = Math.Clamp(Math.Abs(loudnessDiffDb) / 9.0, 0.0, 1.0);

        var tailSilenceSec = Math.Max(0, currentAnalysis.TailSilenceSec);
        var invalidTailSec = Math.Max(0, currentAnalysis.InvalidTailSec);
        var tailWeakness = Math.Clamp((0.035 - currentTailRms) / 0.035, 0.0, 1.0);
        var introStrength = Math.Clamp(nextIntroRms / 0.09, 0.0, 1.0);
        var introBrightness = Math.Clamp(nextAnalysis.StartBrightness / 0.45, 0.0, 1.0);
        var introSilenceSec = Math.Max(0, nextAnalysis.IntroSilenceSec);
        var introBlankness = Math.Clamp(introSilenceSec / 4.5, 0.0, 1.0);
        var introActivityProtect = Math.Clamp(nextAnalysis.IntroActivityRatio, 0.0, 1.0);
        var tailActivity = Math.Clamp(currentAnalysis.TailActivityRatio, 0.0, 1.0);
        tailWeakness = Math.Max(tailWeakness, Math.Clamp((0.58 - tailActivity) / 0.58, 0.0, 1.0));

        var bpmDifficulty = EstimateBpmDifficulty(currentAnalysis.Bpm, nextAnalysis.Bpm);

        var mixSpanSeed = Clamp(
            8.15
            + bpmDifficulty * 1.20
            + loudnessDifficulty * 0.85
            + tailWeakness * 0.70
            + introBlankness * 0.65
            - introStrength * 0.75
            - introActivityProtect * 0.45,
            6.8,
            12.8);
        var mixEntrySec = Clamp(4.0 + invalidTailSec * 0.92 + tailSilenceSec * 0.35 + introSilenceSec * 0.92 + tailWeakness * 1.1 + bpmDifficulty * 0.35 - introStrength * 0.95 - introBrightness * 0.45 - introActivityProtect * 0.35, 2.9, 14.0);
        var overlapSec = Clamp(
            3.25
            + bpmDifficulty * 0.35
            + loudnessDifficulty * 0.35
            + tailWeakness * 0.55
            + introBlankness * 0.40
            - introStrength * 0.55
            - introActivityProtect * 0.20,
            2.4,
            Math.Max(2.4, Math.Min(mixEntrySec, 7.2)));
        var releaseSec = Clamp(
            mixSpanSeed - overlapSec,
            1.45,
            3.8);
        var mixDurationSec = overlapSec + releaseSec;
        var mixBreathSec = Clamp(
            1.25
            + introStrength * 0.42
            + introBrightness * 0.22
            + loudnessDifficulty * 0.16
            + introBlankness * 0.22,
            0.95,
            Math.Max(1.05, overlapSec * 0.72));
        var settleFloor = Math.Max(mixBreathSec + 0.9, overlapSec + releaseSec * 0.25);
        var settleCeiling = Math.Max(settleFloor + 0.45, mixDurationSec - 0.2);
        var incomingSettleSec = Clamp(
            mixDurationSec * (
                0.66
                + introBlankness * 0.16
                + loudnessDifficulty * 0.06
                - introStrength * 0.15
                - introActivityProtect * 0.10
                - introBrightness * 0.08),
            settleFloor,
            settleCeiling);

        var outgoingDuck = (float)Clamp(0.44 + loudnessDifficulty * 0.38 + introStrength * 0.10, 0.40, 0.92);
        var incomingGainCap = (float)Clamp(0.99 - introStrength * 0.08 - introBrightness * 0.06 - introBlankness * 0.07 - Math.Max(0, loudnessDiffDb) / 28.0, 0.74, 0.98);
        var outgoingToneDepth = (float)Clamp(0.42 + tailWeakness * 0.25 + loudnessDifficulty * 0.18, 0.30, 0.88);
        var incomingToneDepth = (float)Clamp(0.22 + introBrightness * 0.16 + introBlankness * 0.10 + Math.Max(0, loudnessDiffDb) / 20.0, 0.14, 0.52);
        var outgoingReverb = (float)Clamp(0.14 + loudnessDifficulty * 0.10 + tailWeakness * 0.08, 0.08, 0.32);
        var incomingReverb = (float)Clamp(0.08 + introBrightness * 0.05 + introBlankness * 0.04 + loudnessDifficulty * 0.03, 0.04, 0.22);
        var stereoWidth = (float)Clamp(0.10 + bpmDifficulty * 0.05 + introBrightness * 0.05, 0.07, 0.22);

        var confidence = 0.2f;
        if (currentAnalysis.IsReliable) confidence += 0.28f;
        if (nextAnalysis.IsReliable) confidence += 0.28f;
        if (current.TailMetrics is { } metrics && (metrics.TailRms > 0 || metrics.TailSilenceSec > 0)) confidence += 0.18f;
        confidence = Math.Clamp(confidence, 0.15f, 0.95f);

        var profile = new TransitionProfile
        {
            MixEntrySec = mixEntrySec,
            MixDurationSec = mixDurationSec,
            OverlapSec = overlapSec,
            ReleaseSec = releaseSec,
            MixBreathSec = mixBreathSec,
            IncomingSettleSec = incomingSettleSec,
            OutgoingDuckStrength = outgoingDuck,
            IncomingGainCap = incomingGainCap,
            OutgoingToneDepth = outgoingToneDepth,
            IncomingToneDepth = incomingToneDepth,
            OutgoingReverbAmount = outgoingReverb,
            IncomingReverbAmount = incomingReverb,
            StereoWidth = stereoWidth,
            Confidence = confidence
        };

        return ShapeProfileForConfidence(profile);
    }

    private static double EstimateBpmDifficulty(double currentBpm, double nextBpm)
    {
        if (currentBpm <= 0 || nextBpm <= 0)
        {
            return 0;
        }

        var bestDiff = Math.Abs(currentBpm - nextBpm);
        var candidates = new[] { nextBpm * 0.5, nextBpm, nextBpm * 2.0 };
        foreach (var candidate in candidates)
        {
            if (candidate > 0)
            {
                bestDiff = Math.Min(bestDiff, Math.Abs(currentBpm - candidate));
            }
        }

        return Math.Clamp(bestDiff / 42.0, 0.0, 1.0);
    }

    private static TransitionProfile ShapeProfileForConfidence(TransitionProfile profile)
    {
        var trust = Math.Clamp((profile.Confidence - 0.22f) / 0.68f, 0f, 1f);
        var conservative = TransitionProfile.Default with
        {
            OutgoingDuckStrength = 0.48f,
            IncomingGainCap = 0.95f,
            OutgoingToneDepth = 0.38f,
            IncomingToneDepth = 0.16f,
            OutgoingReverbAmount = 0.06f,
            IncomingReverbAmount = 0.03f,
            StereoWidth = 0.05f,
            Confidence = profile.Confidence
        };

        return profile with
        {
            MixEntrySec = Lerp(conservative.MixEntrySec, profile.MixEntrySec, trust),
            MixDurationSec = Lerp(conservative.MixDurationSec, profile.MixDurationSec, trust),
            OverlapSec = Lerp(conservative.OverlapSec, profile.OverlapSec, trust),
            ReleaseSec = Lerp(conservative.ReleaseSec, profile.ReleaseSec, trust),
            MixBreathSec = Lerp(conservative.MixBreathSec, profile.MixBreathSec, trust),
            IncomingSettleSec = Lerp(conservative.IncomingSettleSec, profile.IncomingSettleSec, trust),
            OutgoingDuckStrength = Lerp(conservative.OutgoingDuckStrength, profile.OutgoingDuckStrength, trust),
            IncomingGainCap = Lerp(conservative.IncomingGainCap, profile.IncomingGainCap, trust),
            OutgoingToneDepth = Lerp(conservative.OutgoingToneDepth, profile.OutgoingToneDepth, trust),
            IncomingToneDepth = Lerp(conservative.IncomingToneDepth, profile.IncomingToneDepth, trust),
            OutgoingReverbAmount = Lerp(conservative.OutgoingReverbAmount, profile.OutgoingReverbAmount, trust),
            IncomingReverbAmount = Lerp(conservative.IncomingReverbAmount, profile.IncomingReverbAmount, trust),
            StereoWidth = Lerp(conservative.StereoWidth, profile.StereoWidth, trust)
        };
    }

    private async Task<TrackAnalysisSnapshot> GetTrackAnalysisAsync(PreparedTrack track, CancellationToken ct)
    {
        if (track.CachedAnalysis != null && track.CachedAnalysis.IsReliable)
        {
            return track.CachedAnalysis;
        }

        if (string.IsNullOrWhiteSpace(track.Source))
        {
            return TrackAnalysisSnapshot.Empty;
        }

        var lazy = _analysisCache.GetOrAdd(track.Source, _ => new Lazy<Task<TrackAnalysisSnapshot>>(
            () => AnalyzeTrackInternalAsync(track, ct), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            _analysisCache.TryRemove(track.Source, out _);
            return TrackAnalysisSnapshot.Empty;
        }
    }

    private async Task<TrackAnalysisSnapshot> AnalyzeTrackInternalAsync(PreparedTrack track, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var flags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;
            var handle = CreateDecodeStream(track.Source, track.IsLocal, flags);
            if (handle == 0)
            {
                return TrackAnalysisSnapshot.Empty;
            }

            try
            {
                Bass.ChannelGetInfo(handle, out var info);
                var sampleRate = info.Frequency > 0 ? info.Frequency : 44100;
                var channels = Math.Max(1, info.Channels);
                var duration = GetDurationSeconds(handle);
                var effectiveDuration = duration > 0 ? duration : track.DurationSeconds;

                var bpm = EstimateBpm(handle, effectiveDuration);
                var startRms = ReadWindowRms(handle, 0, Math.Min(IntroWindowSec, effectiveDuration), sampleRate, channels);
                var introWindowSec = Math.Min(IntroWindowSec, effectiveDuration);
                var startBrightness = ReadWindowBrightness(handle, 0, introWindowSec, sampleRate);
                var introSilence = EstimateEdgeSilence(handle, 0, introWindowSec, sampleRate, channels, scanFromTail: false, out var introAvailable, out var introActivityRatio);

                var tailStart = Math.Max(0, effectiveDuration - OutroWindowSec);
                var tailWindowSec = Math.Min(OutroWindowSec, effectiveDuration);
                var endRms = ReadWindowRms(handle, tailStart, tailWindowSec, sampleRate, channels);
                var endBrightness = ReadWindowBrightness(handle, tailStart, tailWindowSec, sampleRate);
                var tailSilence = EstimateEdgeSilence(handle, tailStart, tailWindowSec, sampleRate, channels, scanFromTail: true, out var tailAvailable, out var tailActivityRatio);
                var tailScanStart = Math.Max(0, effectiveDuration - TailScanWindowSec);
                var tailScanSec = Math.Min(TailScanWindowSec, effectiveDuration);
                var invalidTailSec = EstimateInvalidTail(handle, tailScanStart, tailScanSec, sampleRate, channels);
                var dynamicWindowSec = Clamp(10.0 + invalidTailSec * 0.55, 8.0, 24.0);
                var startDynamicRms = ReadWindowRms(handle, 0, Math.Min(dynamicWindowSec, effectiveDuration), sampleRate, channels);
                var endDynamicSourceSec = Math.Max(0, tailScanSec - invalidTailSec);
                var endDynamicWindowSec = Math.Min(dynamicWindowSec, Math.Max(endDynamicSourceSec, Math.Min(OutroWindowSec, effectiveDuration)));
                var endDynamicStart = Math.Max(0, effectiveDuration - invalidTailSec - endDynamicWindowSec);
                var endDynamicRms = ReadWindowRms(handle, endDynamicStart, endDynamicWindowSec, sampleRate, channels);

                return new TrackAnalysisSnapshot
                {
                    DurationSeconds = effectiveDuration,
                    Bpm = bpm,
                    StartRms = startRms,
                    EndRms = endRms,
                    StartBrightness = startBrightness,
                    EndBrightness = endBrightness,
                    StartDynamicRms = startDynamicRms,
                    EndDynamicRms = endDynamicRms,
                    DynamicWindowSec = dynamicWindowSec,
                    IntroSilenceSec = introSilence,
                    IntroActivityRatio = introActivityRatio,
                    TailSilenceSec = tailSilence,
                    InvalidTailSec = invalidTailSec,
                    TailActivityRatio = tailActivityRatio,
                    TailWindowAvailable = tailAvailable,
                    IsReliable = startRms > 0 || bpm > 0 || tailAvailable || introAvailable
                };
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }, ct).ConfigureAwait(false);
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

    private static double EstimateBpm(int handle, double durationSec)
    {
        var endSec = durationSec > 0 ? Math.Min(durationSec, BpmScanWindowSec) : BpmScanWindowSec;
        var bpm = BassFx.BPMDecodeGet(handle, 0, endSec, 0, BassFlags.Default, null, IntPtr.Zero);
        return bpm > 0 ? bpm : 0;
    }

    private static double ReadWindowRms(int handle, double startSec, double windowSec, int sampleRate, int channels)
    {
        if (windowSec <= 0)
        {
            return 0;
        }

        if (!TrySeekSeconds(handle, startSec))
        {
            return 0;
        }

        var samplesPerChannel = Math.Max(1024, (int)Math.Ceiling(windowSec * sampleRate));
        var buffer = new float[samplesPerChannel * channels];
        var requestedBytes = buffer.Length * sizeof(float);
        var bytesRead = Bass.ChannelGetData(handle, buffer, requestedBytes | (int)DataFlags.Float);
        if (bytesRead <= 0)
        {
            return 0;
        }

        var sampleCount = Math.Min(buffer.Length, bytesRead / sizeof(float));
        if (sampleCount <= 0)
        {
            return 0;
        }

        double sumSquares = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            var value = buffer[i];
            sumSquares += value * value;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }

    private static double ReadWindowBrightness(int handle, double startSec, double windowSec, int sampleRate)
    {
        if (windowSec <= 0)
        {
            return 0;
        }

        var probeCount = windowSec >= 4.0 ? 5 : windowSec >= 1.5 ? 3 : 1;
        var values = new double[probeCount];
        var valueCount = 0;
        var maxOffset = Math.Max(0, windowSec - 0.08);
        for (var i = 0; i < probeCount; i++)
        {
            var offset = probeCount == 1 ? 0 : maxOffset * i / (probeCount - 1);
            var brightness = ReadPointBrightness(handle, startSec + offset, sampleRate);
            if (brightness > 0)
            {
                values[valueCount++] = brightness;
            }
        }

        if (valueCount == 0)
        {
            return 0;
        }

        if (valueCount == values.Length)
        {
            return Percentile(values, 0.5);
        }

        var compact = new double[valueCount];
        Array.Copy(values, compact, valueCount);
        return Percentile(compact, 0.5);
    }

    private static double ReadPointBrightness(int handle, double startSec, int sampleRate)
    {
        if (!TrySeekSeconds(handle, startSec))
        {
            return 0;
        }

        var fft = new float[1024];
        var bytesRead = Bass.ChannelGetData(handle, fft, (int)(DataFlags.FFT2048 | DataFlags.FFTNoWindow | DataFlags.FFTRemoveDC));
        if (bytesRead <= 0)
        {
            return 0;
        }

        double totalEnergy = 0;
        double highEnergy = 0;
        for (var i = 1; i < fft.Length; i++)
        {
            var value = Math.Max(0f, fft[i]);
            var energy = (double)value * value;
            totalEnergy += energy;
            var freq = i * sampleRate / 2048.0;
            if (freq >= 3200)
            {
                highEnergy += energy;
            }
        }

        return totalEnergy > 0 ? highEnergy / totalEnergy : 0;
    }

    private static double EstimateEdgeSilence(
        int handle,
        double startSec,
        double windowSec,
        int sampleRate,
        int channels,
        bool scanFromTail,
        out bool edgeAvailable,
        out double activityRatio)
    {
        edgeAvailable = false;
        activityRatio = 1.0;
        if (windowSec <= 0 || !TrySeekSeconds(handle, startSec))
        {
            return 0;
        }

        var samplesPerChannel = Math.Max(1024, (int)Math.Ceiling(windowSec * sampleRate));
        var buffer = new float[samplesPerChannel * channels];
        var requestedBytes = buffer.Length * sizeof(float);
        var bytesRead = Bass.ChannelGetData(handle, buffer, requestedBytes | (int)DataFlags.Float);
        if (bytesRead <= 0)
        {
            return 0;
        }

        var sampleCount = Math.Min(buffer.Length, bytesRead / sizeof(float));
        if (sampleCount <= 0)
        {
            return 0;
        }

        edgeAvailable = true;
        var frameSize = Math.Max(256, channels * 512);
        var frameCount = Math.Max(1, sampleCount / frameSize);
        var frameRms = new double[frameCount];
        var peak = 0d;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var start = frameIndex * frameSize;
            var end = Math.Min(sampleCount, start + frameSize);
            double sumSquares = 0;
            for (var i = start; i < end; i++)
            {
                var sample = buffer[i];
                sumSquares += sample * sample;
            }

            var rms = Math.Sqrt(sumSquares / Math.Max(1, end - start));
            frameRms[frameIndex] = rms;
            if (rms > peak)
            {
                peak = rms;
            }
        }

        if (peak <= 0)
        {
            activityRatio = 0;
            return windowSec;
        }

        var threshold = Math.Max(peak * 0.18, 0.0025);
        var activeFrames = 0;
        var firstActiveFrame = -1;
        var lastActiveFrame = -1;
        for (var i = 0; i < frameRms.Length; i++)
        {
            if (frameRms[i] >= threshold)
            {
                activeFrames++;
                if (firstActiveFrame < 0)
                {
                    firstActiveFrame = i;
                }
                lastActiveFrame = i;
            }
        }

        activityRatio = Math.Clamp((double)activeFrames / frameRms.Length, 0.0, 1.0);

        var edgeFrameIndex = scanFromTail ? lastActiveFrame : firstActiveFrame;
        if (edgeFrameIndex < 0)
        {
            activityRatio = 0;
            return windowSec;
        }

        var frameDuration = windowSec / frameRms.Length;
        return scanFromTail
            ? Math.Max(0, windowSec - (edgeFrameIndex + 1) * frameDuration)
            : Math.Max(0, edgeFrameIndex * frameDuration);
    }

    private static double EstimateInvalidTail(int handle, double startSec, double windowSec, int sampleRate, int channels)
    {
        if (windowSec <= 0 || !TrySeekSeconds(handle, startSec))
        {
            return 0;
        }

        var samplesPerChannel = Math.Max(1024, (int)Math.Ceiling(windowSec * sampleRate));
        var buffer = new float[samplesPerChannel * channels];
        var requestedBytes = buffer.Length * sizeof(float);
        var bytesRead = Bass.ChannelGetData(handle, buffer, requestedBytes | (int)DataFlags.Float);
        if (bytesRead <= 0)
        {
            return 0;
        }

        var sampleCount = Math.Min(buffer.Length, bytesRead / sizeof(float));
        if (sampleCount <= 0)
        {
            return 0;
        }

        var frameSize = Math.Max(256, channels * 512);
        var frameCount = Math.Max(1, sampleCount / frameSize);
        var rms = new double[frameCount];
        var peak = 0d;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var start = frameIndex * frameSize;
            var end = Math.Min(sampleCount, start + frameSize);
            double sumSquares = 0;
            for (var i = start; i < end; i++)
            {
                var sample = buffer[i];
                sumSquares += sample * sample;
            }

            var value = Math.Sqrt(sumSquares / Math.Max(1, end - start));
            rms[frameIndex] = value;
            if (value > peak)
            {
                peak = value;
            }
        }

        if (peak <= 1e-7)
        {
            return windowSec;
        }

        var smoothed = SmoothMovingAverage(rms, 5);
        var floor = Percentile(smoothed, 0.40);
        var threshold = Math.Max(Math.Max(peak * 0.065, floor * 0.62), 4e-5);
        var lastActiveFrame = -1;
        for (var i = 0; i < smoothed.Length; i++)
        {
            if (smoothed[i] >= threshold)
            {
                lastActiveFrame = i;
            }
        }

        var invalidTail = lastActiveFrame < 0
            ? windowSec
            : Math.Max(0, windowSec - (lastActiveFrame + 1) * (windowSec / smoothed.Length));

        if (invalidTail < 0.4)
        {
            return 0;
        }

        return Math.Min(TailScanWindowSec, invalidTail);
    }

    private static double[] SmoothMovingAverage(double[] values, int windowSize)
    {
        if (values.Length == 0 || windowSize <= 1)
        {
            return values;
        }

        var radius = Math.Max(1, windowSize / 2);
        var smoothed = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var start = Math.Max(0, i - radius);
            var end = Math.Min(values.Length - 1, i + radius);
            double sum = 0;
            var count = 0;
            for (var j = start; j <= end; j++)
            {
                sum += values[j];
                count++;
            }

            smoothed[i] = count > 0 ? sum / count : values[i];
        }

        return smoothed;
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var ordered = values.ToArray();
        Array.Sort(ordered);
        var index = (int)Math.Clamp(Math.Round((ordered.Length - 1) * percentile), 0, ordered.Length - 1);
        return ordered[index];
    }

    private static bool TrySeekSeconds(int handle, double seconds)
    {
        if (seconds <= 0)
        {
            return Bass.ChannelSetPosition(handle, 0);
        }

        var position = Bass.ChannelSeconds2Bytes(handle, seconds);
        return position >= 0 && Bass.ChannelSetPosition(handle, position);
    }

    private static TrackAnalysisSnapshot MergeCurrentTrackMetrics(TrackAnalysisSnapshot baseAnalysis, TailPlaybackMetrics? tailMetrics)
    {
        if (tailMetrics == null)
        {
            return baseAnalysis;
        }

        return baseAnalysis with
        {
            EndRms = tailMetrics.Value.TailRms > 0 ? tailMetrics.Value.TailRms : baseAnalysis.EndRms,
            EndDynamicRms = tailMetrics.Value.TailRms > 0 ? tailMetrics.Value.TailRms : baseAnalysis.EndDynamicRms,
            EndBrightness = tailMetrics.Value.TailBrightness > 0 ? tailMetrics.Value.TailBrightness : baseAnalysis.EndBrightness,
            TailSilenceSec = Math.Max(baseAnalysis.TailSilenceSec, tailMetrics.Value.TailSilenceSec),
            InvalidTailSec = Math.Max(baseAnalysis.InvalidTailSec, tailMetrics.Value.TailSilenceSec),
            TailActivityRatio = baseAnalysis.TailActivityRatio > 0
                ? Math.Min(baseAnalysis.TailActivityRatio, Math.Clamp(1.0 - tailMetrics.Value.TailSilenceSec / 8.0, 0.0, 1.0))
                : Math.Clamp(1.0 - tailMetrics.Value.TailSilenceSec / 8.0, 0.0, 1.0),
            TailWindowAvailable = true,
            IsReliable = true
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static double Lerp(double from, double to, double amount)
    {
        var safe = Math.Clamp(amount, 0.0, 1.0);
        return from + (to - from) * safe;
    }

    private static float Lerp(float from, float to, double amount)
    {
        var safe = (float)Math.Clamp(amount, 0.0, 1.0);
        return from + (to - from) * safe;
    }
}
