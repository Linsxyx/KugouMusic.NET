using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class MusicClient(RawSearchApi rawApi, KgSessionManager sessionManager)
{
    public async Task<List<SongInfo>> SearchAsync(string keyword, int page = 1)
    {
        var json = await rawApi.SearchSongAsync(keyword, page);

        var data = KgApiResponseParser.Parse<SearchResultData>(json, AppJsonContext.Default.SearchResultData);

        if (data?.Songs == null) return new List<SongInfo>();

        return data.Songs;
    }

    public async Task<PlayUrlData?> GetPlayInfoAsync(string hash, string? quality = null)
    {
        var json = await rawApi.GetPlayUrlAsync(hash, quality);

        var result = json.Deserialize(AppJsonContext.Default.PlayUrlData);

        return result ?? new PlayUrlData { Status = 0 };
    }


    public async Task<SearchHotResponse?> GetSearchHotAsync()
    {
        var json = await rawApi.SearchHotAsync();

        var data = KgApiResponseParser.Parse<SearchHotResponse>(
            json,
            AppJsonContext.Default.SearchHotResponse
        );

        return data;
    }


    public async Task<JsonElement> GetSingerSongsAsync(
        string authorId,
        int page = 1,
        int pageSize = 30,
        string sort = "new")
    {
        // 从 Session 获取 DFID
        var dfid = sessionManager.Session.Dfid;

        // 调用 RawApi
        var json = await rawApi.GetSingerSongsAsync(dfid, authorId, page, pageSize, sort);

        return json;
    }
}