using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     添加歌曲到歌单响应
/// </summary>
public record AddSongResponse : KgBaseModel
{
    /// <summary>
    ///     当前歌单内的歌曲总数
    /// </summary>
    [property: JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    ///     目标歌单 ID
    /// </summary>
    [property: JsonPropertyName("listid")]
    public long ListId { get; set; }

    /// <summary>
    ///     成功添加的歌曲列表
    ///     <para>如果只需判断成功，检查这个列表是否为空即可</para>
    /// </summary>
    [property: JsonPropertyName("info")]
    public List<AddSongItem> AddedSongs { get; set; } = new();
}

/// <summary>
///     添加成功的单曲简略信息
/// </summary>
public record AddSongItem
{
    /// <summary>
    ///     歌曲 Hash
    /// </summary>
    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    /// <summary>
    ///     歌单内唯一 ID
    /// </summary>
    [property: JsonPropertyName("fileid")]
    public long FileId { get; set; }

    /// <summary>
    ///     歌曲名称
    /// </summary>
    [property: JsonPropertyName("name")]
    public string Name { get; set; } = "";
}