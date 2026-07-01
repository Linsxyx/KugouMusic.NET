using System;
using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal static class LyricParserRegistry
{
    private static readonly IReadOnlyDictionary<string, ILyricParser> Parsers =
        new Dictionary<string, ILyricParser>(StringComparer.OrdinalIgnoreCase)
        {
            [".krc"] = new KrcLyricParser(),
            [".lrc"] = new LrcLyricParser(),
            [".qrc"] = new QrcLyricParser(),
            [".vtt"] = new VttLyricParser(),
            [".yrc"] = new YrcLyricParser(),
            [".txt"] = new PlainTextLyricParser()
        };

    public static List<LyricLineViewModel> Parse(string content, string extension)
    {
        if (Parsers.TryGetValue(extension, out var parser))
            return parser.Parse(content);

        return Parsers[".txt"].Parse(content);
    }
}
