using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应 /v7/get_all_list 接口的 data 节点
/// </summary>
public record UserPlaylistResponse : KgBaseModel
{
    /// <summary>
    ///     当前登录用户 ID。
    /// </summary>
    [JsonPropertyName("userid")] public long UserId { get; set; }

    /// <summary>
    ///     当前用户歌单总数。
    /// </summary>
    [JsonPropertyName("list_count")] public int ListCount { get; set; }

    /// <summary>
    ///     当前页歌单列表。
    /// </summary>
    [JsonPropertyName("info")] public List<UserPlaylistItem> Playlists { get; set; } = new();
}

/// <summary>
///     单个用户歌单信息
/// </summary>
public record UserPlaylistItem : KgBaseModel
{
    /// <summary>
    ///     歌单名称。
    /// </summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>
    ///     歌单数字 ID。
    /// </summary>
    [JsonPropertyName("listid")] public long ListId { get; set; }

    /// <summary>
    ///     歌单全局 ID。
    /// </summary>
    [JsonPropertyName("global_collection_id")]
    public string GlobalId { get; set; } = "";

    /// <summary>
    ///     创建来源关联 ID。
    /// </summary>
    [JsonPropertyName("list_create_gid")] public string ListCreateId { get; set; } = "";

    /// <summary>
    ///     创建来源歌单 ID。
    /// </summary>
    [JsonPropertyName("list_create_listid")]
    public long ListCreateListId { get; set; }

    /// <summary>
    ///     歌曲数量。
    /// </summary>
    [JsonPropertyName("count")] public int Count { get; set; }

    /// <summary>
    ///     歌单封面图。
    /// </summary>
    [JsonPropertyName("pic")]
    public string? Pic
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    /// <summary>
    ///     是否默认歌单。`1` 表示默认收藏，`2` 表示我喜欢，`0` 表示自建。
    /// </summary>
    [JsonPropertyName("is_def")] public int IsDefault { get; set; }

    /// <summary>
    ///     创建时间戳。
    /// </summary>
    [JsonPropertyName("create_time")] public long CreateTime { get; set; }

    /// <summary>
    ///     歌单类型。
    /// </summary>
    [JsonPropertyName("type")] public int Type { get; set; }

    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [JsonPropertyName("musiclib_id")] public long MusiclibId { get; set; }

    /// <summary>
    ///     创建者用户名。
    /// </summary>
    [JsonPropertyName("list_create_username")]
    public string ListCreateUsername { get; set; } = "";

    /// <summary>
    ///     是否为收藏专辑生成的歌单。
    /// </summary>
    [JsonIgnore]
    public bool IsCollectedAlbum => string.IsNullOrWhiteSpace(ListCreateId);

    /// <summary>
    ///     专辑 ID。优先使用 `MusiclibId`，否则退回到 `ListCreateListId`。
    /// </summary>
    [JsonIgnore]
    public long AlbumId => MusiclibId > 0 ? MusiclibId : ListCreateListId;
}
