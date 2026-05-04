using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;

namespace KuGou.Net.Protocol.Raw;

public class RawAlbumApi(IKgTransport transport)
{
    public Task<JsonElement> GetAlbumShopAsync()
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/zhuanjidata/v3/album_shop_v2/get_classify_data",
            SignatureType = SignatureType.Default
        });
    }

    public async Task<JsonElement> GetAlbumAsync(string albumIds, string? fields = null)
    {
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new JsonArray();
        foreach (var id in albumIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            data.Add(new JsonObject
            {
                ["album_id"] = id,
                ["album_name"] = "",
                ["author_name"] = ""
            });

        var body = new JsonObject
        {
            ["appid"] = "3116",
            ["clienttime"] = clientTime,
            ["clientver"] = "11440",
            ["data"] = data,
            ["dfid"] = "-",
            ["fields"] = fields ?? "",
            ["key"] = KuGou.Net.util.KgSigner.CalcLoginKey(clientTime),
            ["mid"] = "-"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "http://kmr.service.kugou.com",
            Path = "/v1/album",
            Body = body,
            SpecificRouter = "kmr.service.kugou.com",
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetAlbumInfoAsync(string albumId)
    {
        var body = new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject { ["album_id"] = albumId }),
            ["is_buy"] = 0,
            ["fields"] =
                "album_id,album_name,publish_date,sizable_cover,intro,language,is_publish,heat,type,quality,authors,exclusive,author_name,trans_param"
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/kmr/v2/albums",
            Body = body,
            SpecificRouter = "openapi.kugou.com",
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string>
            {
                { "kg-tid", "255" }
            }
        };

        return await transport.SendAsync(request);
    }

    public async Task<JsonElement> GetAlbumSongAsync(string albumId, int page, int pageSize)
    {
        var body = new JsonObject
        {
            ["album_id"] = albumId,
            ["is_buy"] = 0,
            ["page"] = page,
            ["pagesize"] = pageSize
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/v1/album_audio/lite",
            Body = body,
            SpecificRouter = "openapi.kugou.com",
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string>
            {
                { "kg-tid", "255" }
            }
        };

        return await transport.SendAsync(request);
    }
}
