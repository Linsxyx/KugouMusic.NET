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
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/rank/list",
            Params = new Dictionary<string, string>
            {
                ["plat"] = "2",
                ["withsong"] = (withsong ?? 1).ToString(),
                ["parentid"] = "0"
            },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }

    public Task<JsonElement> GetRankInfoAsync(int rankId, int? rankCid = null, int albumImg = 1, string? zone = null)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/rank/info",
            Params = new Dictionary<string, string>
            {
                ["rank_cid"] = (rankCid ?? 0).ToString(),
                ["rankid"] = rankId.ToString(),
                ["with_album_img"] = albumImg.ToString(),
                ["zone"] = zone ?? ""
            },
            SignatureType = SignatureType.Default
        });
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
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/rank/vol",
            Params = new Dictionary<string, string>
            {
                ["rank_cid"] = (rankCid ?? 0).ToString(),
                ["rank_id"] = rankId.ToString(),
                ["ranktype"] = "0",
                ["type"] = "0",
                ["plat"] = "2"
            },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }
}
