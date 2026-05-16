using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌手 MV 
/// </summary>
public record ArtistVideoResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("extra")] public ArtistVideoExtra? Extra { get; set; }

    [property: JsonPropertyName("data")] public List<ArtistVideoItem> Videos { get; set; } = new();
}

public record ArtistVideoExtra
{
    [property: JsonPropertyName("page_total")]
    public int PageTotal { get; set; }
}

public record ArtistVideoItem
{
    [property: JsonPropertyName("video_id")]
    public long VideoId { get; set; }

    [property: JsonPropertyName("video_name")]
    public string VideoName { get; set; } = string.Empty;

    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [property: JsonPropertyName("cover")] public string Cover { get; set; } = string.Empty;

    /// <summary>
    ///     高清封面图，自动替换 {size} 为 400
    /// </summary>
    [property: JsonPropertyName("hdpic")]
    public string? HdPic
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    [property: JsonPropertyName("publish_date")]
    public string PublishDate { get; set; } = string.Empty;

    /// <summary>
    ///     视频时长（毫秒）
    /// </summary>
    [property: JsonPropertyName("timelength")]
    public long DurationMs { get; set; }

    [property: JsonPropertyName("audio_hash")]
    public string AudioHash { get; set; } = string.Empty;

    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    [property: JsonPropertyName("history_heat")]
    public long HistoryHeat { get; set; }

    [property: JsonPropertyName("heat")] public long Heat { get; set; }

    [property: JsonPropertyName("topic")] public string Topic { get; set; } = string.Empty;

    [property: JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;

    [property: JsonPropertyName("intro")] public string Intro { get; set; } = string.Empty;

    /// <summary>
    ///     视频类型 
    /// </summary>
    [property: JsonPropertyName("type")]
    public int Type { get; set; }

    [property: JsonPropertyName("is_short")]
    public int IsShort { get; set; }

    [property: JsonPropertyName("h264")] public VideoH264Info? H264 { get; set; }
}

/// <summary>
///     H264 各清晰度视频哈希与信息
/// </summary>
public record VideoH264Info
{
    // 流畅 (LD)
    [property: JsonPropertyName("ld_hash")]
    public string LdHash { get; set; } = string.Empty;

    [property: JsonPropertyName("ld_filesize")]
    public long LdFileSize { get; set; }

    [property: JsonPropertyName("ld_bitrate")]
    public int LdBitrate { get; set; }

    // 标清 (SD)
    [property: JsonPropertyName("sd_hash")]
    public string SdHash { get; set; } = string.Empty;

    [property: JsonPropertyName("sd_filesize")]
    public long SdFileSize { get; set; }

    [property: JsonPropertyName("sd_bitrate")]
    public int SdBitrate { get; set; }

    // 高清 (QHD)
    [property: JsonPropertyName("qhd_hash")]
    public string QhdHash { get; set; } = string.Empty;

    [property: JsonPropertyName("qhd_filesize")]
    public long QhdFileSize { get; set; }

    [property: JsonPropertyName("qhd_bitrate")]
    public int QhdBitrate { get; set; }

    // 超清 (HD)
    [property: JsonPropertyName("hd_hash")]
    public string HdHash { get; set; } = string.Empty;

    [property: JsonPropertyName("hd_filesize")]
    public long HdFileSize { get; set; }

    [property: JsonPropertyName("hd_bitrate")]
    public int HdBitrate { get; set; }

    // 蓝光 (FHD)
    [property: JsonPropertyName("fhd_hash")]
    public string FhdHash { get; set; } = string.Empty;

    [property: JsonPropertyName("fhd_filesize")]
    public long FhdFileSize { get; set; }

    [property: JsonPropertyName("fhd_bitrate")]
    public int FhdBitrate { get; set; }
}