using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawReportApi(IKgTransport transport)
{
    public Task<JsonElement> UploadPlayHistoryAsync(string userid, string token, long mixSongId,
        long? timestamp = null, int playCount = 1)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/playhistory/v1/upload_songs",
            Params = new Dictionary<string, string> { ["plat"] = "3" },
            Body = new JsonObject
            {
                ["songs"] = new JsonArray(new JsonObject
                {
                    ["mxid"] = mixSongId,
                    ["op"] = 1,
                    ["ot"] = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["pc"] = playCount
                }),
                ["token"] = token,
                ["userid"] = userid
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLatestSongsAsync(string userid, string token, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/playque/devque/v1/get_latest_songs",
            Body = new JsonObject
            {
                ["area_code"] = "1",
                ["sources"] = new JsonArray("pc", "mobile", "tv", "car"),
                ["userid"] = long.TryParse(userid, out var uid) ? uid : 0,
                ["ret_info"] = 1,
                ["token"] = token,
                ["pagesize"] = pageSize
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> AddListenTimeAsync(string userid, string token, string mid)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pPayload = new JsonObject
        {
            ["token"] = token,
            ["clienttime_ms"] = clientTime
        };
        var p = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(pPayload, AppJsonContext.Default.JsonObject))
            .ToUpper();

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "http://userinfo.user.kugou.com",
            Path = "/v2/get_grade_info",
            Body = new JsonObject
            {
                ["p"] = p,
                ["appid"] = KuGouConfig.AppId,
                ["mid"] = mid,
                ["clientver"] = KuGouConfig.ClientVer,
                ["clienttime"] = clientTime,
                ["type"] = "1",
                ["uuid"] = "",
                ["userid"] = userid,
                ["key"] = KgSigner.CalcLoginKey(clientTime)
            },
            SignatureType = SignatureType.Default
        });
    }
}
