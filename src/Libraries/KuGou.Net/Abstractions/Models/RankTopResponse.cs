using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     榜单列表结果。
/// </summary>
public record RankLTopResponse : KgBaseModel
{
    /// <summary>
    ///     榜单列表。
    /// </summary>
    [property: JsonPropertyName("list")] public List<RankListItem> Info { get; set; } = [];
}