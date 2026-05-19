using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// 电台图片列表响应数据
/// </summary>
public record FmImageResponse : KgBaseModel
{
    [JsonPropertyName("data")]
    public List<FmImageData> Data { get; set; } = new();
}

public record FmImageData
{
    [JsonPropertyName("fmid")]
    public long FmId { get; set; }
    
    /*[JsonPropertyName("fmId")]
    public string FmIdString { get; set; } = "";*/

    [JsonPropertyName("fmtype")]
    public int FmType { get; set; }

    [JsonPropertyName("imgUrl50")]
    public string ImgUrl50 { get; set; } = "";

    /// <summary>
    /// 封面 
    /// </summary>
    [JsonPropertyName("imgUrl100")]
    public string? ImgUrl100
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    [JsonPropertyName("imgUrl100_size")]
    public string ImgUrl100Size { get; set; } = "";
}