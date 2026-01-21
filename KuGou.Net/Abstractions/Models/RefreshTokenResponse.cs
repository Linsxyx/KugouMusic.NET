using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public record RefreshTokenResponse : KgBaseModel
{
    [property: JsonPropertyName("userid")] public long UserId { get; set; }

    [property: JsonPropertyName("token")] public string Token { get; set; }

    [property: JsonPropertyName("is_vip")] public long IsVip { get; set; }
}