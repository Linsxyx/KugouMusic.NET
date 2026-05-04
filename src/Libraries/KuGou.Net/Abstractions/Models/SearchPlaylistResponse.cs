using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record SearchPlaylistResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("lists")] public List<SearchPlaylistItem> Playlists { get; set; } = new();
}

public record SearchPlaylistItem : KgBaseModel
{
    [property: JsonPropertyName("specialname")]
    public string Name { get; set; } = "";

    [property: JsonPropertyName("specialid")]
    public long ListId { get; set; }

    [property: JsonPropertyName("gid")] public string GlobalId { get; set; } = "";

    [property: JsonPropertyName("song_count")]
    public int SongCount { get; set; }

    [property: JsonPropertyName("nickname")]
    public string CreatorName { get; set; } = "";

    [property: JsonPropertyName("total_play_count")]
    public long PlayCount { get; set; }

    [property: JsonPropertyName("img")] public string? Cover { get; set; }

    [property: JsonPropertyName("publish_time")]
    public string PublishTime { get; set; } = "";
}