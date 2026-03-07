using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record RankSongResponse : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("songlist")]
    public List<RankSongItem> RankSongLists { get; set; } = new();
}

public record RankSongItem : KgBaseModel
{
    [property: JsonPropertyName("deprecated")]
    public RankSongAudioInfo AudioInfo { get; set; } = new();

    [property: JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    [property: JsonPropertyName("authors")]
    public List<RankSongAuthor> Authors { get; set; } = new();

    [property: JsonPropertyName("songname")]
    public string Name { get; set; } = string.Empty;

    [property: JsonPropertyName("trans_param")]
    public TransParam? TransParam { get; set; }

    [JsonIgnore] public string Hash => AudioInfo.Hash;


    [JsonIgnore] public int DurationMs => AudioInfo.Duration;


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

public record RankSongAudioInfo
{
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    [property: JsonPropertyName("duration")]
    public int Duration { get; set; }
}

public record RankSongAuthor
{
    [property: JsonPropertyName("author_id")]
    public long AuthorId { get; set; }

    [property: JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = "";
}