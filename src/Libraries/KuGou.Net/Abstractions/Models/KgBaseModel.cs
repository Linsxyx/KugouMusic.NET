using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     所有模型的基类，自动捕获多余字段
/// </summary>
public abstract record KgBaseModel
{
    [property: JsonPropertyName("status")] public int? Status { get; set; }

    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extras { get; set; }

    /// <summary>
    ///     方便的辅助方法：从 Extras 里取值
    /// </summary>
    public string? GetExtraString(string key)
    {
        if (Extras != null && Extras.TryGetValue(key, out var val))
            return val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
        return null;
    }
}