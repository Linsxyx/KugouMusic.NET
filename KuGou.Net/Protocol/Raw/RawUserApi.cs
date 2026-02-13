using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawUserApi(IKgTransport transport)
{
    /// <summary>
    ///     获取用户详细信息 
    /// </summary>
    public async Task<JsonElement> GetUserDetailAsync(string userid, string token)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var pkPayload = new JsonObject
        {
            ["token"] = token,
            ["clienttime"] = clientTime
        };
        var pk = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(pkPayload, AppJsonContext.Default.JsonObject))
            .ToUpper();

        var body = new JsonObject
        {
            ["visit_time"] = clientTime,
            ["usertype"] = 1,
            ["p"] = pk,
            ["userid"] = long.Parse(userid)
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://gateway.kugou.com",
            Path = "/v3/get_my_info",
            Params = new Dictionary<string, string> { { "plat", "1" }, { "clienttime", clientTime.ToString() } },
            Body = body,
            SpecificRouter = "usercenter.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取 VIP 信息 
    /// </summary>
    public async Task<JsonElement> GetUserVipDetailAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://kugouvip.kugou.com",
            Path = "/v1/get_union_vip",
            Params = new Dictionary<string, string> { { "busi_type", "concept" } },
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取用户歌单 
    /// </summary>
    public async Task<JsonElement> GetAllListAsync(string userid, string token, int page, int pageSize)
    {
        var body = new JsonObject
        {
            ["userid"] = userid,
            ["token"] = token,
            ["total_ver"] = 979,
            ["type"] = 2,
            ["page"] = page,
            ["pagesize"] = pageSize
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v7/get_all_list",
            Params = new Dictionary<string, string>
            {
                { "plat", "1" },
                { "userid", userid },
                { "token", token }
            },
            Body = body,
            SpecificRouter = "cloudlist.service.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取听歌历史 
    /// </summary>
    public async Task<JsonElement> GetPlayHistoryAsync(string userid, string token, string? bp = null)
    {
        var body = new JsonObject
        {
            ["token"] = token,
            ["userid"] = userid,
            ["source_classify"] = "app",
            ["to_subdivide_sr"] = 1
        };

        if (!string.IsNullOrEmpty(bp)) body["bp"] = bp;

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/playhistory/v1/get_songs",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取听歌排行 
    /// </summary>
    public async Task<JsonElement> GetListenListAsync(string userid, string token, int type)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pkPayload = new JsonObject
        {
            ["clienttime"] = clientTime,
            ["token"] = token
        };
        var p = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(pkPayload, AppJsonContext.Default.JsonObject))
            .ToUpper();

        var body = new JsonObject
        {
            ["t_userid"] = userid,
            ["userid"] = userid,
            ["list_type"] = type,
            ["area_code"] = 1,
            ["cover"] = 2,
            ["p"] = p
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://listenservice.kugou.com",
            Path = "/v2/get_list",
            Params = new Dictionary<string, string> { { "plat", "0" } },
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取关注歌手 
    /// </summary>
    public async Task<JsonElement> GetFollowSingerListAsync(string userid, string token)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var pkPayload = new JsonObject
        {
            ["clienttime"] = clientTime,
            ["token"] = token
        };
        var p = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(pkPayload, AppJsonContext.Default.JsonObject))
            .ToUpper();

        var body = new JsonObject
        {
            ["merge"] = 2,
            ["need_iden_type"] = 1,
            ["ext_params"] = "k_pic,jumptype,singerid,score",
            ["userid"] = userid,
            ["type"] = 0,
            ["id_type"] = 0,
            ["p"] = p
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v4/follow_list",
            Params = new Dictionary<string, string> { { "plat", "1" } },
            Body = body,
            SpecificRouter = "relationuser.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     领取当天 VIP
    /// </summary>
    public async Task<JsonElement> GetOneDayVipAsync()
    {
        var receiveDay = DateTime.Today.ToString("yyyy-MM-dd");
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v1/recharge/receive_vip_listen_song",
            Params = new Dictionary<string, string> { { "source_id", "90139" }, { "receive_day", receiveDay } },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     升级 VIP
    /// </summary>
    public async Task<JsonElement> UpgradeVipAsync(string userid)
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v1/listen_song/upgrade_vip_reward",
            Params = new Dictionary<string, string>
            {
                { "kugouid", userid },
                { "ad_type", "1" }
            },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }
    
    
    /// <summary>
    ///     获取当月已领取 VIP 天数
    /// </summary>
    public async Task<JsonElement> GetVipRecordAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v1/activity/get_month_vip_record",
            Params = new Dictionary<string, string>
            {
                { "latest_limit", "100" },
            },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }
}