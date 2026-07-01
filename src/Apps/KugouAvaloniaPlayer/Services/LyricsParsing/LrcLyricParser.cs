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
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<LyricLineViewModel>();

        foreach (var line in lines)
        {
            var matches = TimestampRegex.Matches(line);
            if (matches.Count == 0)
                continue;

            var text = line[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
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

                var time = minutes * 60000 + seconds * 1000 + milliseconds;
                result.Add(CreateLine(text, time));
            }
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
