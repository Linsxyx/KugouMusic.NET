using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KuGou.Net.Abstractions.Models;

namespace KuGou.Net.Adapters.Common;

public static class KgApiResponseParser
{
    /// <summary>
    ///     解析响应：校验状态 -> 提取Data -> 反序列化 -> ★回填外层状态★
    ///     <para>AOT 兼容版本</para>
    /// </summary>
    public static T? Parse<T>(JsonElement root, JsonTypeInfo<T> typeInfo)
    {
        // 1. 捕获外层状态 (Root Level Status)
        int? rootStatus = null;
        int? rootErrCode = null;

        if (root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number)
            rootStatus = s.GetInt32();

        if (root.TryGetProperty("error_code", out var e) && e.ValueKind == JsonValueKind.Number)
            rootErrCode = e.GetInt32();

        if (rootErrCode == null && root.TryGetProperty("errcode", out var ec) && ec.ValueKind == JsonValueKind.Number)
            rootErrCode = ec.GetInt32();

        var isSuccess = (rootStatus.HasValue && rootStatus.Value == 1) ||
                        (rootErrCode.HasValue && rootErrCode.Value == 0);

        //if (!isSuccess) return default; 

        var targetElement = root;
        if (root.TryGetProperty("data", out var innerData) && innerData.ValueKind != JsonValueKind.Null)
            targetElement = innerData;

        if (typeof(T) == typeof(JsonElement)) return (T)(object)targetElement;

        var result = targetElement.Deserialize(typeInfo);

        if (result is KgBaseModel baseModel)
            if (baseModel.Status == null && rootStatus.HasValue)
                baseModel.Status = rootStatus;

        return result;
    }
}