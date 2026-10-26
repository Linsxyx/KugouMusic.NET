using System;
using System.Text.RegularExpressions;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal static class EnhancedLrcWordParser
{
    public static void MapWords(LyricLineViewModel line, string text)
    {
        if (TryMapRelativeWords(line, text))
            return;

        TryMapAbsoluteWords(line, text);
    }

    public static void CompleteWordDurations(LyricLineViewModel line)
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

    public static string StripTags(string text)
    {
        return Regex.Replace(
                Regex.Replace(text, @"<\d+,\d+(?:,\d+)?>", ""),
                @"<\d{1,3}:\d{2}(?:[.:]\d{1,4})?>",
                "")
            .Trim();
    }

    private static bool TryMapRelativeWords(LyricLineViewModel line, string text)
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

    private static bool TryMapAbsoluteWords(LyricLineViewModel line, string text)
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
}
