using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     搜索歌曲结果。
/// </summary>
public record SearchResultData : KgBaseModel
{
    /// <summary>
    ///     匹配到的总记录数。
    /// </summary>
    [property: JsonPropertyName("total")] public int Total { get; set; }

    /// <summary>
    ///     当前页歌曲列表。
    /// </summary>
    [property: JsonPropertyName("lists")] public List<SongInfo>? Songs { get; set; }
}

/// <summary>
///     搜索结果中的单首歌曲信息。
/// </summary>
public record SongInfo : KgBaseModel
{
    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [property: JsonPropertyName("FileHash")]
    public string Hash { get; set; } = "";

    /// <summary>
    ///     歌曲名称。
    /// </summary>
    [property: JsonPropertyName("FileName")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     歌手名称字符串。
    /// </summary>
    [property: JsonPropertyName("SingerName")]
    public string Singer { get; set; } = "";

    /// <summary>
    ///     歌手列表。
    /// </summary>
    [property: JsonPropertyName("Singers")]
    public List<SingerLite> Singers { get; set; } = new();

    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [property: JsonPropertyName("AlbumID")]
    public string AlbumId { get; set; } = "";

    /// <summary>
    ///     专辑名称。
    /// </summary>
    [property: JsonPropertyName("AlbumName")]
    public string AlbumName { get; set; } = "";

    /// <summary>
    ///     时长，单位为秒。
    /// </summary>
    [property: JsonPropertyName("Duration")]
    public int Duration { get; set; }

    /// <summary>
    ///     封面图地址。
    /// </summary>
    [property: JsonPropertyName("Image")]
    public string? Cover
    {
       get;
    set => field = value?.Replace("{size}", "400");
    }
}

/// <summary>
///     歌曲播放地址结果。
/// </summary>
public record PlayUrlData : KgBaseModel
{
    /// <summary>
    ///     可用播放地址列表。
    /// </summary>
    [property: JsonPropertyName("url")] public List<string>? Urls { get; set; }

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     权限状态。
    /// </summary>
    [property: JsonPropertyName("priv_status")]
    public int PrivStatus { get; set; }

    /// <summary>
    ///     错误码。
    /// </summary>
    [property: JsonPropertyName("err_code")]
    public int ErrCode { get; set; }

    /// <summary>
    ///     是否成功返回了可用播放地址。
    /// </summary>
    [JsonIgnore] public bool IsSuccess => Status == 1 && Urls != null && Urls.Count > 0;

    /// <summary>
    ///     是否需要 VIP 权限。
    /// </summary>
    [JsonIgnore] public bool RequiresVip => PrivStatus == 1;

    /// <summary>
    ///     是否需要购买专辑。
    /// </summary>
    [JsonIgnore] public bool RequiresAlbumPurchase => PrivStatus == 10;
}
