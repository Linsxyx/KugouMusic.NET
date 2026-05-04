using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class FmClient(RawFmApi rawApi)
{
    public Task<JsonElement> GetRecommendAsync()
    {
        return rawApi.GetRecommendAsync();
    }

    public Task<JsonElement> GetSongsAsync(string fmIds, int type = 2, int offset = -1, int size = 20)
    {
        return rawApi.GetSongsAsync(fmIds, type, offset, size);
    }

    public Task<JsonElement> GetClassSongAsync()
    {
        return rawApi.GetClassSongAsync();
    }

    public Task<JsonElement> GetImagesAsync(string fmIds)
    {
        return rawApi.GetImagesAsync(fmIds);
    }
}
