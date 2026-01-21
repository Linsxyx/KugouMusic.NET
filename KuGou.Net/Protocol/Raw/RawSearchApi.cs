using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

/// <summary>
///     原始搜索接口 (对应原来的 SearchService)
/// </summary>
public class RawSearchApi(IKgTransport transport)
{
    // 对应: /v3/search/song
    public async Task<JsonElement> SearchSongAsync(string keyword, int page = 1, int pageSize = 30)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "keyword", keyword },
            { "page", page.ToString() },
            { "pagesize", pageSize.ToString() },
            { "platform", "AndroidFilter" },
            { "iscorrection", "1" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v3/search/song",
            Params = paramsDict,
            SpecificRouter = "complexsearch.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    // 获取播放链接
    public async Task<JsonElement> GetPlayUrlAsync(string hash, string? quality = "128")
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "album_id", "0" },
            { "area_code", "1" },
            { "hash", hash.ToLower() },
            { "ssa_flag", "is_fromtrack" },
            { "version", KuGouConfig.Version },
            { "page_id", "967177915" },
            { "quality", quality ?? "128" },
            { "album_audio_id", "0" },
            { "behavior", "play" },
            { "pid", "411" },
            { "cmd", "26" },
            { "pidversion", "3001" },
            { "IsFreePart", "0" },
            { "ppage_id", "356753938,823673182,967485191" },
            { "cdnBackup", "1" },
            { "kcard", "0" },
            { "module", "" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v5/url",
            Params = paramsDict,
            SpecificRouter = "trackercdn.kugou.com",
            SignatureType = SignatureType.V5,
            SpecificDfid = Guid.NewGuid().ToString("N")[..24]
        };

        return await transport.SendAsync(request);
    }


    //获取热搜
    public async Task<JsonElement> SearchHotAsync()
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "navid", "1" },
            { "plat", "2" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/api/v3/search/hot_tab",
            Params = paramsDict,
            SpecificRouter = "msearch.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }


    public async Task<JsonElement> GetSingerSongsAsync(
        string dfid,
        string authorId,
        int page = 1,
        int pageSize = 30,
        string sort = "new")
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. 计算特殊 Key: md5(appid + salt + clientver + time)
        var keyRaw = $"{KuGouConfig.AppId}{KuGouConfig.LiteSalt}{KuGouConfig.ClientVer}{clientTime}";
        var key = KgUtils.Md5(keyRaw);
        var mid = KgUtils.CalcNewMid(dfid);

        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["clientver"] = KuGouConfig.ClientVer,
            ["mid"] = mid,
            ["clienttime"] = clientTime,
            ["key"] = key,
            ["author_id"] = authorId,
            ["pagesize"] = pageSize,
            ["page"] = page,
            ["sort"] = sort == "hot" ? 1 : 2, // 1：最热，2：最新
            ["area_code"] = "all"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://openapi.kugou.com",
            Path = "/kmr/v1/audio_group/author",
            Body = body,
            SpecificRouter = "openapi.kugou.com",
            SignatureType = SignatureType.Default,

            CustomHeaders = new Dictionary<string, string>
            {
                { "kg-tid", "220" }
            }
        };

        return await transport.SendAsync(request);
    }
}