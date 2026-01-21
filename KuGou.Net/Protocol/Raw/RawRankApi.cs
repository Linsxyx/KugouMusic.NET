using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;

namespace KuGou.Net.Protocol.Raw;

public class RawRankApi(IKgTransport transport)
{
    /// <summary>
    ///     获取排行榜音乐列表
    /// </summary>
    public async Task<JsonElement> GetRankAudioAsync(int rankId, int? rankCid = null, int? page = null,
        int? pageSize = null)
    {
        var body = new JsonObject
        {
            ["show_portrait_mv"] = 1,
            ["show_type_total"] = 1,
            ["filter_original_remarks"] = 1,
            ["area_code"] = 1,
            ["pagesize"] = pageSize ?? 30,
            ["rank_cid"] = rankCid ?? 0,
            ["type"] = 1,
            ["page"] = page ?? 1,
            ["rank_id"] = rankId
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/openapi/kmr/v2/rank/audio",
            Body = body,
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string>
            {
                { "kg-tid", "369" }
            }
        };
        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     获取排行榜列表
    /// </summary>
    public async Task<JsonElement> GetRankListAsync(int? withsong = null)
    {
        var body = new JsonObject
        {
            ["plat"] = 2,
            ["withsong"] = withsong ?? 1,
            ["parentid"] = 0
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/rank/list",
            Body = body,
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取排行榜推荐列表
    /// </summary>
    public async Task<JsonElement> GetRankTopAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/mobileservice/api/v5/rank/rec_rank_list",
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取排行榜往期列表
    /// </summary>
    public async Task<JsonElement> GetRankVolAsync(int rankId, int? rankCid = null)
    {
        var body = new JsonObject
        {
            ["rank_cid"] = rankCid ?? 0,
            ["rank_id"] = rankId,
            ["ranktype"] = 0,
            ["type"] = 0,
            ["plat"] = 2
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/rank/vol",
            Body = body,
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }
}