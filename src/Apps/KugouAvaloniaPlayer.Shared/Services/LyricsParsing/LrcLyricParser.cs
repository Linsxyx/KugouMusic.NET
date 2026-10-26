using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class LrcLyricParser : ILyricParser
{
    private static readonly Regex TimestampRegex = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,4}))?\]");

    public string Extension => ".lrc";

    public List<LyricLineViewModel> Parse(string content)
    {
        var source = content.AsSpan();
        var result = new List<LyricLineViewModel>();
        var previousPrimaryLines = new List<LyricLineViewModel>();
        var currentPrimaryLines = new List<LyricLineViewModel>();

        foreach (var lineRange in source.SplitAny("\r\n"))
        {
            var line = source[lineRange];
            if (line.IsEmpty)
                continue;

            var hasTimestamp = false;
            var lastTimestampEnd = 0;
            foreach (var match in TimestampRegex.EnumerateMatches(line))
            {
                hasTimestamp = true;
                lastTimestampEnd = match.Index + match.Length;
            }

            if (!hasTimestamp)
                continue;

            var text = line[lastTimestampEnd..].Trim().ToString();
            currentPrimaryLines.Clear();
            foreach (var match in TimestampRegex.EnumerateMatches(line))
            {
                var timestamp = line.Slice(match.Index, match.Length);
                var lyricLine = CreateLine(text, ParseTimestamp(timestamp));
                var translationTarget = FindTranslationTarget(previousPrimaryLines, lyricLine.StartTime);
                if (translationTarget != null)
                {
                    translationTarget.Translation = lyricLine.Content;
                    continue;
                }

                result.Add(lyricLine);
                currentPrimaryLines.Add(lyricLine);
            }

            (previousPrimaryLines, currentPrimaryLines) = (currentPrimaryLines, previousPrimaryLines);
        }

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));

        for (var i = 0; i < result.Count; i++)
        {
            result[i].Duration = i < result.Count - 1
                ? result[i + 1].StartTime - result[i].StartTime
                : 5000;

            EnhancedLrcWordParser.CompleteWordDurations(result[i]);
        }

        return result;
    }

    private static LyricLineViewModel? FindTranslationTarget(
        List<LyricLineViewModel> previousPrimaryLines,
        double startTime)
    {
        foreach (var line in previousPrimaryLines)
        {
            if (line.StartTime == startTime && string.IsNullOrEmpty(line.Translation))
                return line;
        }

        return null;
    }

    private static long ParseTimestamp(ReadOnlySpan<char> timestamp)
    {
        var value = timestamp[1..^1];
        var colonIndex = value.IndexOf(':');
        var minutes = int.Parse(value[..colonIndex]);
        var seconds = int.Parse(value.Slice(colonIndex + 1, 2));
        var milliseconds = 0;

        if (value.Length > colonIndex + 3)
        {
            var millisecondText = value[(colonIndex + 4)..];
            milliseconds = int.Parse(millisecondText);
            if (millisecondText.Length == 1) milliseconds *= 100;
            else if (millisecondText.Length == 2) milliseconds *= 10;
            else if (millisecondText.Length == 4) milliseconds /= 10;
        }

        return minutes * 60000L + seconds * 1000L + milliseconds;
    }

    private static LyricLineViewModel CreateLine(string text, long startTime)
    {
        var line = new LyricLineViewModel
        {
            Content = EnhancedLrcWordParser.StripTags(text),
            StartTime = startTime,
            Translation = "",
            IsActive = false
        };

        EnhancedLrcWordParser.MapWords(line, text);
        return line;
    }
}
