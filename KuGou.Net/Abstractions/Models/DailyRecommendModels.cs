using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     每日推荐响应数据 (对应 data 节点)
/// </summary>
public record DailyRecommendResponse : KgBaseModel
{
    /// <summary>
    ///     推荐日期 (例如: "20260131")
    /// </summary>
    [property: JsonPropertyName("creation_date")]
    public string Date { get; set; } = "";

    /// <summary>
    ///     封面图 (例如: http://imge.kugou.com/commendpic/...)
    /// </summary>
    [property: JsonPropertyName("cover_img_url")]
    public string CoverUrl { get; set; } = "";

    /// <summary>
    ///     副标题
    /// </summary>
    [property: JsonPropertyName("sub_title")]
    public string SubTitle { get; set; } = "";

    /// <summary>
    ///     歌曲列表
    /// </summary>
    [property: JsonPropertyName("song_list")]
    public List<DailyRecommendSong> Songs { get; set; } = new();
}

/// <summary>
///     每日推荐单曲信息
/// </summary>
public record DailyRecommendSong : KgBaseModel
{
    // ================= 基础信息 =================

    /// <summary>
    ///     歌曲名称
    /// </summary>
    [property: JsonPropertyName("songname")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     歌手名称 (组合字符串)
    /// </summary>
    [property: JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    /// <summary>
    ///     文件 Hash (标准音质/128k)
    /// </summary>
    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    /// <summary>
    ///     时长 (秒)
    /// </summary>
    [property: JsonPropertyName("time_length")]
    public int Duration { get; set; }

    /// <summary>
    ///     专辑 ID
    /// </summary>
    [property: JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = "";

    /// <summary>
    ///     专辑名称
    /// </summary>
    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";

    /// <summary>
    ///     歌曲 ID (AudioID)
    /// </summary>
    [property: JsonPropertyName("songid")]
    public long AudioId { get; set; }

    /// <summary>
    ///     混合 ID (用于歌词搜索等)
    /// </summary>
    [property: JsonPropertyName("mixsongid")]
    public string MixSongId { get; set; } = "";

    // ================= 封面与推荐语 =================

    /// <summary>
    ///     带尺寸占位符的封面 (例如: .../{size}/...)
    /// </summary>
    [property: JsonPropertyName("sizable_cover")]
    public string SizableCover { get; set; } = "";

    /// <summary>
    ///     推荐理由/文案 (例如: "人气歌曲推荐")
    /// </summary>
    [property: JsonPropertyName("rec_copy_write")]
    public string RecommendReason { get; set; } = "";

    /// <summary>
    ///     子推荐语 (例如: "昨日收听破万")
    /// </summary>
    [property: JsonPropertyName("rec_sub_copy_write")]
    public string RecommendSubReason { get; set; } = "";

    // ================= 音质 Hash 与大小 =================

    // 320K 高品质
    [property: JsonPropertyName("hash_320")]
    public string Hash320 { get; set; } = "";

    [property: JsonPropertyName("filesize_320")]
    public long FileSize320 { get; set; }

    // Flac 无损
    [property: JsonPropertyName("hash_flac")]
    public string HashFlac { get; set; } = "";

    [property: JsonPropertyName("filesize_flac")]
    public long FileSizeFlac { get; set; }

    // Hi-Res (如果有的话，通常字段是 hash_high)
    [property: JsonPropertyName("hash_192")]
    public string HashHiRes { get; set; } = "";

    // ================= 权限与标签 =================

    /// <summary>
    ///     权限位 (0=免费, 8=免费/会员混杂, 10=VIP, 1=付费)
    /// </summary>
    [property: JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    /// <summary>
    ///     是否原唱 (1=是, 0=否)
    /// </summary>
    [property: JsonPropertyName("is_original")]
    public int IsOriginal { get; set; }

    /// <summary>
    ///     歌手列表 (复用已有的 SingerLite)
    /// </summary>
    [property: JsonPropertyName("singerinfo")]
    public List<SingerLite> Singers { get; set; } = new();

    /// <summary>
    ///     获取可用封面 URL
    /// </summary>
    public string GetCoverUrl(int size = 400)
    {
        if (string.IsNullOrEmpty(SizableCover)) return "";
        return SizableCover.Replace("{size}", size.ToString());
    }
}