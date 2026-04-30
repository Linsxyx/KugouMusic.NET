using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class ArtistClient(RawArtistApi rawApi, RawSearchApi rawSearchApi, KgSessionManager sessionManager)
{
    public async Task<SingerDetailResponse?> GetDetailAsync(string id)
    {
        var json = await rawSearchApi.GetSingerDetailAsync(id);
        return KgApiResponseParser.Parse<SingerDetailResponse>(
            json,
            AppJsonContext.Default.SingerDetailResponse
        );
    }

    public async Task<SingerAudioResponse?> GetAudiosAsync(string id, int page = 1, int pageSize = 30,
        string sort = "new")
    {
        var json = await rawSearchApi.GetSingerSongsAsync(sessionManager.Session.Dfid, id, page, pageSize, sort);
        return json.Deserialize(AppJsonContext.Default.SingerAudioResponse);
    }

    public Task<JsonElement> GetAlbumsAsync(string id, int page = 1, int pageSize = 30, string sort = "new")
    {
        return rawApi.GetAlbumsAsync(id, page, pageSize, sort);
    }

    public Task<JsonElement> GetDetailRawAsync(string id)
    {
        return rawApi.GetDetailAsync(id);
    }

    public Task<JsonElement> GetAudiosRawAsync(string id, int page = 1, int pageSize = 30, string sort = "new")
    {
        return rawApi.GetAudiosAsync(id, page, pageSize, sort);
    }
}
