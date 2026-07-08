using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     搜索专辑结果。
/// </summary>
public record SearchAuthorResponse : KgBaseModel
{
    /// <summary>
    ///     匹配到的总记录数。
    /// </summary>
    [property: JsonPropertyName("total")] public int Total { get; set; }

    /// <summary>
    ///     当前页歌手列表。
    /// </summary>
    [property: JsonPropertyName("lists")] public List<SearchAuthorItem> Author { get; set; } = [];
}

/// <summary>
///     搜索结果中的专辑信息。
/// </summary>
public record SearchAuthorItem : KgBaseModel
{
    /// <summary>
    ///     歌手 ID。
    /// </summary>
    [property: JsonPropertyName("AuthorId")]
    public long AuthorId { get; set; }

    /// <summary>
    ///     歌手名称。
    /// </summary>
    [property: JsonPropertyName("AuthorName")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     收录歌曲数。
    /// </summary>
    [property: JsonPropertyName("AudioCount")]
    public int AudioCount { get; set; }

    /// <summary>
    ///     发布时间。
    /// </summary>
    [property: JsonPropertyName("publish_time")]
    public string PublishTime { get; set; } = "";

    /// <summary>
    ///     热度。
    /// </summary>
    [property: JsonPropertyName("Heat")]
    public long Heat { get; set; }

    /// <summary>
    ///     封面图地址。
    /// </summary>
    [property: JsonPropertyName("Avatar")]
    public string? Cover { get; set; }
}