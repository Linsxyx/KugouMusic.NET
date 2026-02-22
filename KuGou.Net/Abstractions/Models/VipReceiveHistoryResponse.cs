using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     VIP 领取记录响应 (对应 data 节点)
/// </summary>
public record VipReceiveHistoryResponse : KgBaseModel
{
    /// <summary>
    ///     当前查询的月份 (格式: yyyy-MM)
    /// </summary>
    [property: JsonPropertyName("month")]
    public string Month { get; set; } = "";

    /// <summary>
    ///     服务器时间戳
    /// </summary>
    [property: JsonPropertyName("server_time")]
    public long ServerTime { get; set; }

    /// <summary>
    ///     领取详情列表
    /// </summary>
    [property: JsonPropertyName("list")]
    public List<VipReceiveItem> Items { get; set; } = new();

    /// <summary>
    ///     签到列表 (JSON 中为空，暂时用 object 占位，或定义具体类)
    /// </summary>
    [property: JsonPropertyName("sign_list")]
    public List<object> SignList { get; set; } = new();

    /// <summary>
    ///     未来/剩余时长统计信息
    /// </summary>
    [property: JsonPropertyName("future_duration")]
    public VipFutureDuration? FutureDuration { get; set; }
}

/// <summary>
///     单日 VIP 领取详情
/// </summary>
public record VipReceiveItem
{
    /// <summary>
    ///     日期 (格式: yyyy-MM-dd)
    /// </summary>
    [property: JsonPropertyName("day")]
    public string Day { get; set; } = "";

    /// <summary>
    ///     是否领取 (1: 已领取)
    /// </summary>
    [property: JsonPropertyName("receive_vip")]
    public int IsReceived { get; set; }

    /// <summary>
    ///     VIP 类型 (tvip: 概念版, svip: 超级会员)
    /// </summary>
    [property: JsonPropertyName("vip_type")]
    public string VipType { get; set; } = "";
}

/// <summary>
///     时长统计详情
/// </summary>
public record VipFutureDuration
{
    [property: JsonPropertyName("up_seconds")]
    public int UpSeconds { get; set; }

    [property: JsonPropertyName("duration")]
    public int Duration { get; set; }

    [property: JsonPropertyName("seconds")]
    public int Seconds { get; set; }

    [property: JsonPropertyName("month_num")]
    public int MonthNum { get; set; }
}