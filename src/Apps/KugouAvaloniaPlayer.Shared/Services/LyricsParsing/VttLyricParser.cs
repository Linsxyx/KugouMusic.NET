using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class VttLyricParser : ILyricParser
{
    private static readonly Regex TimeRangeRegex =
        new(@"(\d{2}:)?(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}:)?(\d{2}):(\d{2})\.(\d{3})");

    public string Extension => ".vtt";

    public List<LyricLineViewModel> Parse(string content)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<LyricLineViewModel>();

        for (var i = 0; i < lines.Length; i++)
        {
            var match = TimeRangeRegex.Match(lines[i]);
            if (!match.Success)
                continue;

            var startH = string.IsNullOrEmpty(match.Groups[1].Value) ? 0 : int.Parse(match.Groups[1].Value.TrimEnd(':'));
            var startM = int.Parse(match.Groups[2].Value);
            var startS = int.Parse(match.Groups[3].Value);
            var startMs = int.Parse(match.Groups[4].Value);

            var endH = string.IsNullOrEmpty(match.Groups[5].Value) ? 0 : int.Parse(match.Groups[5].Value.TrimEnd(':'));
            var endM = int.Parse(match.Groups[6].Value);
            var endS = int.Parse(match.Groups[7].Value);
            var endMs = int.Parse(match.Groups[8].Value);

            var startTime = startH * 3600000 + startM * 60000 + startS * 1000 + startMs;
            var endTime = endH * 3600000 + endM * 60000 + endS * 1000 + endMs;

            var textLines = new List<string>();
            i++;

            while (i < lines.Length && !TimeRangeRegex.IsMatch(lines[i]))
            {
                var currentLine = lines[i].Trim();
                if (!string.IsNullOrEmpty(currentLine) &&
                    !currentLine.Contains("WEBVTT", StringComparison.OrdinalIgnoreCase) &&
                    !currentLine.StartsWith("NOTE", StringComparison.Ordinal) &&
                    !IsNumericLine(currentLine))
                {
                    textLines.Add(currentLine);
                }

                i++;
            }

            i--;

            var text = string.Join("\n", textLines).Trim();
            if (!string.IsNullOrEmpty(text))
            {
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

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return result;
    }

    private static bool IsNumericLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        foreach (var c in line.Trim())
        {
            if (!char.IsDigit(c) && !char.IsWhiteSpace(c))
                return false;
        }

        return true;
    }
}
