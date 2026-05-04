using System.Text.Json;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KgTest.Models;

namespace KgTest.Services;

internal sealed class TerminalLyricsService(LyricClient lyricClient)
{
    private KrcLyric? _current;

    public async Task LoadOnlineLyricsAsync(string hash, string name)
    {
        _current = null;
        try
        {
            var searchJson = await lyricClient.SearchLyricAsync(hash, null, name, "no");
            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) ||
                candidatesElem.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var bestMatch = candidatesElem.EnumerateArray().FirstOrDefault();
            if (bestMatch.ValueKind == JsonValueKind.Undefined)
            {
                return;
            }

            var id = bestMatch.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
            var key = bestMatch.TryGetProperty("accesskey", out var keyElem) ? keyElem.GetString() : null;
            var fmt = bestMatch.TryGetProperty("fmt", out var fmtElem) ? fmtElem.GetString() ?? "krc" : "krc";
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key))
            {
                return;
            }

            var lyricResult = await lyricClient.GetLyricAsync(id, key, fmt);
            if (!string.IsNullOrEmpty(lyricResult.DecodedContent))
            {
                _current = KrcParser.Parse(lyricResult.DecodedContent);
            }
        }
        catch
        {
            _current = null;
        }
    }

    public IReadOnlyList<string> GetWindow(TimeSpan position, TerminalLyricMode mode)
    {
        var lines = _current?.Lines;
        if (lines == null || lines.Count == 0)
        {
            return ["无歌词数据"];
        }

        var currentMs = position.TotalMilliseconds;
        var currentIndex = FindCurrentIndex(lines, currentMs);
        var window = new List<string>();

        for (var offset = -2; offset <= 2; offset++)
        {
            var index = currentIndex + offset;
            if (index < 0 || index >= lines.Count)
            {
                window.Add("");
                continue;
            }

            var prefix = offset == 0 ? "> " : "  ";
            window.Add(prefix + BuildLine(lines[index], mode));
        }

        return window;
    }

    private static int FindCurrentIndex(IReadOnlyList<KrcLine> lines, double currentMs)
    {
        var result = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartTime <= currentMs)
            {
                result = i;
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private static string BuildLine(KrcLine line, TerminalLyricMode mode)
    {
        return mode switch
        {
            TerminalLyricMode.Translation when !string.IsNullOrWhiteSpace(line.Translation) =>
                $"{line.Content} / {line.Translation}",
            TerminalLyricMode.Romanization when !string.IsNullOrWhiteSpace(line.Romanization) =>
                $"{line.Content} / {line.Romanization}",
            TerminalLyricMode.Combined => string.Join(" / ",
                new[] { line.Content, line.Translation, line.Romanization }.Where(x => !string.IsNullOrWhiteSpace(x))),
            _ => line.Content
        };
    }
}
