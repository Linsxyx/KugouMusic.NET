using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AvaloniaLyrics;
using Avalonia.Collections;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using TagFile = TagLib.File;

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
        var extensions = new[] { ".krc", ".lrc", ".vtt" };
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
        return ParseLyricContent(content, ext);
    }

    private static string? ReadEmbeddedLyrics(string audioFilePath)
    {
        try
        {
            using var tagFile = TagFile.Create(audioFilePath);
            return string.IsNullOrWhiteSpace(tagFile.Tag.Lyrics) ? null : tagFile.Tag.Lyrics;
        }
        catch
        {
            return null;
        }
    }

    private static string DetectEmbeddedLyricFormat(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("-->", StringComparison.Ordinal))
        {
            return ".vtt";
        }

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
        var result = new List<LyricLineViewModel>();

        bool IsNumericLine(string line)
        {
            return !string.IsNullOrWhiteSpace(line) &&
                   line.Trim().AsValueEnumerable().All(c => char.IsDigit(c) || char.IsWhiteSpace(c));
        }


        if (ext == ".krc")
        {
            var krc = KrcParser.Parse(content);
            foreach (var line in krc.Lines)
            {
                var lyricLine = new LyricLineViewModel
                {
                    Content = line.Content,
                    Translation = line.Translation,
                    Romanization = line.Romanization,
                    StartTime = line.StartTime,
                    Duration = line.Duration,
                    IsActive = false
                };
                MapKrcWords(lyricLine, line.Words);
                result.Add(lyricLine);
            }
        }
        else if (ext == ".lrc")
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,4}))?\]");
            var lrcLines = new List<LyricLineViewModel>();

            foreach (var line in lines)
            {
                var matches = regex.Matches(line);
                if (matches.Count > 0)
                {
                    var text = line.Substring(matches[matches.Count - 1].Index + matches[matches.Count - 1].Length)
                        .Trim();

                    foreach (Match match in matches)
                    {
                        var m = int.Parse(match.Groups[1].Value);
                        var s = int.Parse(match.Groups[2].Value);
                        var ms = 0;
                        var msStr = match.Groups[3].Value;
                        if (!string.IsNullOrEmpty(msStr))
                        {
                            ms = int.Parse(msStr);
                            if (msStr.Length == 1) ms *= 100;
                            else if (msStr.Length == 2) ms *= 10;
                            else if (msStr.Length == 4) ms /= 10;
                        }

                        var time = m * 60000 + s * 1000 + ms;

                        lrcLines.Add(CreateLrcLine(text, time));
                    }
                }
            }

            lrcLines = lrcLines.AsValueEnumerable().OrderBy(x => x.StartTime).ToList();

            for (var i = 0; i < lrcLines.Count; i++)
            {
                if (i < lrcLines.Count - 1)
                    lrcLines[i].Duration = lrcLines[i + 1].StartTime - lrcLines[i].StartTime;
                else
                    lrcLines[i].Duration = 5000;

                CompleteEnhancedLrcWordDurations(lrcLines[i]);
            }

            result.AddRange(lrcLines);
        }
        else if (ext == ".vtt")
        {
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"(\d{2}:)?(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}:)?(\d{2}):(\d{2})\.(\d{3})");

            for (var i = 0; i < lines.Length; i++)
            {
                var match = regex.Match(lines[i]);
                if (match.Success)
                {
                    var startH = string.IsNullOrEmpty(match.Groups[1].Value)
                        ? 0
                        : int.Parse(match.Groups[1].Value.TrimEnd(':'));
                    var startM = int.Parse(match.Groups[2].Value);
                    var startS = int.Parse(match.Groups[3].Value);
                    var startMs = int.Parse(match.Groups[4].Value);

                    var endH = string.IsNullOrEmpty(match.Groups[5].Value)
                        ? 0
                        : int.Parse(match.Groups[5].Value.TrimEnd(':'));
                    var endM = int.Parse(match.Groups[6].Value);
                    var endS = int.Parse(match.Groups[7].Value);
                    var endMs = int.Parse(match.Groups[8].Value);

                    var startTime = startH * 3600000 + startM * 60000 + startS * 1000 + startMs;
                    var endTime = endH * 3600000 + endM * 60000 + endS * 1000 + endMs;

                    var textLines = new List<string>();
                    i++;

                    while (i < lines.Length && !regex.IsMatch(lines[i]))
                    {
                        var currentLine = lines[i].Trim();

                        if (!string.IsNullOrEmpty(currentLine) &&
                            !currentLine.Contains("WEBVTT") &&
                            !currentLine.StartsWith("NOTE") &&
                            !IsNumericLine(currentLine))
                            textLines.Add(currentLine);
                        i++;
                    }

                    i--;

                    var text = string.Join("\n", textLines).Trim();
                    if (!string.IsNullOrEmpty(text))
                        result.Add(new LyricLineViewModel
                        {
                            Content = text,
                            StartTime = startTime,
                            Duration = endTime - startTime,
                            Translation = "",
                            IsActive = false
                        });
                }
            }
        }
        else if (ext == ".txt")
        {
            const int plainLineDurationMs = 5000;
            var plainLines = content
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .AsValueEnumerable().Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            for (var i = 0; i < plainLines.Count; i++)
            {
                result.Add(new LyricLineViewModel
                {
                    Content = plainLines[i],
                    StartTime = i * plainLineDurationMs,
                    Duration = plainLineDurationMs,
                    Translation = "",
                    IsActive = false
                });
            }
        }

        return result.AsValueEnumerable().OrderBy(x => x.StartTime).ToList();
    }

    private static LyricLineViewModel CreateLrcLine(string text, long startTime)
    {
        var line = new LyricLineViewModel
        {
            Content = StripEnhancedLrcTags(text),
            StartTime = startTime,
            Translation = "",
            IsActive = false
        };

        MapEnhancedLrcWords(line, text);
        return line;
    }

    private static void MapEnhancedLrcWords(LyricLineViewModel line, string text)
    {
        if (TryMapRelativeEnhancedLrcWords(line, text))
            return;

        TryMapAbsoluteEnhancedLrcWords(line, text);
    }

    private static bool TryMapRelativeEnhancedLrcWords(LyricLineViewModel line, string text)
    {
        var matches = Regex.Matches(text, @"<(\d+),(\d+)(?:,\d+)?>");
        if (matches.Count == 0)
            return false;

        foreach (Match match in matches)
        {
            var segment = GetTextUntilNextMatch(text, match, matches);
            if (string.IsNullOrEmpty(segment))
                continue;

            line.Words.Add(new LyricWordViewModel
            {
                Text = segment,
                StartTime = line.StartTime + double.Parse(match.Groups[1].Value),
                Duration = double.Parse(match.Groups[2].Value)
            });
        }

        line.IsKrcWordLevel = line.Words.Count > 0;
        return line.IsKrcWordLevel;
    }

    private static bool TryMapAbsoluteEnhancedLrcWords(LyricLineViewModel line, string text)
    {
        var matches = Regex.Matches(text, @"<(\d{1,3}):(\d{2})(?:[.:](\d{1,4}))?>");
        if (matches.Count == 0)
            return false;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var segment = GetTextUntilNextMatch(text, match, matches);
            if (string.IsNullOrEmpty(segment))
                continue;

            var startTime = ParseLrcTime(match);
            var duration = i < matches.Count - 1
                ? Math.Max(0, ParseLrcTime(matches[i + 1]) - startTime)
                : 0;

            line.Words.Add(new LyricWordViewModel
            {
                Text = segment,
                StartTime = startTime,
                Duration = duration
            });
        }

        line.IsKrcWordLevel = line.Words.Count > 0;
        return line.IsKrcWordLevel;
    }

    private static string GetTextUntilNextMatch(string text, Match match, MatchCollection matches)
    {
        var start = match.Index + match.Length;
        var nextStart = text.Length;
        foreach (Match next in matches)
        {
            if (next.Index > match.Index)
            {
                nextStart = next.Index;
                break;
            }
        }

        return text[start..nextStart];
    }

    private static void CompleteEnhancedLrcWordDurations(LyricLineViewModel line)
    {
        if (!line.IsKrcWordLevel || line.Words.Count == 0)
            return;

        var lineEnd = line.StartTime + Math.Max(0, line.Duration);
        for (var i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i];
            if (word.Duration > 0)
                continue;

            var nextStart = i < line.Words.Count - 1 ? line.Words[i + 1].StartTime : lineEnd;
            word.Duration = Math.Max(80, nextStart - word.StartTime);
        }
    }

    private static string StripEnhancedLrcTags(string text)
    {
        return Regex.Replace(
                Regex.Replace(text, @"<\d+,\d+(?:,\d+)?>", ""),
                @"<\d{1,3}:\d{2}(?:[.:]\d{1,4})?>",
                "")
            .Trim();
    }

    private static double ParseLrcTime(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value);
        var seconds = int.Parse(match.Groups[2].Value);
        var milliseconds = 0;
        var msText = match.Groups[3].Value;
        if (!string.IsNullOrEmpty(msText))
        {
            milliseconds = int.Parse(msText);
            if (msText.Length == 1) milliseconds *= 100;
            else if (msText.Length == 2) milliseconds *= 10;
            else if (msText.Length == 4) milliseconds /= 10;
        }

        return minutes * 60000 + seconds * 1000 + milliseconds;
    }

    private static void MapKrcWords(LyricLineViewModel line, IReadOnlyList<KrcWord> words)
    {
        if (words.Count == 0) return;

        line.IsKrcWordLevel = true;
        foreach (var word in words)
            line.Words.Add(new LyricWordViewModel
            {
                Text = word.Text,
                StartTime = word.StartTime,
                Duration = word.Duration
            });

        MapKrcTranslationWords(line);
    }

    private static void MapKrcTranslationWords(LyricLineViewModel line)
    {
        if (string.IsNullOrWhiteSpace(line.Translation) || line.Duration <= 0) return;

        var chars = line.Translation.ToCharArray();
        if (chars.Length == 0) return;

        line.HasWordLevelTranslation = true;

        var perCharDuration = Math.Max(40, line.Duration / chars.Length);
        for (var i = 0; i < chars.Length; i++)
        {
            var startTime = line.StartTime + i * perCharDuration;
            if (startTime > line.StartTime + line.Duration) startTime = line.StartTime + line.Duration;

            line.TranslationWords.Add(new LyricWordViewModel
            {
                Text = chars[i].ToString(),
                StartTime = startTime,
                Duration = perCharDuration
            });
        }
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
