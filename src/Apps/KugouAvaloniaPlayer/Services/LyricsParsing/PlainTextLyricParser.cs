using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal sealed class PlainTextLyricParser : ILyricParser
{
    private const int PlainLineDurationMs = 5000;

    public string Extension => ".txt";

    public List<LyricLineViewModel> Parse(string content)
    {
        var result = new List<LyricLineViewModel>();
        var plainLines = content.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);

        var index = 0;
        foreach (var rawLine in plainLines)
        {
            var text = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Add(new LyricLineViewModel
            {
                Content = text,
                StartTime = index * PlainLineDurationMs,
                Duration = PlainLineDurationMs,
                Translation = "",
                IsActive = false
            });

            index++;
        }

        return result;
    }
}
