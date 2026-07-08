using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     榜单列表结果。
/// </summary>
public record RankListResponse : KgBaseModel
{
    /// <summary>
    ///     榜单列表。
    /// </summary>
    [property: JsonPropertyName("info")] public List<RankListItem> Info { get; set; } = new();
}

/// <summary>
///     单个榜单信息。
/// </summary>
public record RankListItem
{
    /// <summary>
    ///     榜单封面图地址。
    /// </summary>
    [property: JsonPropertyName("img_9")]
    public string? Cover
    {
        get => field?.Replace("{size}", "250");
        set;
    }

    /// <summary>
    ///     榜单 ID。
    /// </summary>
    [property: JsonPropertyName("rankid")] public long FileId { get; set; }

    /// <summary>
    ///     榜单名称。
    /// </summary>
    [property: JsonPropertyName("rankname")]
    public string Name { get; set; } = "";
    
    /// <summary>
    ///     榜单分类 1：星耀榜，2：地区榜，3：特色榜，4：全球榜，5：曲风榜。
    /// </summary>
    [property: JsonPropertyName("classify")]
    public long Classify { get; set; } 
    
    [property: JsonPropertyName("songinfo")]
    public List<RankListSongPreview> SongPreviews { get; set; } = [];
}


/// <summary>
/// 榜单列表中附带的歌曲预览信息
/// </summary>
public record RankListSongPreview
{
    /// <summary>
    /// 歌曲名
    /// </summary>
    [property: JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 歌手名
    /// </summary>
    [property: JsonPropertyName("author")]
    public string Author { get; set; } = "";

    /// <summary>
    /// 完整组合名称（通常是 "歌手 - 歌名"）
    /// </summary>
    [property: JsonPropertyName("songname")]
    public string SongName { get; set; } = "";

    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }
    
    [property: JsonPropertyName("trans_param")]
    public PrivilegeTransParam? TransParam { get; set; }
}