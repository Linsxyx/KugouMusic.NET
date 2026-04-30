using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawMediaCatalogApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetVideoDetailAsync(string ids)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var session = sessionManager.Session;
        var data = new JsonArray();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject { ["video_id"] = id });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/video",
            Body = new JsonObject
            {
                ["appid"] = KuGouConfig.AppId,
                ["clientver"] = KuGouConfig.ClientVer,
                ["clienttime"] = now,
                ["mid"] = KgUtils.CalcNewMid(session.Dfid),
                ["uuid"] = KgUtils.Md5($"{session.Dfid}{KgUtils.CalcNewMid(session.Dfid)}"),
                ["dfid"] = session.Dfid,
                ["token"] = session.Token,
                ["key"] = KgSigner.CalcLoginKey(now),
                ["show_resolution"] = 1,
                ["data"] = data
            },
            ClearDefaultParams = true,
            SpecificRouter = "kmr.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioAlbumDetailAsync(string albumIds)
    {
        var data = new JsonArray();
        foreach (var id in albumIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject { ["album_id"] = id });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/openapi/v2/broadcast",
            Body = new JsonObject
            {
                ["data"] = data,
                ["show_album_tag"] = 1,
                ["fields"] =
                    "album_name,album_id,category,authors,sizable_cover,intro,author_name,trans_param,album_tag,mix_intro,full_intro,is_publish"
            },
            CustomHeaders = new Dictionary<string, string> { ["kg-tid"] = "78" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioAlbumAudiosAsync(string albumId, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/longaudio/v2/album_audios",
            Body = new JsonObject
            {
                ["album_id"] = albumId,
                ["area_code"] = 1,
                ["tagid"] = 0,
                ["page"] = page,
                ["pagesize"] = pageSize
            },
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["kg-tid"] = "78" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioDailyRecommendAsync(int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/longaudio/v1/home_new/daily_recommend",
            Params = new Dictionary<string, string>
            {
                ["module_id"] = "1",
                ["size"] = pageSize.ToString(),
                ["page"] = page.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioRankRecommendAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/longaudio/v1/home_new/rank_card_recommend",
            Params = new Dictionary<string, string> { ["platform"] = "ios" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioVipRecommendAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/longaudio/v1/home_new/vip_select_recommend",
            Params = new Dictionary<string, string>
            {
                ["position"] = "2",
                ["clientver"] = "12329"
            },
            Body = new JsonObject { ["album_playlist"] = new JsonArray() },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetLongAudioWeekRecommendAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/longaudio/v1/home_new/week_new_albums_recommend",
            Params = new Dictionary<string, string> { ["clientver"] = "12329" },
            Body = new JsonObject { ["album_playlist"] = new JsonArray() },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetIpResourcesAsync(string id, string type = "audios", int page = 1, int pageSize = 30)
    {
        var normalizedType = type is "audios" or "albums" or "videos" or "author_list" ? type : "audios";
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = $"/openapi/v1/ip/{normalizedType}",
            Body = new JsonObject
            {
                ["is_publish"] = 1,
                ["ip_id"] = id,
                ["sort"] = 3,
                ["page"] = page,
                ["pagesize"] = pageSize,
                ["query"] = 1
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetIpDetailAsync(string ids)
    {
        var data = new JsonArray();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject { ["ip_id"] = id });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/openapi/v1/ip",
            Body = new JsonObject
            {
                ["data"] = data,
                ["is_publish"] = 1
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetIpPlaylistsAsync(string id, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/ocean/v6/pubsongs/list_info_for_ip",
            Params = new Dictionary<string, string>
            {
                ["ip"] = id,
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetIpZoneAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v1/zone/index",
            SpecificRouter = "yuekucategory.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetIpZoneHomeAsync(string id)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v1/zone/home",
            Params = new Dictionary<string, string>
            {
                ["id"] = id,
                ["share"] = "0"
            },
            SpecificRouter = "yuekucategory.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneListsAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/scene/v1/scene/list",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneAudiosAsync(string sceneId, string? moduleId = null, string? tag = null,
        int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/scene/v1/scene/audio_list",
            Params = new Dictionary<string, string>
            {
                ["scene_id"] = sceneId,
                ["module_id"] = moduleId ?? "",
                ["tag"] = tag ?? "",
                ["page"] = page.ToString(),
                ["page_size"] = pageSize.ToString()
            },
            Body = new JsonObject
            {
                ["appid"] = KuGouConfig.AppId,
                ["clientver"] = KuGouConfig.ClientVer,
                ["token"] = sessionManager.Session.Token,
                ["userid"] = sessionManager.Session.UserId
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneCollectionsAsync(string tagId, int page = 1, int pageSize = 30)
    {
        var session = sessionManager.Session;
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/scene/v1/distribution/collection_list",
            Body = new JsonObject
            {
                ["appid"] = KuGouConfig.AppId,
                ["clientver"] = KuGouConfig.ClientVer,
                ["token"] = session.Token,
                ["userid"] = session.UserId,
                ["tag_id"] = tagId,
                ["page"] = page,
                ["page_size"] = pageSize,
                ["exposed_data"] = new JsonArray()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneListsV2Async(string sceneId, int page = 1, int pageSize = 30,
        string sort = "rec")
    {
        var sortType = sort switch
        {
            "hot" => "2",
            "new" => "3",
            _ => "1"
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/scene/v1/scene/list_v2",
            Params = new Dictionary<string, string>
            {
                ["scene_id"] = sceneId,
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString(),
                ["sort_type"] = sortType,
                ["kugouid"] = sessionManager.Session.UserId
            },
            Body = new JsonObject { ["exposure"] = new JsonArray() },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneModulesAsync(string sceneId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/scene/v1/scene/module",
            Params = new Dictionary<string, string> { ["scene_id"] = sceneId },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneModuleInfoAsync(string sceneId, string moduleId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/scene/v1/scene/module_info",
            Params = new Dictionary<string, string>
            {
                ["scene_id"] = sceneId,
                ["module_id"] = moduleId
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneMusicAsync(string sceneId, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/genesisapi/v1/scene_music/rec_music",
            Params = new Dictionary<string, string>
            {
                ["scene_id"] = sceneId,
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString()
            },
            Body = new JsonObject { ["exposure"] = new JsonArray() },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSceneVideosAsync(string tagId, int page = 1, int pageSize = 30)
    {
        var session = sessionManager.Session;
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/scene/v1/distribution/video_list",
            Body = new JsonObject
            {
                ["appid"] = KuGouConfig.AppId,
                ["clientver"] = KuGouConfig.ClientVer,
                ["token"] = session.Token,
                ["userid"] = session.UserId,
                ["tag_id"] = tagId,
                ["page"] = page,
                ["page_size"] = pageSize,
                ["exposed_data"] = new JsonArray()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetThemeMusicAsync(string ids)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/everydayrec.service/v1/mul_theme_category_recommend",
            Body = new JsonObject
            {
                ["platform"] = "android",
                ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["show_theme_category_ids"] = ids,
                ["userid"] = sessionManager.Session.UserId,
                ["module_id"] = 508
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetThemePlaylistsAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/getthemelist",
            Body = new JsonObject
            {
                ["platform"] = "android",
                ["clientver"] = KuGouConfig.ClientVer,
                ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["area_code"] = 1,
                ["module_id"] = 1,
                ["userid"] = sessionManager.Session.UserId
            },
            SpecificRouter = "everydayrec.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetThemeMusicDetailAsync(string id)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/everydayrec.service/v1/theme_category_recommend",
            Body = new JsonObject
            {
                ["platform"] = "android",
                ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["theme_category_id"] = id,
                ["show_theme_category_id"] = 0,
                ["userid"] = sessionManager.Session.UserId,
                ["module_id"] = 508
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetThemePlaylistTracksAsync(string themeId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/gettheme_songidlist",
            Body = new JsonObject
            {
                ["platform"] = "android",
                ["clientver"] = KuGouConfig.ClientVer,
                ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["area_code"] = 1,
                ["module_id"] = 1,
                ["userid"] = sessionManager.Session.UserId,
                ["theme_id"] = themeId
            },
            SpecificRouter = "everydayrec.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }
}
