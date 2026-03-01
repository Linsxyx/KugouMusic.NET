using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌手歌曲列表响应 (对应 Root)
/// </summary>
public record SingerAudioResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("data")] public List<SingerSongItem> Songs { get; set; } = new();
}

/// <summary>
///     单首歌曲信息
/// </summary>
public record SingerSongItem : KgBaseModel
{
    [property: JsonPropertyName("audio_name")]
    public string Name { get; set; } = "";

    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    [property: JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";

    [property: JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    [property: JsonPropertyName("timelength")]
    public long Duration { get; set; }

    [property: JsonPropertyName("trans_param")]
    public SingerTransParam? TransParam { get; set; }
}

public record SingerTransParam
{
    [property: JsonPropertyName("union_cover")]
    public string? UnionCover
    {
        get => field?.Replace("{size}", "400");
        set;
    }
}