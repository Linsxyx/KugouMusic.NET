using System.Collections.Generic;
using ZLinq;
using System.Text.Json;
using System.Text.RegularExpressions;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class YrcLyricParser : ILyricParser
{
    private static readonly Regex LyricLineRegex = new(@"^\[(\d+),(\d+)\](.*)$");
    private static readonly Regex WordRegex = new(@"\((\d+),(\d+),\d+\)([^()]+)");

    public string Extension => ".yrc";

    public List<LyricLineViewModel> Parse(string content)
    {
        var result = new List<LyricLineViewModel>();
        var lines = content.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("{"))
            {
                if (TryParseCreditLine(line, out var creditLine))
                    result.Add(creditLine);

                continue;
            }

            var match = LyricLineRegex.Match(line);
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

            foreach (Match wordMatch in WordRegex.Matches(match.Groups[3].Value))
            {
                var text = wordMatch.Groups[3].Value;
                if (string.IsNullOrEmpty(text))
                    continue;

                lyricLine.Words.Add(new LyricWordViewModel
                {
                    Text = text,
                    StartTime = double.Parse(wordMatch.Groups[1].Value),
                    Duration = double.Parse(wordMatch.Groups[2].Value)
                });
            }

            lyricLine.Content = string.Concat(lyricLine.Words.AsValueEnumerable().Select(static x => x.Text).ToList());
            if (!string.IsNullOrWhiteSpace(lyricLine.Content))
                result.Add(lyricLine);
        }

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return result;
    }

    private static bool TryParseCreditLine(string line, out LyricLineViewModel lyricLine)
    {
        lyricLine = new LyricLineViewModel();

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("t", out var timestampElement) ||
                !root.TryGetProperty("c", out var creditsElement) ||
                creditsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var text = string.Concat(
                creditsElement.EnumerateArray()
                    .AsValueEnumerable().Where(static x => x.TryGetProperty("tx", out _))
                    .Select(static x => x.GetProperty("tx").GetString() ?? string.Empty).ToList());

            if (string.IsNullOrWhiteSpace(text))
                return false;

            lyricLine = new LyricLineViewModel
            {
                Content = text,
                StartTime = timestampElement.GetDouble(),
                Duration = 3000,
                Translation = "",
                IsActive = false
            };

            return true;
        }
        catch
        {
            return false;
        }
    }
}
