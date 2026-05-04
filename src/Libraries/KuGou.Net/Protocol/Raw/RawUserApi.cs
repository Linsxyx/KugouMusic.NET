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
                { "latest_limit", "100" }
            },
            SignatureType = SignatureType.Default
        };
        return await transport.SendAsync(request);
    }

    public Task<JsonElement> GetYouthChannelAllAsync(int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v2/channel/channel_all_list",
            Params = new Dictionary<string, string>
            {
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString(),
                ["type"] = "1"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthChannelAmwayAsync(string globalCollectionId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/api/amway/v2/index",
            Params = new Dictionary<string, string>
            {
                ["global_collection_id"] = globalCollectionId
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthChannelDetailAsync(string globalCollectionIds)
    {
        var data = new JsonArray();
        foreach (var id in globalCollectionIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject { ["global_collection_id"] = id });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/api/channel/v1/channel_list_by_id",
            Body = new JsonObject { ["data"] = data },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthChannelSimilarAsync(string channelId, string vipType)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v1/channel/get_friendly_channel",
            Params = new Dictionary<string, string>
            {
                ["channel_id"] = channelId
            },
            Body = new JsonObject
            {
                ["area_code"] = 1,
                ["playlist_ver"] = 2,
                ["vip_type"] = int.TryParse(vipType, out var parsedVipType) ? parsedVipType : 0,
                ["platform"] = "ios"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthChannelSongsAsync(string globalCollectionId, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/api/channel/v1/channel_get_song_audit_passed",
            Params = new Dictionary<string, string>
            {
                ["global_collection_id"] = globalCollectionId,
                ["pagesize"] = pageSize.ToString(),
                ["page"] = page.ToString(),
                ["is_filter"] = "0"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthChannelSongDetailAsync(string globalCollectionId, string fileId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v2/post/get_song_detail",
            Params = new Dictionary<string, string>
            {
                ["global_collection_id"] = globalCollectionId,
                ["fileid"] = fileId
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> SetYouthChannelSubscriptionAsync(string globalCollectionId, bool subscribe)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = subscribe ? HttpMethod.Post : HttpMethod.Delete,
            Path = subscribe ? "/youth/v1/channel_subscribe" : "/youth/v1/channel_unsubscribe",
            Params = new Dictionary<string, string>
            {
                ["global_collection_id"] = globalCollectionId,
                ["source"] = "1"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthDynamicAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v3/user/get_dynamic",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthRecentDynamicAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v3/user/recent_dynamic",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> ReportYouthListenSongAsync(long mixSongId = 666075191)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v2/report/listen_song",
            Params = new Dictionary<string, string>
            {
                ["clientver"] = "10566"
            },
            Body = new JsonObject
            {
                ["mixsongid"] = mixSongId
            },
            CustomHeaders = new Dictionary<string, string>
            {
                ["user-agent"] = "Android13-1070-10566-201-0-ReportPlaySongToServerProtocol-wifi"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthUnionVipAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://kugouvip.kugou.com",
            Path = "/v1/get_union_vip",
            Params = new Dictionary<string, string>
            {
                ["busi_type"] = "concept",
                ["opt_product_types"] = "dvip,qvip",
                ["product_type"] = "svip"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetYouthUserSongsAsync(string userid, int page = 1, int pageSize = 30, int type = 0)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/youth/v1/get_user_song_public",
            Params = new Dictionary<string, string>
            {
                ["filter_video"] = "0",
                ["type"] = type.ToString(),
                ["userid"] = userid,
                ["pagesize"] = pageSize.ToString(),
                ["page"] = page.ToString(),
                ["is_filter"] = "0"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> ReportYouthVipAdPlayAsync()
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/youth/v1/ad/play_report",
            Body = new JsonObject
            {
                ["ad_id"] = 12307537187,
                ["play_end"] = time,
                ["play_start"] = time - 30000
            },
            SignatureType = SignatureType.Default
        });
    }

    public async Task<JsonElement> GetCloudAsync(string userid, string token, string mid, int page = 1,
        int pageSize = 30)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = new JsonObject
        {
            ["page"] = page,
            ["pagesize"] = pageSize,
            ["getkmr"] = 1
        };

        var (aesStr, aesKey) = KgCrypto.PlaylistAesEncrypt(body);
        var pPayload = new JsonObject
        {
            ["aes"] = aesKey,
            ["uid"] = userid,
            ["token"] = token
        };
        var p = KgCrypto.RsaEncryptPkcs1(JsonSerializer.Serialize(pPayload, AppJsonContext.Default.JsonObject))
            .ToUpper();

        var response = await transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://mcloudservice.kugou.com",
            Path = "/v1/get_list",
            Params = new Dictionary<string, string>
            {
                ["clienttime"] = clientTime.ToString(),
                ["mid"] = mid,
                ["key"] = KgSigner.CalcLoginKey(clientTime),
                ["clientver"] = KuGouConfig.ClientVer,
                ["appid"] = KuGouConfig.AppId,
                ["p"] = p
            },
            BinaryBody = Convert.FromBase64String(aesStr),
            ContentType = "application/octet-stream",
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });

        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("__raw_base64__", out var rawEl) &&
            !string.IsNullOrWhiteSpace(rawEl.GetString()))
        {
            var decrypted = KgCrypto.PlaylistAesDecrypt(rawEl.GetString()!, aesKey);
            using var doc = JsonDocument.Parse(decrypted);
            return doc.RootElement.Clone();
        }

        return response;
    }

    public Task<JsonElement> GetCloudUrlAsync(string hash, string? albumAudioId = null, string? audioId = null,
        string? name = null)
    {
        var normalizedHash = hash.ToLowerInvariant();
        const int pid = 20026;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/bsstrackercdngz/v2/query_musicclound_url",
            Params = new Dictionary<string, string>
            {
                ["hash"] = normalizedHash,
                ["ssa_flag"] = "is_fromtrack",
                ["version"] = "20102",
                ["ssl"] = "0",
                ["album_audio_id"] = albumAudioId ?? "0",
                ["pid"] = pid.ToString(),
                ["audio_id"] = audioId ?? "0",
                ["kv_id"] = "2",
                ["key"] = KgSigner.CalcCloudKey(normalizedHash, pid),
                ["bucket"] = "musicclound",
                ["name"] = name ?? "",
                ["with_res_tag"] = "0"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetFollowMessagesAsync(string userid, string artistId, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/msg.mobile/v3/msgtag/history",
            Params = new Dictionary<string, string>
            {
                ["filter"] = "1",
                ["maxid"] = "0",
                ["pagesize"] = pageSize.ToString(),
                ["tag"] = $"chat:{userid}_{artistId}"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetCollectedVideosAsync(string userid, string token, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/collectservice/v2/collect_list_mixvideo",
            Params = new Dictionary<string, string> { ["plat"] = "1" },
            Body = new JsonObject
            {
                ["userid"] = userid,
                ["token"] = token,
                ["page"] = page,
                ["pagesize"] = pageSize
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLikedVideosAsync(string userid, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/m.comment.service/v1/get_user_like_video",
            Params = new Dictionary<string, string>
            {
                ["kugouid"] = userid,
                ["pagesize"] = pageSize.ToString(),
                ["load_video_info"] = "1",
                ["p"] = "1",
                ["plat"] = "1"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetFavoriteCountAsync(string mixSongIds)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/count/v1/audio/mget_collect",
            Params = new Dictionary<string, string>
            {
                ["mixsongids"] = mixSongIds
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetServerNowAsync(string userid, string token)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/server_now",
            Params = new Dictionary<string, string>
            {
                ["plat"] = "3"
            },
            Body = new JsonObject
            {
                ["token"] = token,
                ["userid"] = userid
            },
            SpecificRouter = "usercenter.kugou.com",
            SignatureType = SignatureType.Default
        });
    }
}
