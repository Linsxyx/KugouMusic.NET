using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     AI 相似歌曲推荐返回的单曲信息。
///     字段命名参照 <see cref="DailyRecommendSong" />，JSON key 在实跑校准后可能调整。
/// </summary>
public record AiRecommendSong : KgBaseModel
{
    /// <summary>歌曲名称</summary>
    [property: JsonPropertyName("songname")]
    public string Name { get; set; } = "";

    /// <summary>歌手名称</summary>
    [property: JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    /// <summary>文件 Hash (标准音质/128k)</summary>
    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    /// <summary>时长 (秒)</summary>
    [property: JsonPropertyName("time_length")]
    public int Duration { get; set; }

    /// <summary>专辑 ID</summary>
    [property: JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = "";

    /// <summary>专辑名称</summary>
    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";

    /// <summary>歌曲 ID (AudioID)</summary>
    [property: JsonPropertyName("songid")]
    public long AudioId { get; set; }

    /// <summary>混合 ID / album_audio_id，用于再次发起相似推荐</summary>
    [property: JsonPropertyName("mixsongid")]
    public long AlbumAudioId { get; set; }

    /// <summary>封面 (含 {size} 占位符)</summary>
    [property: JsonPropertyName("sizable_cover")]
    public string? SizableCover
    {
        get;
        set => field = value?.Replace("{size}", "400");
    }
}
