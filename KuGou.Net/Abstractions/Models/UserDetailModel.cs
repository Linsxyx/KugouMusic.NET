using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record UserDetailModel : KgBaseModel
{
    [property: JsonPropertyName("nickname")]
    public string Name { get; set; }

    [property: JsonPropertyName("pic")] public string Pic { get; set; }
}

public record OneDayVipModel : KgBaseModel
{
    [property: JsonPropertyName("ad_vip_num")]
    public int AdVipNum { get; set; }

    [property: JsonPropertyName("ad_vip_end_time")]
    public int AdVipEndTime { get; set; }
}

public record UpgradeVipModel : KgBaseModel
{
    [property: JsonPropertyName("recharge_hours")]
    public int RechargeHours { get; set; }
}