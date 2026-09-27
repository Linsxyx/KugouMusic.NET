using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

public sealed record VerificationInfo : KgBaseModel
{
    [JsonPropertyName("sessionid")] public string? SessionId { get; set; }
    [JsonPropertyName("txappid")] public JsonElement TencentAppIdValue { get; set; }
    [JsonIgnore] public string TencentAppId => TencentAppIdValue.ToString();
    [JsonPropertyName("v_type")] public int VerificationType { get; set; }
}

public sealed record VerificationResult : KgBaseModel
{
    [JsonIgnore]
    public bool IsSuccess => Status == 1 && (ErrorCode is null or 0) && !RequiresVerification;
}
