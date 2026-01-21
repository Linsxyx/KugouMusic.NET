using System.Text;
using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;
using JsonElement = System.Text.Json.JsonElement;

namespace KuGou.Net.Clients;

public class LyricClient(RawLyricApi rawApi)
{
    /// <summary>
    ///     搜索歌词
    /// </summary>
    public async Task<JsonElement> SearchLyricAsync(string? hash, string? albumAudioId, string? keyword, string? man)
    {
        return await rawApi.SearchLyricAsync(hash, albumAudioId, keyword, man);
    }

    /// <summary>
    ///     获取并解密歌词
    /// </summary>
    /// <param name="decode">是否自动解密 Base64/KRC 内容</param>
    public async Task<LyricResult> GetLyricAsync(
        string id,
        string accessKey,
        string fmt = "krc",
        bool decode = true)
    {
        var json = await rawApi.DownloadLyricAsync(id, accessKey, fmt);

        string? decodedContent = null;
        string? decodedTrans = null;

        if (decode)
        {
            if (json.TryGetProperty("content", out var contentElem)
                && contentElem.ValueKind == JsonValueKind.String)
            {
                var base64 = contentElem.GetString();
                if (!string.IsNullOrEmpty(base64))
                {
                    var contentType = json.TryGetProperty("contenttype", out var t)
                        ? t.GetInt32()
                        : 0;

                    decodedContent =
                        fmt == "lrc" || contentType != 0
                            ? Encoding.UTF8.GetString(Convert.FromBase64String(base64))
                            : KgCrypto.DecodeLyrics(base64);
                }
            }

            if (json.TryGetProperty("trans", out var transElem)
                && transElem.ValueKind == JsonValueKind.String)
                decodedTrans = Encoding.UTF8.GetString(
                    Convert.FromBase64String(transElem.GetString()!)
                );
        }

        return new LyricResult(
            json.GetProperty("content").GetString(),
            decodedContent,
            decodedTrans,
            json
        );
    }
}