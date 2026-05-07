using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record RankListResponse : KgBaseModel
{
    [property: JsonPropertyName("info")] public List<RankListItem> Info { get; set; } = new();
}

public record RankListItem
{
    [property: JsonPropertyName("img_9")]
    public string? Cover
    {
        get => field?.Replace("{size}", "250");
        set;
    }

    [property: JsonPropertyName("rankid")] public long FileId { get; set; }

    [property: JsonPropertyName("rankname")]
    public string Name { get; set; } = "";
}