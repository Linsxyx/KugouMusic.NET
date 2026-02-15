using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuGou.Net.util;

namespace KuGou.Net.Adapters.Lyrics;

public static class KrcParser
{
    /// <summary>
    ///     解析 KRC 文本 (包含翻译和音译提取)
    /// </summary>
    /// <param name="krcText">解密后的 krc 字符串 (decodeContent)</param>
    public static KrcLyric Parse(string krcText)
    {
        var result = new KrcLyric();
        if (string.IsNullOrEmpty(krcText)) return result;

        var lines = krcText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        List<List<string>>? translationList = null;
        List<List<string>>? romanizationList = null;


        foreach (var line in lines)
        {
            if (line.StartsWith("[language:"))
            {
                try
                {
                    var base64 = line.Substring(10, line.Length - 11);


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

            var metaMatch = Regex.Match(line, @"^\[([a-zA-Z]+):(.*)\]$");
            if (metaMatch.Success)
            {
                var key = metaMatch.Groups[1].Value;
                var val = metaMatch.Groups[2].Value;
                result.MetaData[key] = val;
            }
        }


        var lineIndex = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("[") && line.Contains(":") && !Regex.IsMatch(line, @"^\[\d+,\d+\]"))
                continue;

            var match = Regex.Match(line, @"^\[(\d+),(\d+)\](.*)");
            if (!match.Success) continue;

            var startTime = long.Parse(match.Groups[1].Value);
            var duration = long.Parse(match.Groups[2].Value);
            var rawContent = match.Groups[3].Value;

            var krcLine = new KrcLine
            {
                StartTime = startTime,
                Duration = duration
            };


            var wordMatches = Regex.Matches(rawContent, @"<(\d+),(\d+),\d+>([^<]+)");
            var sbContent = new StringBuilder();

            if (wordMatches.Count > 0)
                foreach (Match wm in wordMatches)
                {
                    var wStartOffset = long.Parse(wm.Groups[1].Value);
                    var wDuration = long.Parse(wm.Groups[2].Value);
                    var wText = wm.Groups[3].Value;

                    krcLine.Words.Add(new KrcWord
                    {
                        Text = wText,
                        StartTime = startTime + wStartOffset,
                        Duration = wDuration
                    });
                    sbContent.Append(wText);
                }
            else

                sbContent.Append(rawContent);

            krcLine.Content = sbContent.ToString();


            if (translationList != null && lineIndex < translationList.Count)
            {
                var tLines = translationList[lineIndex];
                if (tLines != null && tLines.Count > 0) krcLine.Translation = tLines[0];
            }

            if (romanizationList != null && lineIndex < romanizationList.Count)
            {
                var rLines = romanizationList[lineIndex];
                if (rLines != null) krcLine.Romanization = string.Join("", rLines);
            }

            result.Lines.Add(krcLine);
            lineIndex++;
        }

        return result;
    }
}