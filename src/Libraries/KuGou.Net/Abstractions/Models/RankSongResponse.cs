using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     榜单歌曲分页结果。
/// </summary>
public record RankSongResponse : KgBaseModel
{
    /// <summary>
    ///     总歌曲数。
    /// </summary>
    [property: JsonPropertyName("total")] public int Total { get; set; }

    /// <summary>
    ///     当前页榜单歌曲列表。
    /// </summary>
    [property: JsonPropertyName("songlist")]
    public List<RankSongItem> RankSongLists { get; set; } = new();
}

/// <summary>
///     榜单中的单首歌曲信息。
/// </summary>
public record RankSongItem : KgBaseModel
{
    /// <summary>
    ///     音频基础信息。
    /// </summary>
    [property: JsonPropertyName("deprecated")]
    public RankSongAudioInfo AudioInfo { get; set; } = new();

    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    /// <summary>
    ///     作者列表。
    /// </summary>
    [property: JsonPropertyName("authors")]
    public List<RankSongAuthor> Authors { get; set; } = new();

    /// <summary>
    ///     混合 ID (album_audio_id)，用于相似推荐。榜单接口将其放在顶层。
    /// </summary>
    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    /// <summary>
    ///     歌曲名称。
    /// </summary>
    [property: JsonPropertyName("songname")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     扩展参数。
    /// </summary>
    [property: JsonPropertyName("trans_param")]
    public TransParam? TransParam { get; set; }

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [JsonIgnore] public string Hash => AudioInfo.Hash;

    /// <summary>
    ///     时长，单位为毫秒。
    /// </summary>
    [JsonIgnore] public int DurationMs => AudioInfo.Duration;

    /// <summary>
    ///     专辑信息。
    /// </summary>
    [property: JsonPropertyName("album_info")]
    public RankAlbum? Album { get; set; }

    /// <summary>
    ///     转换为兼容 UI 的歌手列表。
    /// </summary>
    [JsonIgnore]
    public List<SingerLite> Singers => Authors.Select(a => new SingerLite
    {
        Id = a.AuthorId,
        Name = a.AuthorName
    }).ToList();
}

/// <summary>
///     榜单歌曲的音频基础信息。
/// </summary>
public record RankSongAudioInfo
{
    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     时长，单位为毫秒。
    /// </summary>
    [property: JsonPropertyName("duration")]
    public int Duration { get; set; }
}

/// <summary>
///     榜单歌曲作者信息。
/// </summary>
public record RankSongAuthor
{
    /// <summary>
    ///     作者 ID。
    /// </summary>
    [property: JsonPropertyName("author_id")]
    public long AuthorId { get; set; }

    /// <summary>
    ///     作者名称。
    /// </summary>
    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = "";
}

/// <summary>
///     榜单歌曲所属专辑信息。
/// </summary>
public record RankAlbum : KgBaseModel
{
    /// <summary>
    ///     专辑封面地址。
    /// </summary>
    [property: JsonPropertyName("sizable_cover")]
    public string Cover { get; set; } = "";

    /// <summary>
    ///     专辑名称。
    /// </summary>
    [property: JsonPropertyName("album_name")]
    public string Name { get; set; } = "";
}
