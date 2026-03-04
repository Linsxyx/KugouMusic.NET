using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record RemoveSongResponse : KgBaseModel
{
    [property: JsonPropertyName("count")] public int Count { get; set; }

    [property: JsonPropertyName("listid")] public long ListId { get; set; }

    [property: JsonPropertyName("last_time")]
    public long LastTime { get; set; }
}