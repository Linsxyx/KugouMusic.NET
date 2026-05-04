using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class LongAudioClient(RawMediaCatalogApi rawApi)
{
    public Task<JsonElement> GetAlbumDetailAsync(string albumIds)
    {
        return rawApi.GetLongAudioAlbumDetailAsync(albumIds);
    }

    public Task<JsonElement> GetAlbumAudiosAsync(string albumId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetLongAudioAlbumAudiosAsync(albumId, page, pageSize);
    }

    public Task<JsonElement> GetDailyRecommendAsync(int page = 1, int pageSize = 30)
    {
        return rawApi.GetLongAudioDailyRecommendAsync(page, pageSize);
    }

    public Task<JsonElement> GetRankRecommendAsync()
    {
        return rawApi.GetLongAudioRankRecommendAsync();
    }

    public Task<JsonElement> GetVipRecommendAsync()
    {
        return rawApi.GetLongAudioVipRecommendAsync();
    }

    public Task<JsonElement> GetWeekRecommendAsync()
    {
        return rawApi.GetLongAudioWeekRecommendAsync();
    }
}
