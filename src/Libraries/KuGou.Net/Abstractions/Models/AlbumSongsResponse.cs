using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     专辑歌曲响应外层 (对应 data 节点)
/// </summary>
public record AlbumSongResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("songs")] public List<AlbumSongItem> Songs { get; set; } = new();
}

/// <summary>
///     专辑中的单首歌曲
/// </summary>
public record AlbumSongItem : KgBaseModel
{
    [property: JsonPropertyName("base")] public AlbumSongBase BaseInfo { get; set; } = new();

    [property: JsonPropertyName("audio_info")]
    public AlbumSongAudioInfo AudioInfo { get; set; } = new();

    [property: JsonPropertyName("album_info")]
    public AlbumSongAlbumInfo AlbumInfo { get; set; } = new();

    [property: JsonPropertyName("authors")]
    public List<AlbumSongAuthor> Authors { get; set; } = new();

    [JsonIgnore] public string Name => BaseInfo.AudioName;

    [JsonIgnore] public string Singer => BaseInfo.AuthorName;

    [JsonIgnore] public string Hash => AudioInfo.Hash;

    [JsonIgnore] public string AlbumId => BaseInfo.AlbumId.ToString();

    [JsonIgnore] public int DurationMs => AudioInfo.Duration;

    [JsonIgnore] public string Cover => AlbumInfo.Cover.Replace("{size}", "400");

    /// <summary>
    ///     转换为兼容 UI 的 SingerLite 列表
    /// </summary>
    [JsonIgnore]
    public List<SingerLite> Singers => Authors.Select(a => new SingerLite
    {
        Id = a.AuthorId,
        Name = a.AuthorName
    }).ToList();
}

public record AlbumSongBase
{
    [property: JsonPropertyName("audio_name")]
    public string AudioName { get; set; } = "";

    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = "";

    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }
}

public record AlbumSongAudioInfo
{
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    [property: JsonPropertyName("duration")]
    public int Duration { get; set; }
}

public record AlbumSongAlbumInfo
{
    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";

    [property: JsonPropertyName("cover")] public string Cover { get; set; } = "";
}

public record AlbumSongAuthor
{
    [property: JsonPropertyName("author_id")]
    public long AuthorId { get; set; }

    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = "";
}