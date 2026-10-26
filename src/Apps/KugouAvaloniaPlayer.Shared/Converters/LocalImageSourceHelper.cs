using System;
using System.IO;

namespace KugouAvaloniaPlayer.Converters;

internal static class LocalImageSourceHelper
{
    private const string EmbeddedCoverScheme = "kg-embedded-cover";

    public static string? GetLocalFilePath(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (TryGetEmbeddedCoverFilePath(source, out _))
            return null;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return uri.IsFile && File.Exists(uri.LocalPath)
                ? uri.LocalPath
                : null;
        }

        return File.Exists(source) ? source : null;
    }

    public static string BuildEmbeddedCoverSource(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var ticks = File.Exists(normalizedPath)
            ? File.GetLastWriteTimeUtc(normalizedPath).Ticks
            : 0;
        var encodedPath = Uri.EscapeDataString(normalizedPath);
        return $"{EmbeddedCoverScheme}://track?path={encodedPath}&ticks={ticks}";
    }

    public static bool TryGetEmbeddedCoverFilePath(string? source, out string? filePath)
    {
        filePath = null;
        if (string.IsNullOrWhiteSpace(source) ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, EmbeddedCoverScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
            return false;

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length != 2 || !string.Equals(pair[0], "path", StringComparison.OrdinalIgnoreCase))
                continue;

            var decodedPath = Uri.UnescapeDataString(pair[1]);
            if (!File.Exists(decodedPath))
                return false;

            filePath = decodedPath;
            return true;
        }

        return false;
    }
}
