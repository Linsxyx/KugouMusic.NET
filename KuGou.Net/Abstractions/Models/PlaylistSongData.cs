using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应 data 节点的结构
/// </summary>
public record PlaylistSongResponse : KgBaseModel
{
    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("songs")] public List<PlaylistSong> Songs { get; set; } = new();
}

/// <summary>
///     单首歌曲信息 (对应 songs 数组中的项)
/// </summary>
public record PlaylistSong : KgBaseModel
{
    // 歌曲名称 (通常是 "歌手 - 歌名" 格式)
    [property: JsonPropertyName("name")] public string Name { get; set; } = "";

    // 文件 Hash (最重要)
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    // 时长 (毫秒)
    [property: JsonPropertyName("timelen")]
    public int DurationMs { get; set; }

    // 专辑 ID
    [property: JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = "";

    // 权限/VIP 标记 (10通常是VIP, 0是免费，8可能是版权限制)
    [property: JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    // 歌手信息数组
    [property: JsonPropertyName("singerinfo")]
    public List<SingerLite> Singers { get; set; } = new();

    // 专辑信息对象
    [property: JsonPropertyName("albuminfo")]
    public AlbumLite? Album { get; set; }

    //封面，先写死300，酷狗官方存的封面本来就没太高清
    [property: JsonPropertyName("cover")]
    public string? Cover
    {
        get => field?.Replace("{size}", "300");
        set;
    }
}

public record SingerLite : KgBaseModel
{
    [property: JsonPropertyName("id")] public long Id { get; set; }

    [property: JsonPropertyName("name")] public string Name { get; set; } = "";

    [property: JsonPropertyName("avatar")]
    public string SingerPic
    {
        get => field.Replace("{size}", "100");
        set;
    } = "";
}

public record AlbumLite : KgBaseModel
{
    [property: JsonPropertyName("id")] public long Id { get; set; } // 这里 JSON 显示是数字，用 long

    [property: JsonPropertyName("name")] public string Name { get; set; } = "";
}