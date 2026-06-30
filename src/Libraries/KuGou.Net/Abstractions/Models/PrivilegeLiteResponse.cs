using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// 歌曲详情 
/// </summary>
public record PrivilegeLiteResponse : KgBaseModel
{
    [property: JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [property: JsonPropertyName("appid_group")]
    public int AppIdGroup { get; set; }

    [property: JsonPropertyName("should_cache")]
    public int ShouldCache { get; set; }

    /// <summary>
    /// 请求查询的歌曲数据列表
    /// </summary>
    [property: JsonPropertyName("data")]
    public List<PrivilegeLiteData> Data { get; set; } = new();
}

/// <summary>
/// 单个音质或单首歌曲的具体权限信息
/// 注意：酷狗的数据结构中，relate_goods 包含了同级结构的其他音质数据
/// </summary>
public record PrivilegeLiteData
{
    [property: JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [property: JsonPropertyName("id")]
    public long Id { get; set; }

    [property: JsonPropertyName("album_id")]
    public string AlbumId { get; set; } = string.Empty;

    [property: JsonPropertyName("recommend_album_id")]
    public string RecommendAlbumId { get; set; } = string.Empty;

    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [property: JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [property: JsonPropertyName("singername")]
    public string SingerName { get; set; } = string.Empty;

    [property: JsonPropertyName("albumname")]
    public string AlbumName { get; set; } = string.Empty;

    [property: JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// 音质标识：128, 320, flac, high, viper_atmos 等
    /// </summary>
    [property: JsonPropertyName("quality")]
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// 权限标识。通常 8=免费，10=VIP/付费
    /// </summary>
    [property: JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    [property: JsonPropertyName("status")]
    public int Status { get; set; }

    [property: JsonPropertyName("pay_type")]
    public int PayType { get; set; }

    [property: JsonPropertyName("price")]
    public int Price { get; set; }

    [property: JsonPropertyName("pkg_price")]
    public int PkgPrice { get; set; }

    [property: JsonPropertyName("info")]
    public PrivilegeAudioInfo? Info { get; set; }

    [property: JsonPropertyName("trans_param")]
    public PrivilegeTransParam? TransParam { get; set; }

    [property: JsonPropertyName("_msg")]
    public string InternalMsg { get; set; } = string.Empty;

    [property: JsonPropertyName("_errno")]
    public int InternalErrNo { get; set; }

    /// <summary>
    /// 关联的其他音质商品（重点：这是一个递归结构，包含了 320k, flac, viper_atmos 等同结构数据）
    /// </summary>
    [property: JsonPropertyName("relate_goods")]
    public List<PrivilegeLiteData> RelateGoods { get; set; } = new();
}

/// <summary>
/// 音频物理信息
/// </summary>
public record PrivilegeAudioInfo
{
    [property: JsonPropertyName("duration")]
    public int Duration { get; set; }

    [property: JsonPropertyName("filesize")]
    public long FileSize { get; set; }

    [property: JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [property: JsonPropertyName("extname")]
    public string ExtName { get; set; } = string.Empty;

    [property: JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    [property: JsonPropertyName("imgsize")]
    public List<int> ImgSize { get; set; } = new();
}

/// <summary>
/// 扩展转换参数 (包含各种 map 和备用 Hash)
/// </summary>
public record PrivilegeTransParam
{
    [property: JsonPropertyName("cid")]
    public long Cid { get; set; }

    [property: JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [property: JsonPropertyName("is_original")]
    public int IsOriginal { get; set; }

    [property: JsonPropertyName("hash_multitrack")]
    public string HashMultitrack { get; set; } = string.Empty;

    [property: JsonPropertyName("union_cover")]
    public string? UnionCover {
        get => field?.Replace("{size}", "400");
        set;
    }

    [property: JsonPropertyName("ogg_128_hash")]
    public string Ogg128Hash { get; set; } = string.Empty;

    [property: JsonPropertyName("ogg_128_filesize")]
    public long Ogg128FileSize { get; set; }

    [property: JsonPropertyName("ogg_320_hash")]
    public string Ogg320Hash { get; set; } = string.Empty;

    [property: JsonPropertyName("ogg_320_filesize")]
    public long Ogg320FileSize { get; set; }

    [property: JsonPropertyName("classmap")]
    public Dictionary<string, long>? ClassMap { get; set; }

    [property: JsonPropertyName("qualitymap")]
    public PrivilegeQualityMap? QualityMap { get; set; }

    [property: JsonPropertyName("ipmap")]
    public Dictionary<string, long>? IpMap { get; set; }
}

public record PrivilegeQualityMap
{
    [property: JsonPropertyName("attr0")]
    public long Attr0 { get; set; }

    [property: JsonPropertyName("attr1")]
    public long Attr1 { get; set; }

    [property: JsonPropertyName("bits")]
    public string Bits { get; set; } = string.Empty;
}