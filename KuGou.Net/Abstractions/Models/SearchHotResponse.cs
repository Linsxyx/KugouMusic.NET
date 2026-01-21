using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应 data 节点的数据
/// </summary>
public record SearchHotResponse
{
    [property: JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    // 映射 JSON 中的 "list" 到更具语义的 "Categories"
    [property: JsonPropertyName("list")] public List<SearchHotCategory> Categories { get; set; } = new();
}

/// <summary>
///     榜单分类（如：热搜榜、飙升榜）
/// </summary>
public record SearchHotCategory
{
    [property: JsonPropertyName("name")] public string Name { get; set; } = "";

    [property: JsonPropertyName("keywords")]
    public List<SearchHotKeyword> Keywords { get; set; } = new();
}

/// <summary>
///     具体的热搜关键词项
/// </summary>
public record SearchHotKeyword
{
    [property: JsonPropertyName("keyword")]
    public string Keyword { get; set; } = "";

    [property: JsonPropertyName("reason")] public string Reason { get; set; } = "";

    [property: JsonPropertyName("jumpurl")]
    public string JumpUrl { get; set; } = "";

    [property: JsonPropertyName("json_url")]
    public string JsonUrl { get; set; } = "";

    [property: JsonPropertyName("is_cover_word")]
    public int IsCoverWord { get; set; }

    [property: JsonPropertyName("type")] public int Type { get; set; }

    [property: JsonPropertyName("icon")] public int Icon { get; set; }
}