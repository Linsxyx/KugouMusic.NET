using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawFmApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetRecommendAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/rcmd_list",
            Body = SignedBody(now, new JsonObject
            {
                ["rcmdsongcount"] = 1,
                ["level"] = 0,
                ["area_code"] = 1,
                ["get_tracker"] = 1,
                ["uid"] = 0
            }),
            SpecificRouter = "fm.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSongsAsync(string fmIds, int type = 2, int offset = -1, int size = 20)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new JsonArray();
        foreach (var id in fmIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject
            {
                ["fmid"] = id,
                ["fmtype"] = type,
                ["offset"] = offset,
                ["size"] = size,
                ["singername"] = ""
            });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/app_song_list_offset",
            Body = SignedBody(now, new JsonObject
            {
                ["area_code"] = 1,
                ["data"] = data,
                ["get_tracker"] = 1,
                ["uid"] = sessionManager.Session.UserId
            }),
            SpecificRouter = "fm.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetClassSongAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var userId = sessionManager.Session.UserId;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/class_fm_song",
            Body = SignedBody(now, new JsonObject
            {
                ["kguid"] = userId,
                ["platform"] = "android",
                ["uid"] = userId,
                ["get_tracker"] = 1
            }),
            SpecificRouter = "fm.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetImagesAsync(string fmIds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new JsonArray();
        foreach (var id in fmIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject
            {
                ["fields"] = "imgUrl100,imgUrl50",
                ["fmid"] = id,
                ["fmtype"] = 2
            });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/fm_info",
            Body = SignedBody(now, new JsonObject
            {
                ["data"] = data,
                ["dfid"] = sessionManager.Session.Dfid
            }),
            SpecificRouter = "fm.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    private JsonObject SignedBody(long now, JsonObject body)
    {
        body["appid"] = KuGouConfig.AppId;
        body["clienttime"] = now;
        body["clientver"] = KuGouConfig.ClientVer;
        body["key"] = KgSigner.CalcLoginKey(now);
        body["mid"] = KgUtils.CalcNewMid(sessionManager.Session.Dfid);
        return body;
    }
}
