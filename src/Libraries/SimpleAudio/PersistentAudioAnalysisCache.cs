using System.Security.Cryptography;
using System.Text;
using MemoryPack;

namespace SimpleAudio;

internal static class PersistentAudioAnalysisCache
{
    private const int TrackAnalysisSchemaVersion = 1;
    private const int NormalizationGainSchemaVersion = 1;
    private const int MaxCacheFiles = 8192;
    private const long MaxCacheBytes = 64L * 1024 * 1024;

    private static readonly string CacheRoot = Path.Combine(
        ResolveLocalCacheRoot(),
        "kugou",
        "simple-audio-cache");

    public static string? GetTrackAnalysisMemoryKey(string source, bool isLocal)
    {
        return TryCreateLocalIdentity(source, isLocal, TrackAnalysisSchemaVersion, out var identity)
            ? identity.MemoryKey
            : null;
    }

    public static string? GetNormalizationGainMemoryKey(string source, bool isLocal)
    {
        return TryCreateLocalIdentity(source, isLocal, NormalizationGainSchemaVersion, out var identity)
            ? identity.MemoryKey
            : null;
    }

    public static bool TryLoadTrackAnalysis(string source, bool isLocal, out TrackAnalysisSnapshot snapshot)
    {
        snapshot = TrackAnalysisSnapshot.Empty;
        if (!TryCreateLocalIdentity(source, isLocal, TrackAnalysisSchemaVersion, out var identity))
        {
            return false;
        }

        try
        {
            var path = GetCachePath("track-analysis", identity);
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return false;
            }

            var payload = MemoryPackSerializer.Deserialize<TrackAnalysisCachePayload>(File.ReadAllBytes(fileInfo.FullName));
            if (payload == null ||
                payload.SchemaVersion != TrackAnalysisSchemaVersion ||
                !identity.Matches(payload.SourcePath, payload.FileSize, payload.LastWriteTimeUtcTicks))
            {
                return false;
            }

            TouchCacheFile(fileInfo);
            snapshot = new TrackAnalysisSnapshot
            {
                DurationSeconds = payload.DurationSeconds,
                Bpm = payload.Bpm,
                StartRms = payload.StartRms,
                EndRms = payload.EndRms,
                StartBrightness = payload.StartBrightness,
                EndBrightness = payload.EndBrightness,
                StartDynamicRms = payload.StartDynamicRms,
                EndDynamicRms = payload.EndDynamicRms,
                DynamicWindowSec = payload.DynamicWindowSec,
                IntroSilenceSec = payload.IntroSilenceSec,
                IntroActivityRatio = payload.IntroActivityRatio,
                TailSilenceSec = payload.TailSilenceSec,
                InvalidTailSec = payload.InvalidTailSec,
                TailActivityRatio = payload.TailActivityRatio,
                TailWindowAvailable = payload.TailWindowAvailable,
                IsReliable = payload.IsReliable
            };
            return snapshot.IsReliable;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveTrackAnalysis(string source, bool isLocal, TrackAnalysisSnapshot snapshot)
    {
        if (!snapshot.IsReliable ||
            !TryCreateLocalIdentity(source, isLocal, TrackAnalysisSchemaVersion, out var identity))
        {
            return;
        }

        var payload = new TrackAnalysisCachePayload
        {
            SchemaVersion = TrackAnalysisSchemaVersion,
            SourcePath = identity.SourcePath,
            FileSize = identity.FileSize,
            LastWriteTimeUtcTicks = identity.LastWriteTimeUtcTicks,
            DurationSeconds = snapshot.DurationSeconds,
            Bpm = snapshot.Bpm,
            StartRms = snapshot.StartRms,
            EndRms = snapshot.EndRms,
            StartBrightness = snapshot.StartBrightness,
            EndBrightness = snapshot.EndBrightness,
            StartDynamicRms = snapshot.StartDynamicRms,
            EndDynamicRms = snapshot.EndDynamicRms,
            DynamicWindowSec = snapshot.DynamicWindowSec,
            IntroSilenceSec = snapshot.IntroSilenceSec,
            IntroActivityRatio = snapshot.IntroActivityRatio,
            TailSilenceSec = snapshot.TailSilenceSec,
            InvalidTailSec = snapshot.InvalidTailSec,
            TailActivityRatio = snapshot.TailActivityRatio,
            TailWindowAvailable = snapshot.TailWindowAvailable,
            IsReliable = snapshot.IsReliable
        };

        SavePayload("track-analysis", identity, payload);
    }

    public static bool TryLoadNormalizationGain(string source, bool isLocal, out float gain)
    {
        gain = 1.0f;
        if (!TryCreateLocalIdentity(source, isLocal, NormalizationGainSchemaVersion, out var identity))
        {
            return false;
        }

        try
        {
            var path = GetCachePath("normalization-gain", identity);
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return false;
            }

            var payload = MemoryPackSerializer.Deserialize<NormalizationGainCachePayload>(File.ReadAllBytes(fileInfo.FullName));
            if (payload == null ||
                payload.SchemaVersion != NormalizationGainSchemaVersion ||
                !identity.Matches(payload.SourcePath, payload.FileSize, payload.LastWriteTimeUtcTicks) ||
                float.IsNaN(payload.Gain) ||
                float.IsInfinity(payload.Gain) ||
                payload.Gain <= 0)
            {
                return false;
            }

            TouchCacheFile(fileInfo);
            gain = payload.Gain;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveNormalizationGain(string source, bool isLocal, float gain)
    {
        if (float.IsNaN(gain) ||
            float.IsInfinity(gain) ||
            gain <= 0 ||
            !TryCreateLocalIdentity(source, isLocal, NormalizationGainSchemaVersion, out var identity))
        {
            return;
        }

        var payload = new NormalizationGainCachePayload
        {
            SchemaVersion = NormalizationGainSchemaVersion,
            SourcePath = identity.SourcePath,
            FileSize = identity.FileSize,
            LastWriteTimeUtcTicks = identity.LastWriteTimeUtcTicks,
            Gain = gain
        };

        SavePayload("normalization-gain", identity, payload);
    }

    private static void SavePayload<T>(string scope, SourceIdentity identity, T payload)
    {
        try
        {
            var directory = GetCacheDirectory(scope);
            Directory.CreateDirectory(directory);
            var path = GetCachePath(scope, identity);
            var tempPath = Path.Combine(directory, $"{identity.Hash}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(tempPath, MemoryPackSerializer.Serialize(payload));
            File.Move(tempPath, path, true);
            TrimCacheRoot();
        }
        catch
        {
            // Cache writes are opportunistic; playback should never depend on them.
        }
    }

    private static void TrimCacheRoot()
    {
        try
        {
            var root = new DirectoryInfo(CacheRoot);
            if (!root.Exists)
            {
                return;
            }

            var files = root
                .EnumerateFiles("*.mpack", SearchOption.AllDirectories)
                .OrderByDescending(static x => x.LastWriteTimeUtc)
                .ToList();
            var totalBytes = files.Sum(static x => x.Length);

            if (files.Count <= MaxCacheFiles && totalBytes <= MaxCacheBytes)
            {
                return;
            }

            for (var i = files.Count - 1; i >= 0; i--)
            {
                if (files.Count <= MaxCacheFiles && totalBytes <= MaxCacheBytes)
                {
                    break;
                }

                var file = files[i];
                var length = file.Length;
                try
                {
                    file.Delete();
                    files.RemoveAt(i);
                    totalBytes -= length;
                }
                catch
                {
                    // Best effort trim.
                }
            }
        }
        catch
        {
            // Best effort trim.
        }
    }

    private static void TouchCacheFile(FileInfo fileInfo)
    {
        try
        {
            fileInfo.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch
        {
            // Best effort recency update only.
        }
    }

    private static string GetCachePath(string scope, SourceIdentity identity)
    {
        return Path.Combine(GetCacheDirectory(scope), $"{identity.Hash}.mpack");
    }

    private static string GetCacheDirectory(string scope)
    {
        return Path.Combine(CacheRoot, scope);
    }

    private static bool TryCreateLocalIdentity(string source, bool isLocal, int schemaVersion, out SourceIdentity identity)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(source) ||
            (!isLocal && source.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            if (!File.Exists(source))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(source);
            var fileInfo = new FileInfo(fullPath);
            var lastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
            var memoryKey = $"{schemaVersion}|{fullPath}|{fileInfo.Length}|{lastWriteTimeUtcTicks}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(memoryKey)));
            identity = new SourceIdentity(fullPath, fileInfo.Length, lastWriteTimeUtcTicks, memoryKey, hash);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveLocalCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
    }

    private readonly record struct SourceIdentity(
        string SourcePath,
        long FileSize,
        long LastWriteTimeUtcTicks,
        string MemoryKey,
        string Hash)
    {
        public bool Matches(string sourcePath, long fileSize, long lastWriteTimeUtcTicks)
        {
            return fileSize == FileSize &&
                   lastWriteTimeUtcTicks == LastWriteTimeUtcTicks &&
                   string.Equals(sourcePath, SourcePath, StringComparison.Ordinal);
        }
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class TrackAnalysisCachePayload
{
    [MemoryPackOrder(0)]
    public int SchemaVersion { get; set; }

    [MemoryPackOrder(1)]
    public string SourcePath { get; set; } = "";

    [MemoryPackOrder(2)]
    public long FileSize { get; set; }

    [MemoryPackOrder(3)]
    public long LastWriteTimeUtcTicks { get; set; }

    [MemoryPackOrder(4)]
    public double DurationSeconds { get; set; }

    [MemoryPackOrder(5)]
    public double Bpm { get; set; }

    [MemoryPackOrder(6)]
    public double StartRms { get; set; }

    [MemoryPackOrder(7)]
    public double EndRms { get; set; }

    [MemoryPackOrder(8)]
    public double StartBrightness { get; set; }

    [MemoryPackOrder(9)]
    public double EndBrightness { get; set; }

    [MemoryPackOrder(10)]
    public double StartDynamicRms { get; set; }

    [MemoryPackOrder(11)]
    public double EndDynamicRms { get; set; }

    [MemoryPackOrder(12)]
    public double DynamicWindowSec { get; set; }

    [MemoryPackOrder(13)]
    public double IntroSilenceSec { get; set; }

    [MemoryPackOrder(14)]
    public double IntroActivityRatio { get; set; }

    [MemoryPackOrder(15)]
    public double TailSilenceSec { get; set; }

    [MemoryPackOrder(16)]
    public double InvalidTailSec { get; set; }

    [MemoryPackOrder(17)]
    public double TailActivityRatio { get; set; }

    [MemoryPackOrder(18)]
    public bool TailWindowAvailable { get; set; }

    [MemoryPackOrder(19)]
    public bool IsReliable { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class NormalizationGainCachePayload
{
    [MemoryPackOrder(0)]
    public int SchemaVersion { get; set; }

    [MemoryPackOrder(1)]
    public string SourcePath { get; set; } = "";

    [MemoryPackOrder(2)]
    public long FileSize { get; set; }

    [MemoryPackOrder(3)]
    public long LastWriteTimeUtcTicks { get; set; }

    [MemoryPackOrder(4)]
    public float Gain { get; set; } = 1.0f;
}
