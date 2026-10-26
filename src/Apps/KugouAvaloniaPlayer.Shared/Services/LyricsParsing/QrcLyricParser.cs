using System.Collections.Generic;
using ZLinq;
using System.Text.RegularExpressions;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class QrcLyricParser : ILyricParser
{
    private static readonly Regex LineRegex = new(@"^\[(\d+),(\d+)\](.*)$");
    private static readonly Regex WordRegex = new(@"(.*?)\((\d+),(\d+)\)");

    public string Extension => ".qrc";

    public List<LyricLineViewModel> Parse(string content)
    {
        var result = new List<LyricLineViewModel>();
        var lines = content.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var match = LineRegex.Match(line);
            if (!match.Success)
                continue;

            var lyricLine = new LyricLineViewModel
            {
                StartTime = long.Parse(match.Groups[1].Value),
                Duration = long.Parse(match.Groups[2].Value),
                Translation = "",
                IsActive = false,
                IsKrcWordLevel = true
            };

            var contentPart = match.Groups[3].Value;
            foreach (Match wordMatch in WordRegex.Matches(contentPart))
            {
                var text = wordMatch.Groups[1].Value;
                if (string.IsNullOrEmpty(text))
                    continue;

                var startTime = double.Parse(wordMatch.Groups[2].Value);
                var duration = double.Parse(wordMatch.Groups[3].Value);

                lyricLine.Words.Add(new LyricWordViewModel
                {
                    Text = text,
                    StartTime = startTime,
                    Duration = duration
                });
            }

            lyricLine.Content = string.Concat(lyricLine.Words.AsValueEnumerable().Select(static x => x.Text).ToList());
            if (!string.IsNullOrWhiteSpace(lyricLine.Content))
                result.Add(lyricLine);
        }

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return result;
    }
}
