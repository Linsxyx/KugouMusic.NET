using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌手专辑列表响应。
/// </summary>
public record ArtistAlbumResponse : KgBaseModel
{
    [property: JsonPropertyName("total")]
    public int Total { get; set; }

    [property: JsonPropertyName("data")]
    public List<ArtistAlbumItem> Albums { get; set; } = new();

    [property: JsonPropertyName("extra")]
    public ArtistAlbumExtra? Extra { get; set; }
}

/// <summary>
///     歌手专辑分页扩展信息。
/// </summary>
public record ArtistAlbumExtra
{
    [property: JsonPropertyName("page_total")]
    public int PageTotal { get; set; }
}

/// <summary>
///     歌手专辑列表项。
/// </summary>
public record ArtistAlbumItem : KgBaseModel
{
    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = string.Empty;

    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [property: JsonPropertyName("cover")]
    public string Cover { get; set; } = string.Empty;

    [property: JsonPropertyName("sizable_cover")]
    public string? SizableCover { get; set; }

    [property: JsonPropertyName("authors")]
    public List<ArtistAlbumAuthor> Authors { get; set; } = new();

    [property: JsonPropertyName("publish_date")]
    public string PublishDate { get; set; } = string.Empty;

    [property: JsonPropertyName("intro")]
    public string Intro { get; set; } = string.Empty;

    [property: JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
///     歌手专辑作者信息。
/// </summary>
public record ArtistAlbumAuthor
{
    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [property: JsonPropertyName("author_id")]
    public long AuthorId { get; set; }
}
