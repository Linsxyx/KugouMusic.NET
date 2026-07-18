using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.util;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     推荐歌单响应数据 (对应 data 节点)
/// </summary>
public record RecommendPlaylistResponse : KgBaseModel
{
    /// <summary>
    ///     是否还有下一页。
    /// </summary>
    [property: JsonPropertyName("has_next")]
    public int HasNext { get; set; }

    /// <summary>
    ///     推荐歌单列表。
    /// </summary>
    [property: JsonPropertyName("special_list")]
    public List<RecommendPlaylistItem> Playlists { get; set; } = new();
}

/// <summary>
///     推荐歌单信息
/// </summary>
public record RecommendPlaylistItem : KgBaseModel
{
    /// <summary>
    ///     歌单数字 ID
    /// </summary>
    [property: JsonPropertyName("specialid")]
    public long ListId { get; set; }

    /// <summary>
    ///     歌单全局 ID (最常用的请求参数)
    /// </summary>
    [property: JsonPropertyName("global_collection_id")]
    public string GlobalId { get; set; } = "";

    /// <summary>
    ///     歌单名称
    /// </summary>
    [property: JsonPropertyName("specialname")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     创建者昵称
    /// </summary>
    [property: JsonPropertyName("nickname")]
    public string CreatorName { get; set; } = "";

    /// <summary>
    ///     创建者 ID
    /// </summary>
    [property: JsonPropertyName("suid")]
    public long CreatorId { get; set; }

    /// <summary>
    ///     播放量
    /// </summary>
    [property: JsonPropertyName("play_count")]
    public long PlayCount { get; set; }

    /// <summary>
    ///     收藏数
    /// </summary>
    [property: JsonPropertyName("collectcount")]
    public long CollectCount { get; set; }

    /// <summary>
    ///     简介
    /// </summary>
    [property: JsonPropertyName("intro")]
    public string Intro { get; set; } = "";

    /// <summary>
    ///     封面 (自动处理 {size})
    /// </summary>
    [property: JsonPropertyName("flexible_cover")]
    public string? Cover
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    /// <summary>
    ///     标签列表
    /// </summary>
    [property: JsonPropertyName("tags")]
    [property: JsonConverter(typeof(RecommendPlaylistTagListJsonConverter))]
    public List<PlaylistTagItem> Tags { get; set; } = new();
}

/// <summary>
///     兼容推荐歌单 tags 字段偶发返回 [] / "" / null / {}。
/// </summary>
public sealed class RecommendPlaylistTagListJsonConverter : JsonConverter<List<PlaylistTagItem>>
{
    public override List<PlaylistTagItem> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return [];
            case JsonTokenType.StartArray:
                return JsonSerializer.Deserialize(ref reader, AppJsonContext.Default.ListPlaylistTagItem) ?? [];
            case JsonTokenType.String when string.IsNullOrWhiteSpace(reader.GetString()):
                return [];
            case JsonTokenType.StartObject:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                if (!document.RootElement.EnumerateObject().Any())
                    return [];

                throw new JsonException("Unexpected non-empty object when parsing recommended playlist tags.");
            }
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} when parsing recommended playlist tags.");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<PlaylistTagItem> value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, AppJsonContext.Default.ListPlaylistTagItem);
    }
}
