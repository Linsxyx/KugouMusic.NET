using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     登录响应数据
/// </summary>
public record LoginResponse : KgBaseModel
{
    /// <summary>
    ///     用户 ID。
    /// </summary>
    [property: JsonPropertyName("userid")] public long? UserId { get; set; }

    /// <summary>
    ///     登录 Token。
    /// </summary>
    [property: JsonPropertyName("token")] public string? Token { get; set; }

    /// <summary>
    ///     附加登录凭证 `t1`。
    /// </summary>
    [property: JsonPropertyName("t1")] public string? T1 { get; set; }

    /// <summary>
    ///     多账号登录时返回的候选账号数据。
    /// </summary>
    [property: JsonPropertyName("data")] public LoginMultiAccountData? Data { get; set; }

    /// <summary>
    ///     是否需要调用方选择账号后携带 userid 重新登录。
    /// </summary>
    [JsonIgnore]
    public bool RequiresUserSelection => ErrorCode == 34175 && Data?.InfoList is { Count: > 0 };
}

public record LoginMultiAccountData
{
    [property: JsonPropertyName("info_list")] public List<LoginAccountInfo> InfoList { get; set; } = [];
}

public record LoginAccountInfo
{
    [property: JsonPropertyName("nickname")] public string? Nickname { get; set; }

    [property: JsonPropertyName("pic")] public string? Pic { get; set; }

    [property: JsonPropertyName("userid")] public long UserId { get; set; }

    [property: JsonPropertyName("appid")] public int AppId { get; set; }

    [property: JsonPropertyName("username")] public string? Username { get; set; }
}

/// <summary>
///     发送验证码响应
/// </summary>
public record SendCodeResponse : KgBaseModel
{
    /// <summary>
    ///     接口返回状态码。
    /// </summary>
    [property: JsonPropertyName("code")] public long Code { get; set; }
}
