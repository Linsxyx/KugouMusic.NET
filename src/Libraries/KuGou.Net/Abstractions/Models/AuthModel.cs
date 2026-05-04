using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     登录响应数据
/// </summary>
public record LoginResponse : KgBaseModel
{
    [property: JsonPropertyName("userid")] public long? UserId { get; set; }

    [property: JsonPropertyName("token")] public string? Token { get; set; }

    [property: JsonPropertyName("t1")] public string? T1 { get; set; }
}

/// <summary>
///     发送验证码响应
/// </summary>
public record SendCodeResponse : KgBaseModel
{
    [property: JsonPropertyName("code")] public long Code { get; set; }
}