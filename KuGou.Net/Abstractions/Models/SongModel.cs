using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

// 1. 搜索结果外层
public record SearchResultData : KgBaseModel
{
    [property: JsonPropertyName("total")] public int Total { get; set; }

    [property: JsonPropertyName("lists")] public List<SongInfo>? Songs { get; set; }
}

// 2. 歌曲详情 (只定义核心字段)
public record SongInfo : KgBaseModel
{
    // 映射 FileHash 或 Hash
    [property: JsonPropertyName("FileHash")]
    public string Hash { get; set; } = "";

    // 映射 FileName 或 SongName
    [property: JsonPropertyName("FileName")]
    public string Name { get; set; } = "";

    [property: JsonPropertyName("SingerName")]
    public string Singer { get; set; } = "";

    [property: JsonPropertyName("Singers")]
    public List<SingerLite> Singers { get; set; } = new();

    [property: JsonPropertyName("AlbumID")]
    public string AlbumId { get; set; } = "";

    [property: JsonPropertyName("Duration")]
    public int Duration { get; set; }

    [property: JsonPropertyName("Image")]
    public string? Cover
    {
        get => field?.Replace("{size}", "400");
        set;
    }
}

// 3. 播放链接结果
public record PlayUrlData : KgBaseModel
{
    [property: JsonPropertyName("url")] public List<string>? Urls { get; set; }

    [property: JsonPropertyName("hash")] public string Hash { get; set; } = "";


    [property: JsonPropertyName("priv_status")]
    public int PrivStatus { get; set; }

    [property: JsonPropertyName("err_code")]
    public int ErrCode { get; set; }

    [JsonIgnore] public bool IsSuccess => Status == 1 && Urls != null && Urls.Count > 0;

    [JsonIgnore] public bool RequiresVip => PrivStatus == 1;

    [JsonIgnore] public bool RequiresAlbumPurchase => PrivStatus == 10;
}