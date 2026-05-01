using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawDiscoveryApi(IKgTransport transport)
{
    /// <summary>
    ///     获取推荐歌单
    /// </summary>
    /// <param name="categoryId">0: 推荐, 11292: HI-RES</param>
    public async Task<JsonElement> GetRecommendedPlaylistsAsync(
        string userid,
        string dfid,
        int categoryId = 0,
        int page = 1,
        int pageSize = 30)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var key = KgSigner.CalcLoginKey(clientTime);

        // 2. 计算 mid 
        var mid = KgUtils.Md5(string.IsNullOrEmpty(dfid) ? "-" : dfid);

        // 3. 构建内部对象 special_recommend
        var specialRecommend = new JsonObject
        {
            ["withtag"] = 1,
            ["withsong"] = 0,
            ["sort"] = 1,
            ["ugc"] = 1,
            ["is_selected"] = 0,
            ["withrecommend"] = 1,
            ["area_code"] = 1,
            ["categoryid"] = categoryId
        };

        // 4. 构建主 Body
        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["mid"] = mid,
            ["clientver"] = KuGouConfig.ClientVer,
            ["platform"] = "android",
            ["clienttime"] = clientTime,
            ["userid"] = userid,
            ["module_id"] = 1,
            ["page"] = page,
            ["pagesize"] = pageSize,
            ["key"] = key,
            ["special_recommend"] = specialRecommend,
            ["req_multi"] = 1,
            ["retrun_min"] = 5,
            ["return_special_falg"] = 1
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/special_recommend",
            Body = body,
            SpecificRouter = "specialrec.service.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     新歌速递
    /// </summary>
    /// <param name="rankId">默认 21608 (华语新歌?)</param>
    public async Task<JsonElement> GetNewSongsAsync(
        string userid,
        int rankId = 21608,
        int page = 1,
        int pageSize = 30)
    {
        var body = new JsonObject
        {
            ["rank_id"] = rankId,
            ["userid"] = userid,
            ["page"] = page,
            ["pagesize"] = pageSize,
            ["tags"] = new JsonArray()
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/musicadservice/container/v1/newsong_publish",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     获取每日推荐
    /// </summary>
    public async Task<JsonElement> GetRecommendSongAsync(string? userid)
    {
        var body = new JsonObject
        {
            ["platform"] = "android",
            ["userid"] = userid ?? "0"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/everyday_song_recommend",
            Body = body,
            SignatureType = SignatureType.Default,
            SpecificRouter = "everydayrec.service.kugou.com"
        };
        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     获取每日风格推荐
    /// </summary>
    public async Task<JsonElement> GetRecommendStyleSongAsync()
    {
        var body = new JsonObject
        {
            ["platform"] = "android"
        };
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/everydayrec.service/everyday_style_recommend",
            Body = body,
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                { "tagids", "" }
            }
        };
        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetAiRecommendAsync(
        string userid,
        string? mid,
        string? albumAudioIds = null)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var recommendSource = new JsonArray();

        if (!string.IsNullOrWhiteSpace(albumAudioIds))
        {
            foreach (var id in albumAudioIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(id, out var parsedId))
                    recommendSource.Add(new JsonObject { ["ID"] = parsedId });
        }

        var body = new JsonObject
        {
            ["platform"] = "ios",
            ["clientver"] = KuGouConfig.ClientVer,
            ["clienttime"] = clientTime,
            ["userid"] = userid,
            ["client_playlist"] = new JsonArray(),
            ["source_type"] = 2,
            ["playlist_ver"] = 2,
            ["area_code"] = 1,
            ["appid"] = KuGouConfig.AppId,
            ["key"] = KgSigner.CalcLoginKey(clientTime),
            ["mid"] = string.IsNullOrWhiteSpace(mid) ? "-" : mid,
            ["recommend_source"] = recommendSource
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/recommend",
            Body = body,
            SpecificRouter = "songlistairec.kugou.com",
            ClearDefaultParams = true,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetYuekuAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v1/yueku/recommend_v2",
            SpecificRouter = "service.mobile.kugou.com",
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                ["operator"] = "7",
                ["plat"] = "0",
                ["type"] = "11",
                ["area_code"] = "1",
                ["req_multi"] = "1"
            }
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetYuekuBannerAsync(string userid)
    {
        var body = new JsonObject
        {
            ["plat"] = 0,
            ["channel"] = 201,
            ["operator"] = 7,
            ["networktype"] = 2,
            ["userid"] = userid,
            ["vip_type"] = 0,
            ["m_type"] = 0,
            ["tags"] = new JsonArray(),
            ["apiver"] = 5,
            ["ability"] = 2,
            ["mode"] = "normal"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/ads.gateway/v3/listen_banner",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetYuekuFmAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v1/time_fm_info",
            SpecificRouter = "fm.service.kugou.com",
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                ["operator"] = "7",
                ["plat"] = "0",
                ["type"] = "11",
                ["area_code"] = "1",
                ["req_multi"] = "1"
            }
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetTopAlbumsAsync(string token, int page = 1, int pageSize = 30)
    {
        var body = new JsonObject
        {
            ["apiver"] = 20,
            ["token"] = token,
            ["page"] = page,
            ["pagesize"] = pageSize,
            ["withpriv"] = 1
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/musicadservice/v1/mobile_newalbum_sp",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetTopCardAsync(
        string userid,
        string? mid,
        int cardId = 1)
    {
        const string fakem = "60f7ebf1f812edbac3c63a7310001701760f";
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["clientver"] = KuGouConfig.ClientVer,
            ["platform"] = "android",
            ["clienttime"] = clientTime,
            ["userid"] = userid,
            ["key"] = KgSigner.CalcLoginKey(clientTime),
            ["fakem"] = fakem,
            ["area_code"] = 1,
            ["mid"] = string.IsNullOrWhiteSpace(mid) ? "-" : mid,
            ["uuid"] = "-",
            ["client_playlist"] = new JsonArray(),
            ["u_info"] = "a0c35cd40af564444b5584c2754dedec"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/singlecardrec.service/v1/single_card_recommend",
            Body = body,
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                ["card_id"] = cardId.ToString(),
                ["fakem"] = fakem,
                ["area_code"] = "1",
                ["platform"] = "ios"
            }
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetTopCardYouthAsync(
        int cardId = 3005,
        int pageSize = 30,
        string? tagId = null)
    {
        var body = new JsonObject
        {
            ["tagid"] = tagId ?? string.Empty,
            ["u_info"] = string.Empty,
            ["source_mixsong"] = string.Empty
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v1/song/single_card_recommend",
            Body = body,
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                ["card_id"] = cardId.ToString(),
                ["area_code"] = "1",
                ["platform"] = "ops",
                ["module_id"] = "1",
                ["ver"] = "v2",
                ["pagesize"] = pageSize.ToString()
            }
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetTopIpAsync()
    {
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/daily_recommend",
            BaseUrl = "http://musicadservice.kugou.com",
            Body = new JsonObject { ["tags"] = new JsonObject() },
            SignatureType = SignatureType.Default,
            Params = new Dictionary<string, string>
            {
                ["clientver"] = "12349",
                ["area_code"] = "1"
            }
        };

        var json = await transport.SendAsync(request);
        return AddIpIdFromInnerUrl(json);
    }

    public async Task<JsonElement> GetPcDiantaiAsync(string userid)
    {
        var body = new JsonObject
        {
            ["isvip"] = 0,
            ["userid"] = userid,
            ["vipType"] = 0
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v3/pc_diantai",
            BaseUrl = "https://adservice.kugou.com",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    private static JsonElement AddIpIdFromInnerUrl(JsonElement json)
    {
        var node = JsonNode.Parse(json.GetRawText());
        if (node?["status"]?.GetValue<int>() != 1) return json;

        var list = node["data"]?["list"]?.AsArray();
        if (list == null) return json;

        foreach (var item in list)
        {
            var innerUrl = item?["extra"]?["inner_url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(innerUrl)) continue;

            var index = innerUrl.LastIndexOf("ip_id", StringComparison.Ordinal);
            if (index == -1) continue;

            var ipIdText = innerUrl[(index + 6)..];
            if (int.TryParse(ipIdText, out var ipId)) item!["extra"]!["ip_id"] = ipId;
        }

        return JsonSerializer.SerializeToElement(node);
    }
    
    /// <summary>
    /// 获取私人推荐 (私人FM / 电台) 及 行为上报
    /// </summary>
    public async Task<JsonElement> GetPersonalRecommendAsync(
        string userid, 
        string token, 
        string vipType, 
        string mid,
        string? hash = null, 
        string? songid = null, 
        int? playtime = null,
        string action = "play",
        int songPoolId = 0,
        int remainSongCount = 0,
        bool isOverplay = false,
        string mode = "normal")
    {
        var clientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); 
        var key = KgSigner.CalcLoginKey(clientTimeMs);

        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["clienttime"] = clientTimeMs,
            ["mid"] = string.IsNullOrEmpty(mid) ? "-" : mid,
            ["action"] = action, 
            ["recommend_source_locked"] = 0,
            ["song_pool_id"] = songPoolId, 
            ["callerid"] = 0,
            ["m_type"] = 1,
            ["platform"] = "android", 
            ["area_code"] = 1,
            ["remain_songcnt"] = remainSongCount, 
            ["clientver"] = KuGouConfig.ClientVer,["is_overplay"] = isOverplay ? 1 : 0,
            ["mode"] = mode, 
            ["fakem"] = "ca981cfc583a4c37f28d2d49000013c16a0a",
            ["key"] = key
        };

        if (!string.IsNullOrEmpty(userid) && userid != "0")
        {
            body["userid"] = userid;
            body["kguid"] = userid;
        }

        if (!string.IsNullOrEmpty(token)) body["token"] = token;
        if (!string.IsNullOrEmpty(vipType)) body["vip_type"] = vipType;

        if (!string.IsNullOrEmpty(hash)) body["hash"] = hash;
        if (!string.IsNullOrEmpty(songid)) body["songid"] = songid;
        if (playtime.HasValue) body["playtime"] = playtime.Value;

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/personal_recommend",
            Body = body,
            SpecificRouter = "persnfm.service.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    public Task<JsonElement> GetBrushAsync(string userid, string vipType, string? mid, int songPoolId = 0,
        string mode = "normal")
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var personalRecommend = new JsonObject
        {
            ["userid"] = userid,
            ["appid"] = KuGouConfig.AppId,
            ["playlist_ver"] = 2,
            ["clienttime"] = clientTime,
            ["mid"] = string.IsNullOrWhiteSpace(mid) ? "-" : mid,
            ["new_sync_point"] = clientTime,
            ["module_id"] = 1,
            ["action"] = "login",
            ["vip_type"] = int.TryParse(vipType, out var parsedVipType) ? parsedVipType : 0,
            ["vip_flags"] = 3,
            ["recommend_source_locked"] = 0,
            ["song_pool_id"] = songPoolId,
            ["callerid"] = 0,
            ["m_type"] = 1,
            ["kguid"] = userid,
            ["platform"] = "ios",
            ["area_code"] = 1,
            ["fakem"] = "ca981cfc583a4c37f28d2d49000013c16a0a",
            ["clientver"] = 11850,
            ["mode"] = mode,
            ["active_swtich"] = "on",
            ["key"] = KgSigner.CalcLoginKey(clientTime)
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/genesisapi/v1/newepoch_song_rec/feed",
            Params = new Dictionary<string, string>
            {
                ["sort_type"] = "1",
                ["platform"] = "ios",
                ["page"] = "1",
                ["content_ver"] = "4",
                ["clientver"] = "11850"
            },
            Body = new JsonObject
            {
                ["behaviors"] = new JsonArray(),
                ["abtest"] = new JsonObject
                {
                    ["abtest"] = new JsonObject
                    {
                        ["shuashua"] = new JsonObject { ["commentcard"] = 2 }
                    }
                },
                ["personal_recommend_params"] = personalRecommend
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetEverydayFriendAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://acsing.service.kugou.com",
            Path = "/sing7/relation/json/v3/friend_rec_by_using_song_list",
            Params = new Dictionary<string, string>
            {
                ["channel"] = "130",
                ["isteen"] = "0",
                ["platform"] = "2",
                ["usemkv"] = "1"
            },
            Body = new JsonObject
            {
                ["list"] = new JsonArray(new JsonObject
                {
                    ["user_id"] = 853927886,
                    ["mixsong_ids"] = new JsonArray(290083753, 251724346, 571554587, 250126644, 208831644,
                        40328518, 250504076, 581706850, 318347675, 585258401, 288481998, 407414475,
                        28239430, 280584633, 291957521, 64556644, 243149863, 488725103, 32114153,
                        39951172, 29019580, 40397606, 327507651, 32029382, 32218359, 340353127,
                        276448762, 177071956, 100031397, 249251602)
                })
            },
            CustomHeaders = new Dictionary<string, string> { ["pid"] = "126556797" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetEverydayHistoryAsync(string mode = "list", string platform = "ios",
        string? historyName = null, string? date = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["mode"] = mode,
            ["platform"] = platform
        };
        if (!string.IsNullOrWhiteSpace(historyName)) parameters["history_name"] = historyName;
        if (!string.IsNullOrWhiteSpace(date)) parameters["date"] = date;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/everyday/api/v1/get_history",
            Params = parameters,
            SpecificRouter = "everydayrec.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }
}
