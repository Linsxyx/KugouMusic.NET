using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawArtistApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetDetailAsync(string id)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v3/author",
            Body = new JsonObject { ["author_id"] = id },
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["kg-tid"] = "36" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAudiosAsync(string id, int page = 1, int pageSize = 30, string sort = "new")
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var session = sessionManager.Session;

        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["clientver"] = KuGouConfig.ClientVer,
            ["mid"] = KgUtils.CalcNewMid(session.Dfid),
            ["clienttime"] = clientTime,
            ["key"] = KgSigner.CalcLoginKey(clientTime),
            ["author_id"] = id,
            ["pagesize"] = pageSize,
            ["page"] = page,
            ["sort"] = sort == "hot" ? 1 : 2,
            ["area_code"] = "all"
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://openapi.kugou.com",
            Path = "/kmr/v1/audio_group/author",
            Body = body,
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["kg-tid"] = "220" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAlbumsAsync(string id, int page = 1, int pageSize = 30, string sort = "new")
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v1/author/albums",
            Body = new JsonObject
            {
                ["author_id"] = id,
                ["pagesize"] = pageSize,
                ["page"] = page,
                ["sort"] = sort == "hot" ? 3 : 1,
                ["category"] = 1,
                ["area_code"] = "all"
            },
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["kg-tid"] = "36" },
            SignatureType = SignatureType.Default
        });
    }
}
