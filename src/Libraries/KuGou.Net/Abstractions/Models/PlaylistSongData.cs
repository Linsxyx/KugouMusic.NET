using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应 data 节点的结构
/// </summary>
public record PlaylistSongResponse : KgBaseModel
{
    /// <summary>
    ///     歌曲总数。
    /// </summary>
    [JsonPropertyName("count")] public int Count { get; set; }

    /// <summary>
    ///     当前页歌曲列表。
    /// </summary>
    [JsonPropertyName("songs")] public List<PlaylistSong> Songs { get; set; } = new();
}

/// <summary>
///     单首歌曲信息 
/// </summary>
public record PlaylistSong : KgBaseModel
{
    /// <summary>
    ///     歌曲名称。
    /// </summary>
    [property: JsonPropertyName("name")]
    public string Name
    {
        get => ProcessName(field);
        set;
    }

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     时长，单位为毫秒。
    /// </summary>
    [property: JsonPropertyName("timelen")]
    public int DurationMs { get; set; }

    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [property: JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = "";

    /// <summary>
    ///     权限标记。
    /// </summary>
    [property: JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    /// <summary>
    ///     歌单内文件 ID。
    /// </summary>
    [property: JsonPropertyName("fileid")] public int FileId { get; set; }

    /// <summary>
    ///     歌手信息列表。
    /// </summary>
    [property: JsonPropertyName("singerinfo")]
    public List<SingerLite> Singers { get; set; } = new();

    /// <summary>
    ///     专辑信息。
    /// </summary>
    [property: JsonPropertyName("albuminfo")]
    public AlbumLite? Album { get; set; }

    /// <summary>
    ///     封面图地址。
    /// </summary>
    [property: JsonPropertyName("cover")]
    public string? Cover
    {
        get => field?.Replace("{size}", "400");
        set;
    }
    
    /// <summary>
    ///     混合歌曲 ID。
    /// </summary>
    [JsonPropertyName("mixsongid")]
    public int MixSongId { get; set; } 

    private string ProcessName(string? rawName)
    {
        if (string.IsNullOrEmpty(rawName) || !Singers.Any())
            return rawName ?? "未知";

        var dashIndex = rawName.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex <= 0) return rawName;

        var prefix = rawName[..dashIndex].Trim();
        var songName = rawName[(dashIndex + 3)..].Trim();

        var singerNames = Singers.Select(s => s.Name).ToList();

        var containsAllSingers = singerNames.All(singer =>
            prefix.Contains(singer, StringComparison.OrdinalIgnoreCase));

        return containsAllSingers ? songName : rawName;
    }
}

/// <summary>
///     简化歌手信息。
/// </summary>
public record SingerLite : KgBaseModel
{
    /// <summary>
    ///     歌手 ID。
    /// </summary>
    [property: JsonPropertyName("id")] public long Id { get; set; }

    /// <summary>
    ///     歌手名称。
    /// </summary>
    [property: JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>
    ///     歌手头像地址。
    /// </summary>
    [property: JsonPropertyName("avatar")]
    public string SingerPic
    {
        get => field.Replace("{size}", "400");
        set;
    } = "";
}

/// <summary>
///     简化专辑信息。
/// </summary>
public record AlbumLite : KgBaseModel
{
    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [property: JsonPropertyName("id")] public long Id { get; set; }

    /// <summary>
    ///     专辑名称。
    /// </summary>
    [property: JsonPropertyName("name")] public string Name { get; set; } = "";
}
