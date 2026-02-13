using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Adapters.Lyrics;

/// <summary>
///     最终解析出来的歌词对象
/// </summary>
public class KrcLyric
{
    public List<KrcLine> Lines { get; set; } = new();
    public Dictionary<string, string> MetaData { get; set; } = new();
}

/// <summary>
///     每一行歌词
/// </summary>
public class KrcLine
{
    public long StartTime { get; set; } // 毫秒
    public long Duration { get; set; } // 毫秒
    public string Content { get; set; } = ""; // 原文
    public string Translation { get; set; } = ""; // 翻译
    public string Romanization { get; set; } = ""; // 音译/罗马音
    public List<KrcWord> Words { get; set; } = new(); // 逐字数据
}

/// <summary>
///     逐字歌词详情
/// </summary>
public class KrcWord
{
    public string Text { get; set; } = "";
    public long StartTime { get; set; }
    public long Duration { get; set; }
}

// ============ 下面是用于反序列化 [language:...] JSON 的内部类 ============

internal class LanguageContainer
{
    [property: JsonPropertyName("content")]
    public List<LanguageSection>? Content { get; set; }
}

internal class LanguageSection
{
    [property: JsonPropertyName("type")] public int Type { get; set; } // 1: 翻译, 0: 音译

    // 注意：酷狗返回的 lyricContent 是字符串数组的数组 [ ["第一行翻译"], ["第二行翻译"] ]
    [property: JsonPropertyName("lyricContent")]
    public List<List<string>>? LyricContent { get; set; }
}

public record LyricResult(
    string? RawContent,
    string? DecodedContent,
    string? DecodedTranslation,
    JsonElement RawJson
);