using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class FmClient(RawFmApi rawApi)
{
    public async Task<FmRecommendResponse?> GetRecommendAsync()
    {
        var json = await rawApi.GetRecommendAsync();
        return json.Deserialize(AppJsonContext.Default.FmRecommendResponse);
    }

    public async Task<FmSongResponse?> GetSongsAsync(string fmIds, int type = 2, int offset = -1, int size = 20)
    {
        var json = await rawApi.GetSongsAsync(fmIds, type, offset, size);
        return json.Deserialize(AppJsonContext.Default.FmSongResponse);
    }

    public Task<JsonElement> GetClassSongAsync()
    {
        return rawApi.GetClassSongAsync();
    }

    public async Task<FmImageResponse?> GetImagesAsync(string fmIds)
    {
        var json = await rawApi.GetImagesAsync(fmIds);
        return json.Deserialize(AppJsonContext.Default.FmImageResponse);
    }
}
