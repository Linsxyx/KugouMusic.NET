using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuGou.Net.util;

namespace KuGou.Net.Adapters.Lyrics;

public static class KrcParser
{
    private static readonly Regex WordRegex = new(@"<(\d+),(\d+),\d+>([^<]+)");

    /// <summary>
    ///     解析 KRC 文本 (包含翻译和音译提取)
    /// </summary>
    /// <param name="krcText">解密后的 krc 字符串 (decodeContent)</param>
    public static KrcLyric Parse(string krcText)
    {
        var result = new KrcLyric();
        if (string.IsNullOrEmpty(krcText)) return result;

        var source = krcText.AsSpan();

        List<List<string>>? translationList = null;
        List<List<string>>? romanizationList = null;

        foreach (var lineRange in source.Split('\n'))
        {
            var line = source[lineRange];
            if (lineRange.End.GetOffset(source.Length) < source.Length && line.EndsWith('\r'))
                line = line[..^1];

            if (line.IsEmpty)
                continue;

            if (line.StartsWith("[language:", StringComparison.Ordinal))
            {
                try
                {
                    var base64 = line[10..^1].ToString();


                    base64 = base64.Replace("-", "+").Replace("_", "/");
                    var mod4 = base64.Length % 4;
                    if (mod4 > 0) base64 += new string('=', 4 - mod4);

                    var jsonBytes = Convert.FromBase64String(base64);
                    var jsonStr = Encoding.UTF8.GetString(jsonBytes);

                    var langData = JsonSerializer.Deserialize(jsonStr, AppJsonContext.Default.LanguageContainer);

                    if (langData?.Content != null)
                    {
                        var transSection = langData.Content.FirstOrDefault(x => x.Type == 1);
                        if (transSection != null) translationList = transSection.LyricContent;


                        var romSection = langData.Content.FirstOrDefault(x => x.Type == 0);
                        if (romSection != null) romanizationList = romSection.LyricContent;
                    }
                }
                catch
                {
                    // 解析失败忽略，不影响主歌词
                }

                continue;
            }

            if (TryGetMetadataRanges(line, out var keyRange, out var valueRange))
            {
                var key = line[keyRange].ToString();
                var val = line[valueRange].ToString();
                result.MetaData[key] = val;
            }
        }


        var lineIndex = 0;
        foreach (var lineRange in source.Split('\n'))
        {
            var line = source[lineRange];
            if (lineRange.End.GetOffset(source.Length) < source.Length && line.EndsWith('\r'))
                line = line[..^1];

            if (line.IsEmpty || !TryParseLine(line, out var startTime, out var duration, out var contentStart))
                continue;

            var rawContent = line[contentStart..];

            var krcLine = new KrcLine
            {
                StartTime = startTime,
                Duration = duration
            };

            StringBuilder? contentBuilder = null;
            foreach (var wordMatch in WordRegex.EnumerateMatches(rawContent))
            {
                var match = rawContent.Slice(wordMatch.Index, wordMatch.Length);
                var tagEnd = match.IndexOf('>');
                var tag = match[1..tagEnd];
                var firstComma = tag.IndexOf(',');
                var secondComma = tag[(firstComma + 1)..].IndexOf(',') + firstComma + 1;
                var wStartOffset = long.Parse(tag[..firstComma]);
                var wDuration = long.Parse(tag[(firstComma + 1)..secondComma]);
                var wordText = match[(tagEnd + 1)..];

                krcLine.Words.Add(new KrcWord
                {
                    Text = wordText.ToString(),
                    StartTime = startTime + wStartOffset,
                    Duration = wDuration
                });
                (contentBuilder ??= new StringBuilder()).Append(wordText);
            }

            krcLine.Content = contentBuilder?.ToString() ?? rawContent.ToString();


            if (translationList != null && lineIndex < translationList.Count)
            {
                var tLines = translationList[lineIndex];
                if (tLines.Count > 0) krcLine.Translation = tLines[0];
            }

            if (romanizationList != null && lineIndex < romanizationList.Count)
            {
                var rLines = romanizationList[lineIndex];
                krcLine.Romanization = string.Join("", rLines);
            }

            result.Lines.Add(krcLine);
            lineIndex++;
        }

        return result;
    }

    private static bool TryGetMetadataRanges(ReadOnlySpan<char> line, out Range keyRange, out Range valueRange)
    {
        keyRange = default;
        valueRange = default;

        if (line.Length < 4 || line[0] != '[' || line[^1] != ']')
            return false;

        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 1)
            return false;

        foreach (var c in line[1..colonIndex])
        {
            if (c is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z'))
                return false;
        }

        keyRange = 1..colonIndex;
        valueRange = (colonIndex + 1)..^1;
        return true;
    }

    private static bool TryParseLine(
        ReadOnlySpan<char> line,
        out long startTime,
        out long duration,
        out int contentStart)
    {
        startTime = 0;
        duration = 0;
        contentStart = 0;

        if (line.Length < 5 || line[0] != '[')
            return false;

        var commaIndex = line.IndexOf(',');
        if (commaIndex <= 1)
            return false;

        var closingBracketOffset = line[(commaIndex + 1)..].IndexOf(']');
        if (closingBracketOffset < 1)
            return false;

        var closingBracketIndex = commaIndex + 1 + closingBracketOffset;
        var startSpan = line[1..commaIndex];
        var durationSpan = line[(commaIndex + 1)..closingBracketIndex];
        if (!IsAsciiDigits(startSpan) ||
            !IsAsciiDigits(durationSpan) ||
            !long.TryParse(startSpan, out startTime) ||
            !long.TryParse(durationSpan, out duration))
        {
            return false;
        }

        contentStart = closingBracketIndex + 1;
        return true;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }
}
