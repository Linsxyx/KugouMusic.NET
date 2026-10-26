using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;

namespace KugouAvaloniaPlayer.Services;

internal static class PersistentLyricParseCache
{
    private const int SchemaVersion = 1;
    private const long MaxCacheBytes = 64L * 1024 * 1024;
    private const int MaxCacheFiles = 8192;

    private static readonly string CacheRoot = Path.Combine(
        ResolveLocalCacheRoot(),
        "kugou",
        "lyric-parse-cache");

    public static bool TryLoadOnline(string id, string accessKey, string fmt, out List<ParsedLyricLine> lines)
    {
        var identity = CreateOnlineIdentity(id, accessKey, fmt);
        return TryLoad(identity, out lines);
    }

    public static void SaveOnline(string id, string accessKey, string fmt, IReadOnlyList<ParsedLyricLine> lines)
    {
        Save(CreateOnlineIdentity(id, accessKey, fmt), lines);
    }

    public static bool TryLoadLocalFile(string filePath, string ext, out List<ParsedLyricLine> lines)
    {
        lines = new List<ParsedLyricLine>();
        return TryCreateFileIdentity("local-file", filePath, ext, null, out var identity) &&
               TryLoad(identity, out lines);
    }

    public static void SaveLocalFile(string filePath, string ext, IReadOnlyList<ParsedLyricLine> lines)
    {
        if (TryCreateFileIdentity("local-file", filePath, ext, null, out var identity))
        {
            Save(identity, lines);
        }
    }

    public static bool TryLoadEmbedded(string audioFilePath, string ext, string content, out List<ParsedLyricLine> lines)
    {
        lines = new List<ParsedLyricLine>();
        return TryCreateFileIdentity("embedded", audioFilePath, ext, GetContentHash(content), out var identity) &&
               TryLoad(identity, out lines);
    }

    public static void SaveEmbedded(
        string audioFilePath,
        string ext,
        string content,
        IReadOnlyList<ParsedLyricLine> lines)
    {
        if (TryCreateFileIdentity("embedded", audioFilePath, ext, GetContentHash(content), out var identity))
        {
            Save(identity, lines);
        }
    }

    private static bool TryLoad(CacheIdentity identity, out List<ParsedLyricLine> lines)
    {
        lines = new List<ParsedLyricLine>();
        try
        {
            var fileInfo = new FileInfo(GetCachePath(identity));
            if (!fileInfo.Exists)
            {
                return false;
            }

            var payload = MemoryPackSerializer.Deserialize<ParsedLyricCachePayload>(File.ReadAllBytes(fileInfo.FullName));
            if (payload == null ||
                payload.SchemaVersion != SchemaVersion ||
                !string.Equals(payload.Identity, identity.Value, StringComparison.Ordinal) ||
                payload.Lines.Count == 0)
            {
                return false;
            }

            TouchCacheFile(fileInfo);
            lines = payload.Lines;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Save(CacheIdentity identity, IReadOnlyList<ParsedLyricLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheRoot);
            var path = GetCachePath(identity);
            var tempPath = Path.Combine(CacheRoot, $"{identity.Hash}.{Guid.NewGuid():N}.tmp");
            var payload = new ParsedLyricCachePayload
            {
                SchemaVersion = SchemaVersion,
                Identity = identity.Value,
                Lines = lines.AsValueEnumerable().ToList()
            };

            File.WriteAllBytes(tempPath, MemoryPackSerializer.Serialize(payload));
            File.Move(tempPath, path, true);
            TrimCache();
        }
        catch
        {
            // Parsed lyrics are a warm cache only; loading should always be able to re-parse.
        }
    }

    private static CacheIdentity CreateOnlineIdentity(string id, string accessKey, string fmt)
    {
        var value = string.Join(
            '|',
            SchemaVersion,
            "online",
            id.Trim(),
            accessKey.Trim(),
            NormalizeExtension(fmt));
        return new CacheIdentity(value, Hash(value));
    }

    private static bool TryCreateFileIdentity(
        string scope,
        string filePath,
        string ext,
        string? contentHash,
        out CacheIdentity identity)
    {
        identity = default;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(filePath);
            var fileInfo = new FileInfo(fullPath);
            var value = string.Join(
                '|',
                SchemaVersion,
                scope,
                fullPath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                NormalizeExtension(ext),
                contentHash ?? "");
            identity = new CacheIdentity(value, Hash(value));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TrimCache()
    {
        try
        {
            var directory = new DirectoryInfo(CacheRoot);
            if (!directory.Exists)
            {
                return;
            }

            var files = directory
                .EnumerateFiles("*.mpack")
                .AsValueEnumerable().OrderByDescending(static x => x.LastWriteTimeUtc)
                .ToList();
            var totalBytes = files.AsValueEnumerable().Sum(static x => x.Length);
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
                    // Best effort cleanup only.
                }
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static string GetCachePath(CacheIdentity identity)
    {
        return Path.Combine(CacheRoot, $"{identity.Hash}.mpack");
    }

    private static string GetContentHash(string content)
    {
        return Hash(content);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string NormalizeExtension(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? ".krc" : value.Trim().ToLowerInvariant();
        return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : "." + normalized;
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

    private static string ResolveLocalCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
    }

    private readonly record struct CacheIdentity(string Value, string Hash);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ParsedLyricCachePayload
{
    [MemoryPackOrder(0)]
    public int SchemaVersion { get; set; }

    [MemoryPackOrder(1)]
    public string Identity { get; set; } = "";

    [MemoryPackOrder(2)]
    public List<ParsedLyricLine> Lines { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ParsedLyricLine
{
    [MemoryPackOrder(0)]
    public string Content { get; set; } = "";

    [MemoryPackOrder(1)]
    public string Translation { get; set; } = "";

    [MemoryPackOrder(2)]
    public string Romanization { get; set; } = "";

    [MemoryPackOrder(3)]
    public double StartTime { get; set; }

    [MemoryPackOrder(4)]
    public double Duration { get; set; }

    [MemoryPackOrder(5)]
    public bool HasWordLevelTranslation { get; set; }

    [MemoryPackOrder(6)]
    public bool IsKrcWordLevel { get; set; }

    [MemoryPackOrder(7)]
    public List<ParsedLyricWord> Words { get; set; } = new();

    [MemoryPackOrder(8)]
    public List<ParsedLyricWord> TranslationWords { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ParsedLyricWord
{
    [MemoryPackOrder(0)]
    public string Text { get; set; } = "";

    [MemoryPackOrder(1)]
    public double StartTime { get; set; }

    [MemoryPackOrder(2)]
    public double Duration { get; set; }
}
