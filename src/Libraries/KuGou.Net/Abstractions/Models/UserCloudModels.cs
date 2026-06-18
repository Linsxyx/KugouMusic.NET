using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     对应用户云盘接口的 data 节点。
/// </summary>
public record UserCloudResponse : KgBaseModel
{
    /// <summary>
    ///     当前页云盘歌曲列表。
    /// </summary>
    [JsonPropertyName("list")] public List<UserCloudSong> Songs { get; set; } = new();

    /// <summary>
    ///     云盘歌曲总数。
    /// </summary>
    [JsonPropertyName("list_count")] public int ListCount { get; set; }

    /// <summary>
    ///     已使用空间，单位字节。
    /// </summary>
    [JsonPropertyName("used_size")] public long UsedSize { get; set; }

    /// <summary>
    ///     剩余可用空间，单位字节。
    /// </summary>
    [JsonPropertyName("availble_size")] public long AvailableSize { get; set; }

    /// <summary>
    ///     云盘总容量，单位字节。
    /// </summary>
    [JsonPropertyName("max_size")] public long MaxSize { get; set; }

    /// <summary>
    ///     用户类型。
    /// </summary>
    [JsonPropertyName("user_type")] public int UserType { get; set; }
}

/// <summary>
///     单首云盘歌曲信息。
/// </summary>
public record UserCloudSong : KgBaseModel
{
    /// <summary>
    ///     歌曲名称。
    /// </summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     标准音质 Hash。
    /// </summary>
    [JsonPropertyName("hash_std")] public string HashStd { get; set; } = "";

    /// <summary>
    ///     音频 ID。
    /// </summary>
    [JsonPropertyName("audio_id")] public long AudioId { get; set; }

    /// <summary>
    ///     专辑音频 ID。
    /// </summary>
    [JsonPropertyName("album_audio_id")] public long AlbumAudioId { get; set; }

    /// <summary>
    ///     KMR 专辑音频 ID。
    /// </summary>
    [JsonPropertyName("kmr_album_audio_id")] public long KmrAlbumAudioId { get; set; }

    /// <summary>
    ///     歌手名称，保留服务端聚合值。
    /// </summary>
    [JsonPropertyName("author_name")] public string AuthorName { get; set; } = "";

    /// <summary>
    ///     歌手列表。
    /// </summary>
    [JsonPropertyName("authors")] public List<UserCloudAuthor> Authors { get; set; } = new();

    /// <summary>
    ///     时长，单位为毫秒。
    /// </summary>
    [JsonPropertyName("timelen")] public int DurationMs { get; set; }

    /// <summary>
    ///     文件大小，单位字节。
    /// </summary>
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>
    ///     比特率等级。
    /// </summary>
    [JsonPropertyName("bitrate")] public int Bitrate { get; set; }

    /// <summary>
    ///     扩展名。
    /// </summary>
    [JsonPropertyName("ext")] public string Ext { get; set; } = "";

    /// <summary>
    ///     添加时间戳。
    /// </summary>
    [JsonPropertyName("add_time")] public long AddTime { get; set; }

    /// <summary>
    ///     库内编号。
    /// </summary>
    [JsonPropertyName("kv_id")] public int KvId { get; set; }

    /// <summary>
    ///     专辑信息。
    /// </summary>
    [JsonPropertyName("album_info")] public UserCloudAlbumInfo? Album { get; set; }

    /// <summary>
    ///     歌曲封面图，优先取 album_info.sizable_cover。
    /// </summary>
    [JsonIgnore]
    public string? Cover => Album?.Cover;
}

/// <summary>
///     云盘歌曲歌手信息。
/// </summary>
public record UserCloudAuthor : KgBaseModel
{
    /// <summary>
    ///     歌手 ID。
    /// </summary>
    [JsonPropertyName("author_id")] public long AuthorId { get; set; }

    /// <summary>
    ///     歌手名称。
    /// </summary>
    [JsonPropertyName("author_name")] public string AuthorName { get; set; } = "";

    /// <summary>
    ///     歌手头像。
    /// </summary>
    [JsonPropertyName("sizable_avatar")]
    public string? Avatar
    {
        get => field?.Replace("{size}", "400");
        set;
    }
}

/// <summary>
///     云盘歌曲专辑信息。
/// </summary>
public record UserCloudAlbumInfo : KgBaseModel
{
    /// <summary>
    ///     专辑 ID。
    /// </summary>
    [JsonPropertyName("album_id")] public long AlbumId { get; set; }

    /// <summary>
    ///     专辑名。
    /// </summary>
    [JsonPropertyName("album_name")] public string AlbumName { get; set; } = "";

    /// <summary>
    ///     发布时间。
    /// </summary>
    [JsonPropertyName("publish_date")] public string? PublishDate { get; set; }

    /// <summary>
    ///     分类。
    /// </summary>
    [JsonPropertyName("category")] public int Category { get; set; }

    /// <summary>
    ///     是否已发布。
    /// </summary>
    [JsonPropertyName("is_publish")] public int IsPublish { get; set; }

    /// <summary>
    ///     专辑封面图。
    /// </summary>
    [JsonPropertyName("sizable_cover")]
    public string? Cover
    {
        get => field?.Replace("{size}", "400");
        set;
    }
}

/// <summary>
///     对应用户云盘歌曲直链接口的 data 节点。
/// </summary>
public record UserCloudUrlResponse : KgBaseModel
{
    /// <summary>
    ///     文件大小，单位字节。
    /// </summary>
    [JsonPropertyName("fileSize")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long FileSize { get; set; }

    /// <summary>
    ///     主下载地址。
    /// </summary>
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>
    ///     备用下载地址。
    /// </summary>
    [JsonPropertyName("backup_url")] public string BackupUrl { get; set; } = "";

    /// <summary>
    ///     文件扩展名。
    /// </summary>
    [JsonPropertyName("extName")] public string ExtName { get; set; } = "";

    /// <summary>
    ///     歌曲 Hash。
    /// </summary>
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";

    /// <summary>
    ///     是否成功返回可用下载地址。
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Status == 1 && !string.IsNullOrWhiteSpace(Url);
}
