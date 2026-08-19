using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌手详情。
/// </summary>
public record SingerDetailResponse : KgBaseModel
{
    /// <summary>
    ///     生日。
    /// </summary>
    [property: JsonPropertyName("birthday")]
    public string Birthday { get; set; } = "";

    /// <summary>
    ///     歌手名称。
    /// </summary>
    [property: JsonPropertyName("author_name")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     歌手头像地址。
    /// </summary>
    [property: JsonPropertyName("sizable_avatar")]
    public string Cover
    {
        get;
        set => field = value.Replace("{size}", "400");
    } = "";
}
