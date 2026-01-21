using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record PlaylistInfo : KgBaseModel
{
    // 歌单 ID (listid)
    [property: JsonPropertyName("listid")] public long Id { get; set; }

    // 全局 ID (这是后续调其他接口最常用的)
    [property: JsonPropertyName("global_collection_id")]
    public string GlobalId { get; set; } = "";

    // 歌单名称
    [property: JsonPropertyName("name")] public string Name { get; set; } = "";

    // 封面图片
    [property: JsonPropertyName("pic")] public string PicUrl { get; set; } = "";

    // 简介
    [property: JsonPropertyName("intro")] public string Intro { get; set; } = "";

    // 歌曲总数
    [property: JsonPropertyName("count")] public int SongCount { get; set; }

    // 创建者信息
    [property: JsonPropertyName("list_create_username")]
    public string CreatorName { get; set; } = "";

    [property: JsonPropertyName("list_create_userid")]
    public long CreatorId { get; set; }

    // 播放量 (heat)
    [property: JsonPropertyName("heat")] public int Heat { get; set; }

    // 创建时间 (时间戳)
    [property: JsonPropertyName("create_time")]
    public long CreateTime { get; set; }

    // 其他如 tags, is_publish, status 等会自动进入 Extras
}