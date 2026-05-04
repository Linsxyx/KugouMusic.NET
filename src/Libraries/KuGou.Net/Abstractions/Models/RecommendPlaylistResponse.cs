using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     推荐歌单响应数据 (对应 data 节点)
/// </summary>
public record RecommendPlaylistResponse : KgBaseModel
{
    [property: JsonPropertyName("has_next")]
    public int HasNext { get; set; }

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
    public List<PlaylistTagItem> Tags { get; set; } = new();
}