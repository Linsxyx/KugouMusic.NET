using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// 私人FM/猜你喜欢 响应数据 (对应 data 节点)
/// </summary>
public record PersonalFmResponse : KgBaseModel
{
    /// <summary>
    /// 推荐的歌曲列表
    /// </summary>
    [JsonPropertyName("song_list")]
    public List<PersonalFmSong> Songs { get; set; } = new();

    /// <summary>
    /// 当前推荐模式 (如: normal, small)
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";
}

/// <summary>
/// 私人FM 单曲信息
/// </summary>
public record PersonalFmSong : KgBaseModel
{
    [JsonPropertyName("songname")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("time_length")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = "";

    [JsonPropertyName("songid")]
    public long AudioId { get; set; }
    [JsonPropertyName("mixsongid")]
    public string MixSongId { get; set; } = "";
    [JsonPropertyName("privilege")]
    public int Privilege { get; set; }
    [JsonPropertyName("singerinfo")]
    public List<SingerLite> Singers { get; set; } = new();

    // 复用项目中已有的 TransParam 提取 union_cover (封面)
    [JsonPropertyName("trans_param")]
    public TransParam? TransParam { get; set; }
}