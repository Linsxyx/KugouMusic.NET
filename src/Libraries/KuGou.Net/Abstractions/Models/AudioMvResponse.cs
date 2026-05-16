using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌曲相关 MV 响应
/// </summary>
public record AudioMvResponse : KgBaseModel
{
    /// <summary>
    ///     数据体是一个二维数组
    /// </summary>
    [property: JsonPropertyName("data")]
    public List<List<AudioMvItem>> Data { get; set; } = new();

    /// <summary>
    ///     辅助方法：方便直接获取所有展平的 MV 列表
    /// </summary>
    [JsonIgnore]
    public IEnumerable<AudioMvItem> Mvs => Data.SelectMany(group => group);
}

/// <summary>
///     单条 MV 详情
/// </summary>
public record AudioMvItem
{
    [property: JsonPropertyName("is_recommend")]
    public int IsRecommend { get; set; }

    [property: JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [property: JsonPropertyName("hot")] 
    public int Hot { get; set; }

    [property: JsonPropertyName("hit")] 
    public long Hit { get; set; }

    [property: JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    [property: JsonPropertyName("songid")] 
    public long SongId { get; set; }

    /// <summary>
    ///     MV 缩略图
    /// </summary>
    [property: JsonPropertyName("thumb")]
    public string? Thumb
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    [property: JsonPropertyName("desc")] 
    public string Desc { get; set; } = string.Empty;

    /// <summary>
    ///     内部状态字段
    /// </summary>
    [property: JsonPropertyName("__status")]
    public int InternalStatus { get; set; }

    [property: JsonPropertyName("singer")] 
    public string Singer { get; set; } = string.Empty;

    [property: JsonPropertyName("other_desc")]
    public string OtherDesc { get; set; } = string.Empty;

    /// <summary>
    ///     MV 高清封面图
    /// </summary>
    [property: JsonPropertyName("hdpic")]
    public string? HdPic
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    [property: JsonPropertyName("is_ugc")] 
    public int IsUgc { get; set; }

    [property: JsonPropertyName("have_mp4")]
    public int HaveMp4 { get; set; }

    [property: JsonPropertyName("audio_id")]
    public long AudioId { get; set; }

    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    [property: JsonPropertyName("is_publish")]
    public int IsPublish { get; set; }

    [property: JsonPropertyName("download_total")]
    public long DownloadTotal { get; set; }

    [property: JsonPropertyName("collection_total")]
    public long CollectionTotal { get; set; }

    [property: JsonPropertyName("is_short")]
    public int IsShort { get; set; }

    [property: JsonPropertyName("remark")] 
    public string Remark { get; set; } = string.Empty;

    [property: JsonPropertyName("video_id")]
    public long VideoId { get; set; }

    [property: JsonPropertyName("user_avatar")]
    public string UserAvatar { get; set; } = string.Empty;

    [property: JsonPropertyName("music_trac")]
    public int MusicTrac { get; set; }

    [property: JsonPropertyName("mv_name")]
    public string MvName { get; set; } = string.Empty;

    /// <summary>
    ///     时长（毫秒）
    /// </summary>
    [property: JsonPropertyName("duration")]
    public long DurationMs { get; set; }

    [property: JsonPropertyName("topic")] 
    public string Topic { get; set; } = string.Empty;

    [property: JsonPropertyName("type")] 
    public int Type { get; set; }

    [property: JsonPropertyName("is_other")]
    public int IsOther { get; set; }

    [property: JsonPropertyName("publish_time")]
    public string PublishTime { get; set; } = string.Empty;

    [property: JsonPropertyName("play_times")]
    public long PlayTimes { get; set; }
}