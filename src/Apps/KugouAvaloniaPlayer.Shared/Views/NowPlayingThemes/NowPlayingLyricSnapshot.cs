using System;
using System.Collections.Generic;
using AvaloniaLyrics;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaSilkEffects.Sonnet;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

/// <summary>One lyric revision, with lazily projected theme inputs. Line objects themselves are immutable.</summary>
internal sealed class NowPlayingLyricSnapshot(IReadOnlyList<LyricLine> lines, double durationSeconds)
{
    private IReadOnlyList<LumiereLine>? _lumiere;
    private IReadOnlyList<SonnetLine>? _sonnet;

    public IReadOnlyList<LumiereLine> LumiereLines => _lumiere ??= Project(
        static (text, start, end) => new LumiereWord(text, start, end),
        static (text, start, end, words) => new LumiereLine(text, start, end, words));

    public IReadOnlyList<SonnetLine> SonnetLines => _sonnet ??= BuildSonnet();

    private IReadOnlyList<SonnetLine> BuildSonnet()
    {
        var result = Project(
            static (text, start, end) => new SonnetWordTiming(text, start, end),
            static (text, start, end, words) => new SonnetLine(text, start, end, words));
        return result.Count == 0 ? SonnetStaffView.VirtualLines() : result;
    }

    private List<TLine> Project<TWord, TLine>(Func<string, double, double, TWord> wordFactory,
        Func<string, double, double, TWord[], TLine> lineFactory)
    {
        var result = new List<TLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            var start = Math.Max(0, line.Start.TotalSeconds);
            var end = line.Duration > TimeSpan.Zero ? start + line.Duration.TotalSeconds :
                index + 1 < lines.Count ? Math.Max(start + 0.1, lines[index + 1].Start.TotalSeconds) :
                Math.Max(start + 4, durationSeconds);
            var words = new TWord[line.Words.Count];
            for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
            {
                var word = line.Words[wordIndex];
                var wordStart = Math.Max(start, word.Start.TotalSeconds);
                words[wordIndex] = wordFactory(word.Text, wordStart, wordStart + Math.Max(0.01, word.Duration.TotalSeconds));
            }
            result.Add(lineFactory(line.Text, start, Math.Max(start + 0.1, end), words));
        }
        return result;
    }
}
