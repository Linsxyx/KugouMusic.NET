using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawSongApi(IKgTransport transport, KgSessionManager sessionManager)
{
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
}
