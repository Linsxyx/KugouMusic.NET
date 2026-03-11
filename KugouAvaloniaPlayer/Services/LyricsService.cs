using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Collections;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public class LyricsService(LyricClient lyricClient, ILogger<LyricsService> logger)
{
    public AvaloniaList<LyricLineViewModel> LyricLines { get; } = new();

    public void Clear()
    {
        LyricLines.Clear();
    }
    
    public LyricLineViewModel? SyncLyrics(double currentMs)
    {
        if (LyricLines.Count == 0) return null;

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
        
        foreach (var line in LyricLines) line.IsActive = line == activeLine;

        return activeLine;
    }

    public async Task LoadOnlineLyricsAsync(string hash, string name)
    {
        Clear();
        try
        {
            var searchJson = await lyricClient.SearchLyricAsync(hash, null, name, "no");
            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) ||
                candidatesElem.ValueKind != JsonValueKind.Array) return;

            var candidates = candidatesElem.EnumerateArray().ToList();
            if (candidates.Count == 0) return;

            var bestMatch = candidates.First();
            var id = bestMatch.GetProperty("id").GetString();
            var key = bestMatch.GetProperty("accesskey").GetString();
            var fmt = bestMatch.TryGetProperty("fmt", out var f) ? f.GetString() ?? "krc" : "krc";

            if (id != null && key != null)
            {
                var lyricResult = await lyricClient.GetLyricAsync(id, key, fmt);
                if (!string.IsNullOrEmpty(lyricResult.DecodedContent))
                {
                    var krc = KrcParser.Parse(lyricResult.DecodedContent);
                    foreach (var line in krc.Lines)
                        LyricLines.Add(new LyricLineViewModel
                        {
                            Content = line.Content,
                            Translation = line.Translation,
                            StartTime = line.StartTime,
                            Duration = line.Duration,
                            IsActive = false
                        });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"获取在线歌词失败: {ex.Message}");
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

            var lyricFilePath = FindLyricFile(directory, audioFileName, audioFileNameWithoutExt);
            if (lyricFilePath == null) return;

            var lines = await ParseLyricFileAsync(lyricFilePath, Path.GetExtension(lyricFilePath).ToLowerInvariant());
            foreach (var line in lines) LyricLines.Add(line);
        }
        catch (Exception ex)
        {
            logger.LogError($"加载本地歌词失败: {ex.Message}");
        }
    }

    private string? FindLyricFile(string directory, string audioFileName, string audioFileNameWithoutExt)
    {
        var extensions = new[] { ".krc", ".lrc", ".vtt" };
        var searchPatterns = new List<Func<string?>>
        {
            () => extensions.Select(ext => Path.Combine(directory, audioFileName + ext)).FirstOrDefault(File.Exists),
            () => extensions.Select(ext => Path.Combine(directory, audioFileNameWithoutExt + ext))
                .FirstOrDefault(File.Exists),
            () =>
            {
                var allLyricFiles = Directory.GetFiles(directory, "*.*")
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
                return allLyricFiles.FirstOrDefault(f =>
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
        var result = new List<LyricLineViewModel>();
        var content = await File.ReadAllTextAsync(filePath);

        bool IsNumericLine(string line)
        {
            return !string.IsNullOrWhiteSpace(line) &&
                   line.Trim().All(c => char.IsDigit(c) || char.IsWhiteSpace(c));
        }


        if (ext == ".krc")
        {
            var krc = KrcParser.Parse(content);
            foreach (var line in krc.Lines)
                result.Add(new LyricLineViewModel
                {
                    Content = line.Content,
                    Translation = line.Translation,
                    StartTime = line.StartTime,
                    Duration = line.Duration,
                    IsActive = false
                });
        }
        else if (ext == ".lrc")
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"\[(\d{2,3}):(\d{2})\.(\d{1,4})\]");
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
                        var msStr = match.Groups[3].Value;
                        var ms = int.Parse(msStr);
                        if (msStr.Length == 1) ms *= 100;
                        else if (msStr.Length == 2) ms *= 10;
                        else if (msStr.Length == 4) ms /= 10;

                        var time = m * 60000 + s * 1000 + ms;

                        lrcLines.Add(new LyricLineViewModel
                        {
                            Content = text,
                            StartTime = time,
                            Translation = "",
                            IsActive = false
                        });
                    }
                }
            }

            lrcLines = lrcLines.OrderBy(x => x.StartTime).ToList();

            for (var i = 0; i < lrcLines.Count; i++)
                if (i < lrcLines.Count - 1)
                    lrcLines[i].Duration = lrcLines[i + 1].StartTime - lrcLines[i].StartTime;
                else
                    lrcLines[i].Duration = 5000;

            result.AddRange(lrcLines);
        }
        else if (ext == ".vtt")
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        return result.OrderBy(x => x.StartTime).ToList();
    }
}