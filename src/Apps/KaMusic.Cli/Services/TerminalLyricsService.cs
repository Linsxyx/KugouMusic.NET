using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ATL;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KaMusic.Cli.Models;

namespace KaMusic.Cli.Services;

// 歌词加载与窗口采样：支持原文、翻译、罗马音、双行组合，以及 KRC 逐字高亮进度计算与本地音频内嵌/同目录歌词解析。

internal sealed class TerminalLyricsService(LyricClient lyricClient)
{
    private const string EmbeddedKugouLyricPrefix = "KUGOU_EMBEDDED_LYRIC:";
    private static readonly Regex LrcTimestampRegex = new(@"\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:[.:](?<ms>\d{1,3}))?\]", RegexOptions.Compiled);

    private KrcLyric? _current;

    public async Task LoadSongLyricsAsync(TerminalSongItem song)
    {
        _current = null;
        if (song.IsLocalFile && !string.IsNullOrWhiteSpace(song.LocalFilePath))
        {
            var localLyric = await TryLoadLocalLyricsAsync(song.LocalFilePath);
            if (localLyric is { Lines.Count: > 0 })
            {
                _current = localLyric;
                return;
            }
        }

        var searchHash = song.IsLocalFile || song.IsRemoteStream ? "" : song.Hash;
        await LoadOnlineLyricsAsync(searchHash, $"{song.Singer} - {song.Name}".Trim(' ', '-'));
    }

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
                _current = ParseLyricContent(lyricResult.DecodedContent);
            }
        }
        catch
        {
            _current = null;
        }
    }

    // 从本地音频同目录的 .krc / .lrc 文件或音频内嵌标签中读取并解析歌词。
    private static async Task<KrcLyric?> TryLoadLocalLyricsAsync(string audioFilePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(audioFilePath);
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(baseName))
            {
                var krcPath = Path.Combine(dir, baseName + ".krc");
                if (File.Exists(krcPath))
                {
                    var text = await File.ReadAllTextAsync(krcPath);
                    var parsed = ParseLyricContent(text);
                    if (parsed is { Lines.Count: > 0 })
                    {
                        return parsed;
                    }
                }

                var lrcPath = Path.Combine(dir, baseName + ".lrc");
                if (File.Exists(lrcPath))
                {
                    var text = await File.ReadAllTextAsync(lrcPath);
                    var parsed = ParseLyricContent(text);
                    if (parsed is { Lines.Count: > 0 })
                    {
                        return parsed;
                    }
                }
            }

            return await Task.Run(() =>
            {
                var track = new Track(audioFilePath);
                if (track.Lyrics is null || track.Lyrics.Count == 0)
                {
                    return null;
                }

                foreach (var lyricsInfo in track.Lyrics)
                {
                    if (!string.IsNullOrWhiteSpace(lyricsInfo.UnsynchronizedLyrics))
                    {
                        var parsed = ParseEmbeddedRawLyric(lyricsInfo.UnsynchronizedLyrics);
                        if (parsed is { Lines.Count: > 0 })
                        {
                            return parsed;
                        }
                    }

                    if (lyricsInfo.SynchronizedLyrics is { Count: > 0 })
                    {
                        var krc = new KrcLyric();
                        foreach (var phrase in lyricsInfo.SynchronizedLyrics.OrderBy(p => p.TimestampStart))
                        {
                            krc.Lines.Add(new KrcLine
                            {
                                StartTime = phrase.TimestampStart,
                                Duration = phrase.TimestampEnd > phrase.TimestampStart
                                    ? phrase.TimestampEnd - phrase.TimestampStart
                                    : 2500,
                                Content = phrase.Text ?? ""
                            });
                        }

                        if (krc.Lines.Count > 0)
                        {
                            return krc;
                        }
                    }
                }

                return null;
            });
        }
        catch
        {
            return null;
        }
    }

    private static KrcLyric? ParseEmbeddedRawLyric(string rawText)
    {
        var trimmed = rawText.Trim();
        if (trimmed.StartsWith(EmbeddedKugouLyricPrefix, StringComparison.Ordinal))
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed[EmbeddedKugouLyricPrefix.Length..].Trim()));
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("RawLyricContent", out var contentProp) ||
                    doc.RootElement.TryGetProperty("rawLyricContent", out contentProp))
                {
                    var rawContent = contentProp.GetString();
                    if (!string.IsNullOrWhiteSpace(rawContent))
                    {
                        return ParseLyricContent(rawContent);
                    }
                }
            }
            catch
            {
                // 降级为普通文本解析。
            }
        }

        return ParseLyricContent(trimmed);
    }

    private static KrcLyric? ParseLyricContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var krc = KrcParser.Parse(content);
        if (krc.Lines.Count > 0)
        {
            return krc;
        }

        // 标准 LRC 时间轴解析（[mm:ss.xx]）
        var parsedLines = new List<KrcLine>();
        foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var matches = LrcTimestampRegex.Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var lastMatch = matches[^1];
            var text = rawLine[(lastMatch.Index + lastMatch.Length)..].Trim();
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
                    !int.TryParse(match.Groups["s"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    continue;
                }

                var msStr = match.Groups["ms"].Value;
                var millis = msStr.Length switch
                {
                    1 => int.Parse(msStr, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(msStr, CultureInfo.InvariantCulture) * 10,
                    3 => int.Parse(msStr, CultureInfo.InvariantCulture),
                    _ => 0
                };

                var startMs = (minutes * 60 + seconds) * 1000 + millis;
                parsedLines.Add(new KrcLine
                {
                    StartTime = startMs,
                    Duration = 3000,
                    Content = text
                });
            }
        }

        if (parsedLines.Count == 0)
        {
            return null;
        }

        var ordered = parsedLines.OrderBy(l => l.StartTime).ToList();
        var merged = new List<KrcLine>(ordered.Count);
        foreach (var item in ordered)
        {
            if (merged.Count > 0 && Math.Abs(merged[^1].StartTime - item.StartTime) <= 20 && !string.IsNullOrWhiteSpace(item.Content))
            {
                if (string.IsNullOrWhiteSpace(merged[^1].Translation))
                {
                    merged[^1].Translation = item.Content;
                    continue;
                }
            }

            merged.Add(item);
        }

        for (var i = 0; i < merged.Count - 1; i++)
        {
            merged[i].Duration = Math.Max(200, merged[i + 1].StartTime - merged[i].StartTime);
        }

        return new KrcLyric { Lines = merged };
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

        for (var offset = -3; offset <= 3; offset++)
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

    /// <summary>
    ///     获取带主副行与逐字进度的歌词窗口，用于侧栏和沉浸页。
    /// </summary>
    public IReadOnlyList<TerminalLyricLineView> GetRichWindow(TimeSpan position, TerminalLyricMode mode, int radius = 4)
    {
        var lines = _current?.Lines;
        if (lines == null || lines.Count == 0)
        {
            return [];
        }

        var currentMs = position.TotalMilliseconds;
        var currentIndex = FindCurrentIndex(lines, currentMs);
        var result = new List<TerminalLyricLineView>(radius * 2 + 1);

        for (var offset = -radius; offset <= radius; offset++)
        {
            var index = currentIndex + offset;
            if (index < 0 || index >= lines.Count)
            {
                result.Add(new TerminalLyricLineView { Offset = offset, Primary = "" });
                continue;
            }

            var line = lines[index];
            var secondary = mode switch
            {
                TerminalLyricMode.Translation => EmptyToNull(line.Translation),
                TerminalLyricMode.Romanization => EmptyToNull(line.Romanization),
                TerminalLyricMode.Combined => EmptyToNull(string.Join(" · ",
                    new[] { line.Translation, line.Romanization }.Where(x => !string.IsNullOrWhiteSpace(x)))),
                _ => null
            };

            result.Add(new TerminalLyricLineView
            {
                Offset = offset,
                IsCurrent = offset == 0,
                Primary = line.Content,
                Secondary = secondary,
                Words = offset == 0 ? BuildWordProgress(line, currentMs) : []
            });
        }

        return result;
    }

    private static IReadOnlyList<(string Text, float Progress)> BuildWordProgress(KrcLine line, double currentMs)
    {
        if (line.Words.Count == 0)
        {
            return [];
        }

        // KrcParser 输出的 KrcWord.StartTime 已是绝对毫秒时间（行起始 + 字偏移），直接与播放位置比较。
        var words = new List<(string Text, float Progress)>(line.Words.Count);
        foreach (var word in line.Words)
        {
            var progress = word.Duration <= 0
                ? currentMs >= word.StartTime ? 1f : 0f
                : (float)Math.Clamp((currentMs - word.StartTime) / word.Duration, 0.0, 1.0);
            words.Add((word.Text, progress));
        }

        return words;
    }

    private static string? EmptyToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
