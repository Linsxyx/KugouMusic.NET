using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

/// <summary>
///     原始搜索接口 
/// </summary>
public class RawSearchApi(IKgTransport transport)
{
    /// <summary>
    ///     搜索
    /// </summary>
    /// <param name="type">special：歌单，song：单曲，album：专辑，author：歌手</param>
    public async Task<JsonElement> SearchAsync(string keyword, int page = 1, int pageSize = 30, string type = "song")
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
            Path = $"/{(type == "song" ? "v3" : "v1")}/search/{type}",
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

    public Task<JsonElement> SearchDefaultAsync(string userId = "0", string vipType = "65530")
    {
        var body = new JsonObject
        {
            ["plat"] = 0,
            ["userid"] = long.TryParse(userId, out var uid) ? uid : 0,
            ["tags"] = "{}",
            ["vip_type"] = vipType,
            ["m_type"] = 0,
            ["own_ads"] = new JsonObject(),
            ["ability"] = "3",
            ["sources"] = new JsonArray(),
            ["bitmap"] = 2,
            ["mode"] = "normal"
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/searchnofocus/v1/search_no_focus_word",
            Params = new Dictionary<string, string> { ["clientver"] = "12329" },
            Body = body,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> SearchSuggestAsync(string keyword, int albumTipCount = 10, int correctTipCount = 10,
        int mvTipCount = 10, int musicTipCount = 10)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v2/getSearchTip",
            Params = new Dictionary<string, string>
            {
                ["keyword"] = keyword,
                ["AlbumTipCount"] = albumTipCount.ToString(),
                ["CorrectTipCount"] = correctTipCount.ToString(),
                ["MVTipCount"] = mvTipCount.ToString(),
                ["MusicTipCount"] = musicTipCount.ToString(),
                ["radiotip"] = "1"
            },
            SpecificRouter = "searchtip.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> SearchMixedAsync(string keyword)
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v3/search/mixed",
            Params = new Dictionary<string, string>
            {
                ["ab_tag"] = "0",
                ["ability"] = "511",
                ["albumhide"] = "0",
                ["apiver"] = "22",
                ["area_code"] = "1",
                ["clientver"] = "20125",
                ["cursor"] = "0",
                ["is_gpay"] = "0",
                ["iscorrection"] = "1",
                ["keyword"] = keyword,
                ["nocollect"] = "0",
                ["osversion"] = "16.5",
                ["platform"] = "IOSFilter",
                ["recver"] = "2",
                ["req_ai"] = "1",
                ["requestid"] = $"{KgUtils.Md5($"bdaa53d04e7475feb9024164a47032f9{time}")}_0",
                ["search_ability"] = "3",
                ["sec_aggre"] = "1",
                ["sec_aggre_bitmap"] = "0",
                ["style_type"] = "3",
                ["tag"] = "em"
            },
            SpecificRouter = "complexsearch.kugou.com",
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string> { ["kg-clienttimems"] = time.ToString() }
        });
    }

    public Task<JsonElement> SearchComplexAsync(string keyword, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://complexsearch.kugou.com",
            Path = "/v6/search/complex",
            Params = new Dictionary<string, string>
            {
                ["platform"] = "AndroidFilter",
                ["keyword"] = keyword,
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString(),
                ["cursor"] = "0"
            },
            SignatureType = SignatureType.Default
        });
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

    public async Task<JsonElement> GetSingerDetailAsync(string authorId)
    {
        var body = new JsonObject
        {
            ["author_id"] = authorId
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v3/author",
            Body = body,
            SpecificRouter = "openapi.kugou.com",
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string>
            {
                { "kg-tid", "36" }
            }
        };

        return await transport.SendAsync(request);
    }
}
