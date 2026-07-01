using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ATL;
using AvaloniaLyrics;
using Avalonia.Collections;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Services.LyricsParsing;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public class LyricsService(LyricClient lyricClient, ILogger<LyricsService> logger)
{
    private LyricLineViewModel? _currentActiveLine;
    public AvaloniaList<LyricLineViewModel> LyricLines { get; } = new();
    public AvaloniaList<LyricLine> RenderLyricLines { get; } = new();
    public int CurrentLyricIndex { get; private set; } = -1;

    public void Clear()
    {
        LyricLines.Clear();
        RenderLyricLines.Clear();
        _currentActiveLine = null;
        CurrentLyricIndex = -1;
    }

    public LyricLineViewModel? SyncLyrics(double currentMs)
    {
        if (LyricLines.Count == 0)
        {
            CurrentLyricIndex = -1;
            return null;
        }

        int left = 0, right = LyricLines.Count - 1, resultIndex = 0;

        if (currentMs < LyricLines[0].StartTime) resultIndex = 0;
        else if (currentMs >= LyricLines[^1].StartTime) resultIndex = LyricLines.Count - 1;
        else
            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                if (LyricLines[mid].StartTime <= currentMs)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

        var activeLine = LyricLines[resultIndex];
        CurrentLyricIndex = resultIndex;

        if (_currentActiveLine != activeLine)
        {
            if (_currentActiveLine != null) _currentActiveLine.IsActive = false;
            activeLine.IsActive = true;
            _currentActiveLine = activeLine;
        }

        return activeLine;
    }

    public LyricLineViewModel? GetLineAt(int index)
    {
        return index >= 0 && index < LyricLines.Count ? LyricLines[index] : null;
    }

    public async Task LoadOnlineLyricsAsync(string hash, string name)
    {
        Clear();
        try
        {
            var searchJson = await lyricClient.SearchLyricAsync(hash, null, name, "no");
            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) ||
                candidatesElem.ValueKind != JsonValueKind.Array) return;

            var candidates = candidatesElem.EnumerateArray().AsValueEnumerable().ToList();
            if (candidates.Count == 0) return;

            var bestMatch = candidates.AsValueEnumerable().First();
            var id = bestMatch.GetProperty("id").GetString();
            var key = bestMatch.GetProperty("accesskey").GetString();
            var fmt = bestMatch.TryGetProperty("fmt", out var f) ? f.GetString() ?? "krc" : "krc";
            var ext = NormalizeLyricExtension(fmt);

            if (id != null && key != null)
            {
                if (PersistentLyricParseCache.TryLoadOnline(id, key, ext, out var cachedLines))
                {
                    AddLyricLines(cachedLines);
                    return;
                }

                var lyricResult = await lyricClient.GetLyricAsync(id, key, fmt);
                if (!string.IsNullOrEmpty(lyricResult.DecodedContent))
                {
                    var lines = ParseLyricContent(lyricResult.DecodedContent, ext);
                    PersistentLyricParseCache.SaveOnline(id, key, ext, lines.AsValueEnumerable().Select(ToParsedLyricLine).ToList());
                    AddLyricLines(lines);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取在线歌词失败");
        }
    }

    public async Task LoadLocalLyricsAsync(string audioFilePath)
    {
        Clear();
        try
        {
            var directory = Path.GetDirectoryName(audioFilePath);
            var audioFileName = Path.GetFileName(audioFilePath);
            var audioFileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFilePath);

            if (directory == null) return;

            List<LyricLineViewModel> lines;
            var lyricFilePath = FindLyricFile(directory, audioFileName, audioFileNameWithoutExt);
            if (lyricFilePath != null)
            {
                var ext = Path.GetExtension(lyricFilePath).ToLowerInvariant();
                if (PersistentLyricParseCache.TryLoadLocalFile(lyricFilePath, ext, out var cachedLines))
                {
                    lines = cachedLines.AsValueEnumerable().Select(ToLyricLineViewModel).ToList();
                }
                else
                {
                    lines = await ParseLyricFileAsync(lyricFilePath, ext);
                    PersistentLyricParseCache.SaveLocalFile(lyricFilePath, ext, lines.AsValueEnumerable().Select(ToParsedLyricLine).ToList());
                }
            }
            else
            {
                var embeddedLyrics = ReadEmbeddedLyrics(audioFilePath);
                if (string.IsNullOrWhiteSpace(embeddedLyrics))
                    return;

                var ext = DetectEmbeddedLyricFormat(embeddedLyrics);
                if (PersistentLyricParseCache.TryLoadEmbedded(audioFilePath, ext, embeddedLyrics, out var cachedLines))
                {
                    lines = cachedLines.AsValueEnumerable().Select(ToLyricLineViewModel).ToList();
                }
                else
                {
                    lines = ParseLyricContent(embeddedLyrics, ext);
                    PersistentLyricParseCache.SaveEmbedded(
                        audioFilePath,
                        ext,
                        embeddedLyrics,
                        lines.AsValueEnumerable().Select(ToParsedLyricLine).ToList());
                }
            }

            AddLyricLines(lines);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载本地歌词失败");
        }
    }

    private string? FindLyricFile(string directory, string audioFileName, string audioFileNameWithoutExt)
    {
        var extensions = new[] { ".krc", ".lrc", ".qrc", ".vtt", ".yrc" };
        var searchPatterns = new List<Func<string?>>
        {
            () => extensions.AsValueEnumerable().Select(ext => Path.Combine(directory, audioFileName + ext)).FirstOrDefault(File.Exists),
            () => extensions.AsValueEnumerable().Select(ext => Path.Combine(directory, audioFileNameWithoutExt + ext))
                .FirstOrDefault(File.Exists),
            () =>
            {
                var allLyricFiles = Directory.GetFiles(directory, "*.*")
                    .AsValueEnumerable().Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
                return allLyricFiles.AsValueEnumerable().FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).ToLowerInvariant()
                        .Contains(audioFileNameWithoutExt.ToLowerInvariant()));
            }
        };

        foreach (var strategy in searchPatterns)
        {
            var result = strategy();
            if (result != null) return result;
        }

        return null;
    }

    private async Task<List<LyricLineViewModel>> ParseLyricFileAsync(string filePath, string ext)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return LyricParserRegistry.Parse(content, ext);
    }

    private static string? ReadEmbeddedLyrics(string audioFilePath)
    {
        try
        {
            var track = new Track(audioFilePath);
            return GetEmbeddedLyrics(track.Lyrics);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetEmbeddedLyrics(IList<LyricsInfo>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return null;

        foreach (var entry in lyrics)
        {
            if (entry.SynchronizedLyrics is { Count: > 0 })
            {
                var content = entry.FormatSynch();
                if (!string.IsNullOrWhiteSpace(content))
                    return content;
            }

            if (!string.IsNullOrWhiteSpace(entry.UnsynchronizedLyrics))
                return entry.UnsynchronizedLyrics;
        }

        return null;
    }

    private static string DetectEmbeddedLyricFormat(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("-->", StringComparison.Ordinal))
        {
            return ".vtt";
        }

        if (Regex.IsMatch(content, @"(?m)^\[\d+,\d+\]\("))
        {
            return ".yrc";
        }

        if (Regex.IsMatch(content, @"(?m)^\[\d+,\d+\].*?\(\d+,\d+\)"))
            return ".qrc";

        if (Regex.IsMatch(content, @"(?m)^\[\d+,\d+\]"))
            return ".krc";

        if (Regex.IsMatch(content, @"\[\d{1,3}:\d{2}(?:[.:]\d{1,4})?\]"))
            return ".lrc";

        return ".txt";
    }

    private static string NormalizeLyricExtension(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? ".krc" : value.Trim().ToLowerInvariant();
        return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : "." + normalized;
    }

    private static List<LyricLineViewModel> ParseLyricContent(string content, string ext)
    {
        return LyricParserRegistry.Parse(content, ext);
    }

    private void AddLyricLine(LyricLineViewModel line)
    {
        LyricLines.Add(line);
        RenderLyricLines.Add(ConvertToRenderLine(line));
    }

    private void AddLyricLines(IEnumerable<LyricLineViewModel> lines)
    {
        foreach (var line in lines)
            AddLyricLine(line);
    }

    private void AddLyricLines(IEnumerable<ParsedLyricLine> lines)
    {
        foreach (var line in lines)
            AddLyricLine(ToLyricLineViewModel(line));
    }

    private static LyricLine ConvertToRenderLine(LyricLineViewModel line)
    {
        return new LyricLine
        {
            Text = line.Content,
            Translation = string.IsNullOrWhiteSpace(line.Translation) ? null : line.Translation,
            Romanization = string.IsNullOrWhiteSpace(line.Romanization) ? null : line.Romanization,
            Start = TimeSpan.FromMilliseconds(line.StartTime),
            Duration = TimeSpan.FromMilliseconds(line.Duration),
            Words = line.Words.AsValueEnumerable().Select(ConvertToRenderWord).ToArray(),
            TranslationWords = line.TranslationWords.AsValueEnumerable().Select(ConvertToRenderWord).ToArray()
        };
    }

    private static LyricWord ConvertToRenderWord(LyricWordViewModel word)
    {
        return new LyricWord
        {
            Text = word.Text,
            Start = TimeSpan.FromMilliseconds(word.StartTime),
            Duration = TimeSpan.FromMilliseconds(word.Duration)
        };
    }

    private static ParsedLyricLine ToParsedLyricLine(LyricLineViewModel line)
    {
        return new ParsedLyricLine
        {
            Content = line.Content,
            Translation = line.Translation,
            Romanization = line.Romanization,
            StartTime = line.StartTime,
            Duration = line.Duration,
            HasWordLevelTranslation = line.HasWordLevelTranslation,
            IsKrcWordLevel = line.IsKrcWordLevel,
            Words = line.Words.AsValueEnumerable().Select(ToParsedLyricWord).ToList(),
            TranslationWords = line.TranslationWords.AsValueEnumerable().Select(ToParsedLyricWord).ToList()
        };
    }

    private static ParsedLyricWord ToParsedLyricWord(LyricWordViewModel word)
    {
        return new ParsedLyricWord
        {
            Text = word.Text,
            StartTime = word.StartTime,
            Duration = word.Duration
        };
    }

    private static LyricLineViewModel ToLyricLineViewModel(ParsedLyricLine line)
    {
        var viewModel = new LyricLineViewModel
        {
            Content = line.Content,
            Translation = line.Translation,
            Romanization = line.Romanization,
            StartTime = line.StartTime,
            Duration = line.Duration,
            HasWordLevelTranslation = line.HasWordLevelTranslation,
            IsKrcWordLevel = line.IsKrcWordLevel,
            IsActive = false
        };

        foreach (var word in line.Words)
            viewModel.Words.Add(ToLyricWordViewModel(word));

        foreach (var word in line.TranslationWords)
            viewModel.TranslationWords.Add(ToLyricWordViewModel(word));

        return viewModel;
    }

    private static LyricWordViewModel ToLyricWordViewModel(ParsedLyricWord word)
    {
        return new LyricWordViewModel
        {
            Text = word.Text,
            StartTime = word.StartTime,
            Duration = word.Duration
        };
    }
}
