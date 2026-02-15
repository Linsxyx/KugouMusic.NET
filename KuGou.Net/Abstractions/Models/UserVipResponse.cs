using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     用户 VIP 详情响应
/// </summary>
public record UserVipResponse : KgBaseModel
{
    [JsonPropertyName("userid")] public long UserId { get; set; }

    [JsonPropertyName("is_vip")] public int IsVip { get; set; }

    [JsonPropertyName("vip_type")] public int VipType { get; set; }

    [JsonPropertyName("busi_vip")] public List<BusiVipInfo> BusiVipList { get; set; } = new();


    /// <summary>
    ///     是否为概念版 VIP (SVIP) - 对应 product_type: "svip"
    /// </summary>
    public bool IsSuperVip => BusiVipList.Any(x =>
        x.ProductType == "svip" && x.IsVip == 1);

    /// <summary>
    ///     是否为畅听 VIP (TVIP) - 对应 product_type: "tvip"
    /// </summary>
    public bool IsConceptVip => BusiVipList.Any(x =>
        x.ProductType == "tvip" && x.IsVip == 1);
}

public record BusiVipInfo
{
    /// <summary>
    ///     是否是 VIP (1: 是, 0: 否)
    /// </summary>
    [JsonPropertyName("is_vip")]
    public int IsVip { get; set; }

    /// <summary>
    ///     VIP 类型标识
    ///     <para>svip = 概念版VIP</para>
    ///     <para>tvip = 畅听VIP</para>
    /// </summary>
    [JsonPropertyName("product_type")]
    public string ProductType { get; set; } = "";

    /// <summary>
    ///     业务类型 (如 "concept")
    /// </summary>
    [JsonPropertyName("busi_type")]
    public string BusiType { get; set; } = "";

    /// <summary>
    ///     开始时间
    /// </summary>
    [JsonPropertyName("vip_begin_time")]
    public string BeginTime { get; set; } = "";

    /// <summary>
    ///     结束时间
    /// </summary>
    [JsonPropertyName("vip_end_time")]
    public string EndTime { get; set; } = "";

    /// <summary>
    ///     清除时间
    /// </summary>
    [JsonPropertyName("vip_clearday")]
    public string ClearDay { get; set; } = "";
}