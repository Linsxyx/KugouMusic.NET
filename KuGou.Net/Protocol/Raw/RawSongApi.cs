using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawSongApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetAudioAsync(string hash)
    {
        var session = sessionManager.Session;
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = SplitComma(hash, s => new JsonObject
        {
            ["hash"] = s,
            ["audio_id"] = 0
        });

        var body = new JsonObject
        {
            ["appid"] = KuGouConfig.AppId,
            ["clienttime"] = clientTime,
            ["clientver"] = KuGouConfig.ClientVer,
            ["data"] = data,
            ["dfid"] = string.IsNullOrWhiteSpace(session.Dfid) ? "-" : session.Dfid,
            ["key"] = KgSigner.CalcLoginKey(clientTime),
            ["mid"] = string.IsNullOrWhiteSpace(session.Mid) ? KgUtils.CalcNewMid(session.Dfid) : session.Mid
        };

        if (!string.IsNullOrWhiteSpace(session.Token)) body["token"] = session.Token;
        if (!string.IsNullOrWhiteSpace(session.UserId) && session.UserId != "0") body["userid"] = session.UserId;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "http://kmr.service.kugou.com",
            Path = "/v1/audio/audio",
            Body = body,
            SpecificRouter = "kmr.service.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAudioRelatedAsync(
        long albumAudioId,
        int page = 1,
        int pageSize = 30,
        string sort = "all",
        int type = 0,
        int showType = 0,
        bool showDetail = true)
    {
        var parameters = new Dictionary<string, string>
        {
            ["album_audio_id"] = albumAudioId.ToString(),
            ["appid"] = "1005",
            ["area_code"] = "1",
            ["clientver"] = "12329"
        };

        if (!showDetail)
        {
            parameters["page"] = page.ToString();
            parameters["pagesize"] = pageSize.ToString();
            parameters["show_input"] = "1";
            parameters["show_type"] = showType.ToString();
            parameters["sort"] = (sort switch
            {
                "hot" => 2,
                "new" => 3,
                _ => 1
            }).ToString();
            parameters["type"] = type.ToString();
        }

        parameters["version"] = "1";
        parameters["signature"] = CalcJoinedSignature(parameters, "OIlwieks28dk2k092lksi2UIkp", string.Empty);

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://listkmrp3cdnretry.kugou.com",
            Path = showDetail ? "/v2/audio_related/total" : "/v3/album_audio/related",
            Params = parameters,
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAudioAccompanyMatchingAsync(
        string hash,
        long mixId = 0,
        string? fileName = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["isteen"] = "0",
            ["mixId"] = mixId.ToString(),
            ["usemkv"] = "1",
            ["platform"] = "2",
            ["fileName"] = fileName ?? string.Empty,
            ["hash"] = hash,
            ["version"] = "12375",
            ["appid"] = KuGouConfig.AppId
        };
        parameters["sign"] = KgUtils.Md5($"{BuildSortedParamString(parameters, "&")}*s&iN#G70*")[8..24];

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://nsongacsing.kugou.com",
            Path = "/sing7/accompanywan/json/v2/cdn/optimal_matching_accompany_2_listen.do",
            Params = parameters,
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAudioKtvTotalAsync(long songId, string songHash, string singerName)
    {
        var parameters = new Dictionary<string, string>
        {
            ["isteen"] = "0",
            ["songId"] = songId.ToString(),
            ["usemkv"] = "1",
            ["platform"] = "2",
            ["singerName"] = singerName,
            ["songHash"] = songHash,
            ["version"] = "12375",
            ["appid"] = KuGouConfig.AppId
        };
        parameters["sign"] = KgUtils.Md5($"{BuildSortedParamString(parameters, "&")}*s&iN#G70*")[8..24];

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://acsing.service.kugou.com",
            Path = "/sing7/listenguide/json/v2/cdn/listenguide/get_total_opus_num_v02.do",
            Params = parameters,
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetKmrAudioMvAsync(string albumAudioIds, string? fields = null)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v1/audio/mv",
            Body = new JsonObject
            {
                ["data"] = SplitComma(albumAudioIds, s => new JsonObject { ["album_audio_id"] = s }),
                ["fields"] = fields ?? string.Empty
            },
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["KG-TID"] = "38" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetKmrAudioAsync(string albumAudioIds, string? fields = "base")
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v2/audio",
            Body = new JsonObject
            {
                ["data"] = SplitComma(albumAudioIds, s => new JsonObject
                {
                    ["entity_id"] = long.TryParse(s, out var id) ? id : 0
                }),
                ["fields"] = fields ?? "base"
            },
            SpecificRouter = "openapi.kugou.com",
            CustomHeaders = new Dictionary<string, string> { ["KG-TID"] = "238" },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSongClimaxAsync(string hash)
    {
        var data = SplitComma(hash, s => new JsonObject { ["hash"] = s });

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://expendablekmrcdn.kugou.com",
            Path = "/v1/audio_climax/audio",
            Params = new Dictionary<string, string>
            {
                ["data"] = data.ToJsonString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSongRankingAsync(string albumAudioId)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/grow/v1/song_ranking/play_page/ranking_info",
            Params = new Dictionary<string, string> { ["album_audio_id"] = albumAudioId },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetSongRankingFilterAsync(string albumAudioId, int page = 1, int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/grow/v1/song_ranking/unlock/v2/ranking_filter",
            Params = new Dictionary<string, string>
            {
                ["album_audio_id"] = albumAudioId,
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString()
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetPrivilegeLiteAsync(string hash, string? albumIds = null)
    {
        var hashes = hash.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var albums = (albumIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resource = new JsonArray();

        for (var i = 0; i < hashes.Length; i++)
        {
            resource.Add(new JsonObject
            {
                ["type"] = "audio",
                ["page_id"] = 0,
                ["hash"] = hashes[i],
                ["album_id"] = i < albums.Length ? albums[i] : "0"
            });
        }

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v2/get_res_privilege/lite",
            Body = new JsonObject
            {
                ["appid"] = KuGouConfig.AppId,
                ["area_code"] = 1,
                ["behavior"] = "play",
                ["clientver"] = KuGouConfig.ClientVer,
                ["need_hash_offset"] = 1,
                ["relate"] = 1,
                ["support_verify"] = 1,
                ["resource"] = resource,
                ["qualities"] = new JsonArray("128", "320", "flac", "high", "viper_atmos", "viper_tape",
                    "viper_clear", "super", "multitrack")
            },
            SpecificRouter = "media.store.kugou.com",
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetImagesAsync(string hash, string? albumIds = null, string? albumAudioIds = null,
        int count = 5)
    {
        var data = BuildImageResource(hash, albumIds, albumAudioIds);
        var parameters = new Dictionary<string, string>
        {
            ["album_image_type"] = "-3",
            ["appid"] = KuGouConfig.AppId,
            ["clientver"] = KuGouConfig.ClientVer,
            ["author_image_type"] = "3,4,5",
            ["count"] = count.ToString(),
            ["data"] = data.ToJsonString(),
            ["isCdn"] = "1",
            ["publish_time"] = "1"
        };
        parameters["signature"] = CalcAndroidRawSignature(parameters);

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://expendablekmr.kugou.com",
            Path = "/container/v2/image",
            Params = parameters,
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetAudioImagesAsync(string hash, string? audioIds = null, string? albumAudioIds = null,
        string? fileNames = null, int count = 5)
    {
        var data = BuildAudioImageResource(hash, audioIds, albumAudioIds, fileNames);
        var parameters = new Dictionary<string, string>
        {
            ["appid"] = KuGouConfig.AppId,
            ["clientver"] = KuGouConfig.ClientVer,
            ["count"] = count.ToString(),
            ["data"] = data.ToJsonString(),
            ["isCdn"] = "1",
            ["publish_time"] = "1",
            ["show_authors"] = "1"
        };
        parameters["signature"] = CalcAndroidRawSignature(parameters);

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = "https://expendablekmr.kugou.com",
            Path = "/v2/author_image/audio",
            Params = parameters,
            ClearDefaultParams = true,
            NotSignature = true,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetUrlNewAsync(string hash, string? albumAudioId = null, bool freePart = false)
    {
        var session = sessionManager.Session;
        var clientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dfid = string.IsNullOrWhiteSpace(session.Dfid) || session.Dfid == "-" ? KgUtils.RandomString(24) : session.Dfid;
        var mid = KgUtils.CalcNewMid(dfid);
        var userId = string.IsNullOrWhiteSpace(session.UserId) ? "0" : session.UserId;

        var body = new JsonObject
        {
            ["area_code"] = "1",
            ["behavior"] = "play",
            ["qualities"] = new JsonArray("128", "320", "flac", "high", "multitrack", "viper_atmos", "viper_tape",
                "viper_clear", "super"),
            ["resource"] = new JsonObject
            {
                ["album_audio_id"] = albumAudioId ?? "",
                ["collect_list_id"] = "3",
                ["collect_time"] = clientTimeMs,
                ["hash"] = hash,
                ["id"] = 0,
                ["page_id"] = 1,
                ["type"] = "audio"
            },
            ["token"] = session.Token,
            ["tracker_param"] = new JsonObject
            {
                ["all_m"] = 1,
                ["auth"] = "",
                ["is_free_part"] = freePart ? 1 : 0,
                ["key"] = KgUtils.Md5($"{hash}{KuGouConfig.V5KeySalt}{KuGouConfig.AppId}{mid}{userId}"),
                ["module_id"] = 0,
                ["need_climax"] = 1,
                ["need_xcdn"] = 1,
                ["open_time"] = "",
                ["pid"] = "411",
                ["pidversion"] = "3001",
                ["priv_vip_type"] = "6",
                ["viptoken"] = session.VipToken
            },
            ["userid"] = userId,
            ["vip"] = session.VipType
        };

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "http://tracker.kugou.com",
            Path = "/v6/priv_url",
            Body = body,
            SignatureType = SignatureType.Default,
            SessionOverrides = new Dictionary<string, string> { ["dfid"] = dfid }
        });
    }

    public Task<JsonElement> GetUrlAsync(string hash, string? quality = "128", string? albumId = null,
        string? albumAudioId = null, bool freePart = false)
    {
        var normalizedQuality = quality is "piano" or "acappella" or "subwoofer" or "ancient" or "dj" or "surnay"
            ? $"magic_{quality}"
            : quality ?? "128";

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v5/url",
            Params = new Dictionary<string, string>
            {
                ["album_id"] = albumId ?? "0",
                ["area_code"] = "1",
                ["hash"] = hash.ToLowerInvariant(),
                ["ssa_flag"] = "is_fromtrack",
                ["version"] = "11430",
                ["page_id"] = "967177915",
                ["quality"] = normalizedQuality,
                ["album_audio_id"] = albumAudioId ?? "0",
                ["behavior"] = "play",
                ["pid"] = "411",
                ["cmd"] = "26",
                ["pidversion"] = "3001",
                ["IsFreePart"] = freePart ? "1" : "0",
                ["ppage_id"] = "356753938,823673182,967485191",
                ["cdnBackup"] = "1",
                ["module"] = "",
                ["clientver"] = "11430"
            },
            SpecificRouter = "trackercdn.kugou.com",
            SignatureType = SignatureType.V5,
            SpecificDfid = KgUtils.RandomString(24)
        });
    }

    public Task<JsonElement> GetVideoUrlAsync(string hash)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/v2/interface/index",
            Params = new Dictionary<string, string>
            {
                ["backupdomain"] = "1",
                ["cmd"] = "123",
                ["ext"] = "mp4",
                ["ismp3"] = "0",
                ["hash"] = hash,
                ["pid"] = "1",
                ["type"] = "1"
            },
            SpecificRouter = "trackermv.kugou.com",
            SignatureType = SignatureType.V5
        });
    }

    private static JsonArray SplitComma(string value, Func<string, JsonObject> factory)
    {
        var array = new JsonArray();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            array.Add(factory(item));
        return array;
    }

    private static string CalcJoinedSignature(Dictionary<string, string> parameters, string salt, string separator)
    {
        return KgUtils.Md5($"{salt}{BuildSortedParamString(parameters, separator)}{salt}");
    }

    private static string BuildSortedParamString(Dictionary<string, string> parameters, string separator)
    {
        return string.Join(separator, parameters
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));
    }

    private static string CalcAndroidRawSignature(Dictionary<string, string> parameters)
    {
        return KgUtils.Md5($"{KuGouConfig.LiteSalt}{BuildSortedParamString(parameters, string.Empty)}{KuGouConfig.LiteSalt}");
    }

    private static JsonArray BuildImageResource(string hash, string? albumIds, string? albumAudioIds)
    {
        var hashes = SplitValues(hash);
        var albums = SplitValues(albumIds);
        var albumAudios = SplitValues(albumAudioIds);
        var data = new JsonArray();

        for (var i = 0; i < hashes.Length; i++)
            data.Add(new JsonObject
            {
                ["album_id"] = i < albums.Length ? albums[i] : "0",
                ["hash"] = hashes[i],
                ["album_audio_id"] = i < albumAudios.Length ? albumAudios[i] : "0"
            });

        return data;
    }

    private static JsonArray BuildAudioImageResource(string hash, string? audioIds, string? albumAudioIds,
        string? fileNames)
    {
        var hashes = SplitValues(hash);
        var audios = SplitValues(audioIds);
        var albumAudios = SplitValues(albumAudioIds);
        var names = SplitValues(fileNames);
        var data = new JsonArray();

        for (var i = 0; i < hashes.Length; i++)
            data.Add(new JsonObject
            {
                ["audio_id"] = i < audios.Length ? audios[i] : "0",
                ["hash"] = hashes[i],
                ["album_audio_id"] = i < albumAudios.Length ? albumAudios[i] : "0",
                ["filename"] = i < names.Length ? names[i] : string.Empty
            });

        return data;
    }

    private static string[] SplitValues(string? value)
    {
        return (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
