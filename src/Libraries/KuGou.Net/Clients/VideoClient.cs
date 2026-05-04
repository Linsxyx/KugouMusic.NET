using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class VideoClient(RawMediaCatalogApi rawApi, RawSongApi rawSongApi)
{
    public Task<JsonElement> GetDetailAsync(string ids)
    {
        return rawApi.GetVideoDetailAsync(ids);
    }

    public Task<JsonElement> GetUrlAsync(string hash)
    {
        return rawSongApi.GetVideoUrlAsync(hash);
    }
}
