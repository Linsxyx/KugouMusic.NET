using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawPlaylistApi(IKgTransport transport)
{
    /// <summary>
    ///     获取歌单内歌曲 (对应 /pubsongs/v2/get_other_list_file_nofilt)
    /// </summary>
    /// <param name="beginIdx">注意：这里传的是起始索引，不是页码</param>
    public async Task<JsonElement> GetPlaylistSongsAsync(string playlistId, int beginIdx, int pageSize)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "area_code", "1" },
            { "begin_idx", beginIdx.ToString() },
            { "plat", "1" },
            { "type", "1" },
            { "mode", "1" },
            { "personal_switch", "1" },
            { "extend_fields", "abtags,hot_cmt,popularization" },
            { "pagesize", pageSize.ToString() },
            { "global_collection_id", playlistId }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/pubsongs/v2/get_other_list_file_nofilt",
            Params = paramsDict,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     获取歌单详情信息 (对应 /v3/get_list_info)
    /// </summary>
    public async Task<JsonElement> GetPlaylistInfoAsync(string playlistId, string userid, string token)
    {
        var innerItem = new JsonObject
        {
            ["global_collection_id"] = playlistId
        };

        var dataArray = new JsonArray
        {
            innerItem
        };
        var body = new JsonObject
        {
            ["data"] = dataArray,
            ["userid"] = userid,
            ["token"] = token
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v3/get_list_info",
            Body = body,
            SpecificRouter = "pubsongs.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     收藏创建歌单
    /// </summary>
    /// <param name="listCreateUserId">原歌单创建者ID</param>
    /// <param name="listCreateListId">原歌单ID (数字)</param>
    /// <param name="listCreateGid">原歌单 Global ID</param>
    /// <param name="name">歌单名称</param>
    /// <param name="type">0: 创建 1：收藏</param>
    public async Task<JsonElement> CollectPlaylistAsync(
        string userid,
        string token,
        string listCreateUserId,
        string listCreateListId,
        string listCreateGid,
        string name,
        long? type)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 构建 Body
        var body = new JsonObject
        {
            ["userid"] = userid,
            ["token"] = token,
            ["total_ver"] = 0,
            ["name"] = name,
            ["type"] = type ?? 0,
            ["source"] = 1,
            ["is_pri"] = 0,
            ["list_create_userid"] = listCreateUserId,
            ["list_create_listid"] = listCreateListId,
            ["list_create_gid"] = listCreateGid ?? "",
            ["from_shupinmv"] = 0
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/cloudlist.service/v5/add_list",
            Params = new Dictionary<string, string>
            {
                { "last_time", clientTime.ToString() },
                { "last_area", "gztx" },
                { "userid", userid },
                { "token", token }
            },
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     取消收藏/删除歌单
    /// </summary>
    public async Task<JsonElement> DeletePlaylistAsync(string userid, string token, string listid)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var dataMap = new JsonObject
        {
            ["listid"] = long.Parse(listid),
            ["total_ver"] = 0,
            ["type"] = 1
        };
        var (aesStr, aesKey) = KgCrypto.PlaylistAesEncrypt(dataMap);
        var keyPayload = new JsonObject
        {
            ["aes"] = aesKey,
            ["uid"] = userid,
            ["token"] = token
        };
        var keyPayloadJson = JsonSerializer.Serialize(keyPayload, AppJsonContext.Default.JsonObject);
        var p = KgCrypto.RsaEncryptPkcs1(keyPayloadJson).ToUpper();

        var signKeyRaw = $"{KuGouConfig.AppId}{KuGouConfig.LiteSalt}{KuGouConfig.ClientVer}{clientTime}";
        var signKey = KgUtils.Md5(signKeyRaw);
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/delete_list",
            Params = new Dictionary<string, string>
            {
                { "clienttime", clientTime.ToString() },
                { "key", signKey },
                { "last_area", "gztx" },
                { "clientver", KuGouConfig.ClientVer },
                { "appid", KuGouConfig.AppId },
                { "last_time", clientTime.ToString() },
                { "p", p }
            },
            RawBody = aesStr,
            ContentType = "text/plain",
            SpecificRouter = "cloudlist.service.kugou.com",
            SignatureType = SignatureType.Default
        };
        var response = await transport.SendAsync(request);

        try
        {
            string? encryptedResponse = null;
            if (response.ValueKind == JsonValueKind.Object &&
                response.TryGetProperty("__raw_base64__", out var rawEl))
                encryptedResponse = rawEl.GetString();
            else if (response.ValueKind == JsonValueKind.Object &&
                     response.TryGetProperty("data", out var dataEl))
                encryptedResponse = dataEl.GetString();
            else if (response.ValueKind == JsonValueKind.String) encryptedResponse = response.GetString();

            if (!string.IsNullOrEmpty(encryptedResponse))
            {
                var decryptedJson = KgCrypto.PlaylistAesDecrypt(encryptedResponse, aesKey);

                using var doc = JsonDocument.Parse(decryptedJson);
                return doc.RootElement.Clone();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeletePlaylist] 解密响应失败: {ex.Message}");
        }

        return response;
    }

    /// <summary>
    ///     获取歌单分类
    /// </summary>
    public async Task<JsonElement> GetPlaylistTagsAsync()
    {
        var body = new JsonObject
        {
            ["tag_type"] = "collection",
            ["tag_id"] = 0,
            ["source"] = 3
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/pubsongs/v1/get_tags_by_type",
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     向歌单添加歌曲
    /// </summary>
    /// <param name="songs">歌曲列表，每项格式: { Name, Hash, AlbumId, MixSongId }</param>
    public async Task<JsonElement> AddSongsToPlaylistAsync(
        string userid,
        string token,
        string listid,
        List<(string Name, string Hash, string AlbumId, string MixSongId)> songs)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 构建 resources 数组
        var resourceArray = new JsonArray();
        foreach (var song in songs)
            resourceArray.Add(new JsonObject
            {
                ["number"] = 1,
                ["name"] = song.Name ?? "",
                ["hash"] = song.Hash ?? "",
                ["size"] = 0,
                ["sort"] = 0,
                ["timelen"] = 0,
                ["bitrate"] = 0,
                ["album_id"] = long.TryParse(song.AlbumId, out var aid) ? aid : 0,
                ["mixsongid"] = long.TryParse(song.MixSongId, out var mid) ? mid : 0
            });

        var body = new JsonObject
        {
            ["userid"] = userid,
            ["token"] = token,
            ["listid"] = listid,
            ["list_ver"] = 0,
            ["type"] = 0,
            ["slow_upload"] = 1,
            ["scene"] = "false;null",
            ["data"] = resourceArray
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/cloudlist.service/v6/add_song",
            Params = new Dictionary<string, string>
            {
                { "last_time", clientTime.ToString() },
                { "last_area", "gztx" },
                { "userid", userid },
                { "token", token }
            },
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     从歌单删除歌曲 (对应 /v4/delete_songs)
    /// </summary>
    /// <param name="fileIds">要删除的 FileId 列表 (注意不是 Hash，是歌单内的 fileid)</param>
    public async Task<JsonElement> RemoveSongsFromPlaylistAsync(
        string userid,
        string token,
        string listid,
        IEnumerable<long> fileIds)
    {
        // 构建 data 数组: [{fileid: 123}, {fileid: 456}]
        var resourceArray = new JsonArray();
        foreach (var fid in fileIds)
            resourceArray.Add(new JsonObject
            {
                ["fileid"] = fid
            });

        var body = new JsonObject
        {
            ["listid"] = listid,
            ["userid"] = userid,
            ["data"] = resourceArray,
            ["type"] = 0,
            ["token"] = token,
            ["list_ver"] = 0
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v4/delete_songs",
            Body = body,
            SpecificRouter = "cloudlist.service.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }
}