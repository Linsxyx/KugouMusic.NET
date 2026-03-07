using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     二维码扫描状态枚举
/// </summary>
public enum QrLoginStatus
{
    /// <summary>
    ///     二维码已过期
    /// </summary>
    Expired = 0,

    /// <summary>
    ///     等待扫码
    /// </summary>
    WaitingForScan = 1,

    /// <summary>
    ///     已扫码，等待手机端确认
    /// </summary>
    WaitingForConfirm = 2,

    /// <summary>
    ///     授权登录成功
    /// </summary>
    Success = 4
}

/// <summary>
///     二维码状态查询响应 (对应 data 节点)
/// </summary>
public record QrLoginStatusResponse : KgBaseModel
{
    /// <summary>
    ///     二维码当前状态
    /// </summary>
    [JsonIgnore]
    public QrLoginStatus QrStatus => (QrLoginStatus)Status!;

    /// <summary>
    ///     用户ID
    /// </summary>
    [property: JsonPropertyName("userid")]
    public long? UserId { get; set; }

    /// <summary>
    ///     用户昵称
    /// </summary>
    [property: JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    /// <summary>
    ///     用户头像
    /// </summary>
    [property: JsonPropertyName("pic")]
    public string? Pic { get; set; }

    /// <summary>
    ///     登录凭证 (仅状态为 4 时返回)
    /// </summary>
    [property: JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    ///     辅助判断属性：是否彻底登录成功
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => QrStatus == QrLoginStatus.Success && !string.IsNullOrEmpty(Token);
}