using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     登录响应数据
/// </summary>
public record LoginResponse(
    [property: JsonPropertyName("userid")] string UserId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("nickname")]
    string? Nickname,
    [property: JsonPropertyName("vip_type")]
    string? VipType,
    [property: JsonPropertyName("vip_token")]
    string? VipToken
)
{
    [JsonExtensionData] public Dictionary<string, object>? ExtraData { get; set; }
}

/// <summary>
///     二维码 Key 响应
/// </summary>
public record QrKeyResponse(
    [property: JsonPropertyName("qrcode_key")]
    string QrCodeKey,
    [property: JsonPropertyName("qrcode_url")]
    string QrCodeUrl
)
{
    [JsonExtensionData] public Dictionary<string, object>? ExtraData { get; set; }
}

/// <summary>
///     二维码状态响应
/// </summary>
public record QrStatusResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("msg")] string? Message,
    [property: JsonPropertyName("userid")] string? UserId,
    [property: JsonPropertyName("token")] string? Token
)
{
    [JsonExtensionData] public Dictionary<string, object>? ExtraData { get; set; }
}

/// <summary>
///     发送验证码响应
/// </summary>
public record SendCodeResponse(
    [property: JsonPropertyName("msg")] string? Message
)
{
    [JsonExtensionData] public Dictionary<string, object>? ExtraData { get; set; }
}