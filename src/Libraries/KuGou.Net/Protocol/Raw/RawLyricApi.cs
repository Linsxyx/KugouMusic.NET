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

        return await transport.SendAsync(request);
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