using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌手歌曲列表响应 
/// </summary>
public record SingerAudioResponse : KgBaseModel
{
    /// <summary>
    ///     总记录数。
    /// </summary>
    [property: JsonPropertyName("total")] public int Total { get; set; }

    /// <summary>
    ///     当前页歌曲列表。
    /// </summary>
    [property: JsonPropertyName("data")] public List<SingerSongItem> Songs { get; set; } = new();
}

/// <summary>
///     单首歌曲信息
/// </summary>
public record SingerSongItem : KgBaseModel
{
    /// <summary>
    ///     歌曲名称。
    /// </summary>
    [property: JsonPropertyName("audio_name")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }
    
    /// <summary>
    ///     专辑歌曲 ID(等同于mixsongid) 。
    /// </summary>
    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    /// <summary>
    ///     专辑名称。
    /// </summary>
    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";

    /// <summary>
    ///     歌手名称。
    /// </summary>
    [property: JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    /// <summary>
    ///     时长，单位为毫秒。
    /// </summary>
    [property: JsonPropertyName("timelength")]
    public long Duration { get; set; }

    /// <summary>
    ///     扩展参数，包含封面等字段。
    /// </summary>
    [property: JsonPropertyName("trans_param")]
    public TransParam? TransParam { get; set; }
}

/// <summary>
///     扩展参数。
/// </summary>
public record TransParam
{
    /// <summary>
    ///     联合封面图地址。
    /// </summary>
    [property: JsonPropertyName("union_cover")]
    public string? UnionCover
    {
        get => field?.Replace("{size}", "400");
        set;
    }
}
