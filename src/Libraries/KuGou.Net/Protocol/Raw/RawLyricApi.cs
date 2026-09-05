using System.Text.Json;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawLyricApi(IKgTransport transport)
{
    private const string LyricHost = "https://lyrics.kugou.com";

    /// <summary>
    ///     搜索歌词 (获取 id 和 accesskey)
    /// </summary>
    public async Task<JsonElement> SearchLyricAsync(string? hash, string? albumAudioId, string? keyword, string? man)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "album_audio_id", albumAudioId ?? "0" },
            { "appid", KuGouConfig.AppId },
            { "clientver", KuGouConfig.ClientVer },
            { "duration", "0" },
            { "hash", hash ?? "" },
            { "keyword", keyword ?? "" },
            { "lrctxt", "1" },
            { "man", man ?? "no" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = LyricHost,
            Path = "/v1/search",
            Params = paramsDict,
            SignatureType = SignatureType.Default
        };

        var result = await transport.SendAsync(request);

        // /v1/search 对部分直连 IP 返回 200 + 空响应（IP 风控），此时回退到老接口 /search。
        // 两个接口的响应结构一致（candidates[].id/accesskey），老接口无 fmt 字段，调用方默认按 krc 处理。
        if (!HasCandidates(result))
            result = await transport.SendAsync(BuildLegacySearchRequest(hash, keyword));

        return result;
    }

    private static KgRequest BuildLegacySearchRequest(string? hash, string? keyword)
    {
        return new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = LyricHost,
            Path = "/search",
            Params = new Dictionary<string, string>
            {
                { "ver", "1" },
                { "man", "yes" },
                { "client", "pc" },
                { "duration", "0" },
                { "hash", hash ?? "" },
                { "keyword", keyword ?? "" }
            },
            SignatureType = SignatureType.Default
        };
    }

    private static bool HasCandidates(JsonElement json)
    {
        return json.ValueKind == JsonValueKind.Object
               && json.TryGetProperty("candidates", out var candidates)
               && candidates.ValueKind == JsonValueKind.Array
               && candidates.GetArrayLength() > 0;
    }

    /// <summary>
    ///     下载歌词 (获取 content 字段)
    /// </summary>
    public async Task<JsonElement> DownloadLyricAsync(string id, string accessKey, string fmt = "krc")
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "ver", "1" },
            { "client", "android" },
            { "id", id },
            { "accesskey", accessKey },
            { "fmt", fmt },
            { "charset", "utf8" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = LyricHost,
            Path = "/download",
            Params = paramsDict,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }
}