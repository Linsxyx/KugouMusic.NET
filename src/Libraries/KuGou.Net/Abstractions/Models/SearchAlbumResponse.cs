using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record SearchAlbumResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("lists")] public List<SearchAlbumItem> Albums { get; set; } = new();
}

public record SearchAlbumItem : KgBaseModel
{
    [property: JsonPropertyName("albumid")]
    public long AlbumId { get; set; }

    [property: JsonPropertyName("albumname")]
    public string Name { get; set; } = "";

    [property: JsonPropertyName("singer")] public string SingerName { get; set; } = "";

    [property: JsonPropertyName("songcount")]
    public int SongCount { get; set; }

    [property: JsonPropertyName("publish_time")]
    public string PublishTime { get; set; } = "";

    [property: JsonPropertyName("ostremark")]
    public string Remark { get; set; } = "";

    [property: JsonPropertyName("img")] public string? Cover { get; set; }
}