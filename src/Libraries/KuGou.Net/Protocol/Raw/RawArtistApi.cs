using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawArtistApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> FollowAsync(string id, string userid, string token)
    {
        var singerId = long.Parse(id);
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var encryptData = new JsonObject
        {
            ["singerid"] = singerId,
            ["token"] = token
        };
        var (encryptedParams, aesKey) = KgCrypto.AesEncrypt(
            JsonSerializer.Serialize(encryptData, AppJsonContext.Default.JsonObject));
        var pPayload = new JsonObject
        {
            ["clienttime"] = clientTime,
            ["key"] = aesKey
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/followservice/v3/follow_singer",
            Body = new JsonObject
            {
                ["plat"] = 0,
                ["userid"] = long.TryParse(userid, out var parsedUserId) ? parsedUserId : 0,
                ["singerid"] = singerId,
                ["source"] = 7,
                ["p"] = KgCrypto.RsaEncryptPkcs1(
                    JsonSerializer.Serialize(pPayload, AppJsonContext.Default.JsonObject)),
                ["params"] = encryptedParams
            },
            Params = new Dictionary<string, string>
            {
                ["clienttime"] = clientTime.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> UnfollowAsync(string id, string userid, string token)
    {
        var encryptData = new JsonObject
        {
            ["singerid"] = id,
            ["token"] = token
        };
        var (encryptedParams, aesKey) = KgCrypto.AesEncrypt(
            JsonSerializer.Serialize(encryptData, AppJsonContext.Default.JsonObject));
        var pPayload = new JsonObject
        {
            ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["key"] = aesKey
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/followservice/v3/unfollow_singer",
            Body = new JsonObject
            {
                ["plat"] = 0,
                ["userid"] = userid,
                ["singerid"] = id,
                ["source"] = 7,
                ["p"] = KgCrypto.RsaEncryptPkcs1(
                    JsonSerializer.Serialize(pPayload, AppJsonContext.Default.JsonObject)),
                ["params"] = encryptedParams
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetFollowNewSongsAsync(long lastAlbumId = 0, int pageSize = 30, int optSort = 1)
    {
        var sort = optSort == 2 ? 2 : 1;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/feed/v1/follow/newsong_album_list",
            Body = new JsonObject { ["last_album_id"] = lastAlbumId },
            Params = new Dictionary<string, string>
            {
                ["last_album_id"] = lastAlbumId.ToString(),
                ["page_size"] = pageSize.ToString(),
                ["opt_sort"] = sort.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetHonourAsync(string id, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "http://h5activity.kugou.com",
            Path = "/v1/query_singer_honour_detail",
            Params = new Dictionary<string, string>
            {
                ["singer_id"] = id,
                ["pagesize"] = pageSize.ToString(),
                ["page"] = page.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetListsAsync(int musician = 0, int sexType = 0, int type = 0, int hotSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/ocean/v6/singer/list",
            Params = new Dictionary<string, string>
            {
                ["musician"] = musician.ToString(),
                ["sextype"] = sexType.ToString(),
                ["showtype"] = "2",
                ["type"] = type.ToString(),
                ["hotsize"] = hotSize.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetVideosAsync(string id, int page = 1, int pageSize = 30, string tag = "all")
    {
        var tagIdx = tag switch
        {
            "official" => "18",
            "live" => "20",
            "fan" => "23",
            "artist" => "42419",
            _ => string.Empty
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://openapicdn.kugou.com",
            Path = "/kmr/v1/author/videos",
            Params = new Dictionary<string, string>
            {
                ["author_id"] = id,
                ["is_fanmade"] = string.Empty,
                ["tag_idx"] = tagIdx,
                ["pagesize"] = pageSize.ToString(),
                ["page"] = page.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

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
