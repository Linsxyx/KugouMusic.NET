using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应 /v7/get_all_list 接口的 data 节点
/// </summary>
public record UserPlaylistResponse : KgBaseModel
{
    [JsonPropertyName("userid")] public long UserId { get; set; }

    [JsonPropertyName("list_count")] public int ListCount { get; set; }

    // 核心列表数据
    [JsonPropertyName("info")] public List<UserPlaylistItem> Playlists { get; set; } = new();
}

/// <summary>
///     单个用户歌单信息
/// </summary>
public record UserPlaylistItem : KgBaseModel
{
    // 歌单名称
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    // 内部 ID (数字)
    [JsonPropertyName("listid")] public long ListId { get; set; }

    // ★★★ 全局 ID (用于获取歌曲) ★★★
    [JsonPropertyName("global_collection_id")]
    public string GlobalId { get; set; } = "";

    // 歌曲数量
    [JsonPropertyName("count")] public int Count { get; set; }

    // 封面图 (可能为空字符串)
    [JsonPropertyName("pic")]
    public string? Pic
    {
        get => field?.Replace("{size}", "600");
        set;
    }

    // 是否默认歌单 (1=默认收藏, 2=我喜欢, 0=自建)
    [JsonPropertyName("is_def")] public int IsDefault { get; set; }

    // 创建时间
    [JsonPropertyName("create_time")] public long CreateTime { get; set; }
}