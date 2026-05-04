using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record SingerDetailResponse : KgBaseModel
{
    [property: JsonPropertyName("birthday")]
    public string Birthday { get; set; } = "";

    [property: JsonPropertyName("author_name")]
    public string Name { get; set; } = "";

    [property: JsonPropertyName("sizable_avatar")]
    public string Cover
    {
        get => field.Replace("{size}", "150");
        set;
    } = "";
}