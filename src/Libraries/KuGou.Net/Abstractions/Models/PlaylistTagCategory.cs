using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌单标签分类大类 (如：场景、主题、语种等)
/// </summary>
public record PlaylistTagCategory : KgBaseModel
{
    [property: JsonPropertyName("parent_id")]
    public int ParentId { get; set; }

    [property: JsonPropertyName("sort")] public int Sort { get; set; }

    [property: JsonPropertyName("tag_id")] public int TagId { get; set; }

    [property: JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [property: JsonPropertyName("son")] public List<PlaylistTagItem> Children { get; set; } = new();
}

/// <summary>
///     具体的歌单标签项 (如：学习、工作、通勤等)
/// </summary>
public record PlaylistTagItem : KgBaseModel
{
    [property: JsonPropertyName("parent_id")]
    public int ParentId { get; set; }

    [property: JsonPropertyName("tag_id")] public int TagId { get; set; }

    [property: JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [property: JsonPropertyName("sort")] public int Sort { get; set; }
}