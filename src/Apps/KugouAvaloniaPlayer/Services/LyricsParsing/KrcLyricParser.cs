using System;
using System.Collections.Generic;
using KuGou.Net.Adapters.Lyrics;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class KrcLyricParser : ILyricParser
{
    public string Extension => ".krc";

    public List<LyricLineViewModel> Parse(string content)
    {
        var result = new List<LyricLineViewModel>();
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

            MapWords(lyricLine, line.Words);
            result.Add(lyricLine);
        }

        result.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return result;
    }

    private static void MapWords(LyricLineViewModel line, IReadOnlyList<KrcWord> words)
    {
        if (words.Count == 0)
            return;

        line.IsKrcWordLevel = true;
        foreach (var word in words)
        {
            line.Words.Add(new LyricWordViewModel
            {
                Text = word.Text,
                StartTime = word.StartTime,
                Duration = word.Duration
            });
        }

        MapTranslationWords(line);
    }

    private static void MapTranslationWords(LyricLineViewModel line)
    {
        if (string.IsNullOrWhiteSpace(line.Translation) || line.Duration <= 0)
            return;

        var chars = line.Translation.ToCharArray();
        if (chars.Length == 0)
            return;

        line.HasWordLevelTranslation = true;

        var perCharDuration = Math.Max(40, line.Duration / chars.Length);
        for (var i = 0; i < chars.Length; i++)
        {
            var startTime = line.StartTime + i * perCharDuration;
            if (startTime > line.StartTime + line.Duration)
                startTime = line.StartTime + line.Duration;

            line.TranslationWords.Add(new LyricWordViewModel
            {
                Text = chars[i].ToString(),
                StartTime = startTime,
                Duration = perCharDuration
            });
        }
    }
}
