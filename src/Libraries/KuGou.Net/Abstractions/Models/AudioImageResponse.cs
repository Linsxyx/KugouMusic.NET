using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     歌曲作者写真，对应 /images/audio。
/// </summary>
public record AudioImageResponse : KgBaseModel
{
    [property: JsonPropertyName("errcode")] public int ErrCode { get; set; }

    [property: JsonPropertyName("errmsg")] public string ErrorMessage { get; set; } = string.Empty;

    [property: JsonPropertyName("data")] public List<List<AudioImageAuthor>> Data { get; set; } = new();

    [JsonIgnore] public IEnumerable<AudioImageAuthor> Authors => Data.SelectMany(group => group);
}

public record AudioImageAuthor
{
    [property: JsonPropertyName("author_id")] public long AuthorId { get; set; }

    [property: JsonPropertyName("author_name")] public string AuthorName { get; set; } = string.Empty;

    [property: JsonPropertyName("is_publish")] public int IsPublish { get; set; }

    [property: JsonPropertyName("res_hash")] public string ResourceHash { get; set; } = string.Empty;

    [property: JsonPropertyName("avatar")] public string Avatar { get; set; } = string.Empty;

    [property: JsonPropertyName("sizable_avatar")] public string SizableAvatar { get; set; } = string.Empty;

    [property: JsonPropertyName("audio_publish_date")] public string AudioPublishDate { get; set; } = string.Empty;

    [property: JsonPropertyName("imgs")] public Dictionary<string, List<AudioImageItem>> Images { get; set; } = new();
}

public record AudioImageItem
{
    [property: JsonPropertyName("id")] public long Id { get; set; }

    [property: JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;

    [property: JsonPropertyName("sizable_portrait")] public string SizablePortrait { get; set; } = string.Empty;

    [property: JsonPropertyName("filename")] public string FileName { get; set; } = string.Empty;

    [property: JsonPropertyName("publish_time")] public string PublishTime { get; set; } = string.Empty;

    [property: JsonPropertyName("source")] public int Source { get; set; }
}
