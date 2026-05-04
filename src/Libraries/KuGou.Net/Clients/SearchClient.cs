using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class SearchClient(RawSearchApi rawApi, KgSessionManager sessionManager)
{
    public async Task<List<SongInfo>> SearchAsync(string keyword, int page = 1, string type = "song")
    {
        var json = await rawApi.SearchAsync(keyword, page, 30, type);

        var data = KgApiResponseParser.Parse<SearchResultData>(json, AppJsonContext.Default.SearchResultData);

        if (data?.Songs == null) return new List<SongInfo>();

        return data.Songs;
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
    public async Task<List<SearchPlaylistItem>?> SearchSpecialAsync(string keyword, int page = 1,
        string type = "special")
    {
        var json = await rawApi.SearchAsync(keyword, page, 30, type);

        var data = KgApiResponseParser.Parse<SearchPlaylistResponse>(
            json,
            AppJsonContext.Default.SearchPlaylistResponse
        );
        return data?.Playlists;
    }

    public async Task<List<SearchAlbumItem>?> SearchAlbumAsync(string keyword, int page = 1, string type = "album")
    {
        var json = await rawApi.SearchAsync(keyword, page, 30, type);

        var data = KgApiResponseParser.Parse<SearchAlbumResponse>(
            json,
            AppJsonContext.Default.SearchAlbumResponse
        );

        return data?.Albums;
    }

    public Task<JsonElement> SearchRawAsync(string keyword, int page = 1, int pageSize = 30, string type = "song")
    {
        return rawApi.SearchAsync(keyword, page, pageSize, type);
    }

    public Task<JsonElement> SearchDefaultRawAsync()
    {
        var session = sessionManager.Session;
        return rawApi.SearchDefaultAsync(session.UserId, session.VipType);
    }

    public Task<JsonElement> SearchSuggestRawAsync(string keyword, int albumTipCount = 10, int correctTipCount = 10,
        int mvTipCount = 10, int musicTipCount = 10)
    {
        return rawApi.SearchSuggestAsync(keyword, albumTipCount, correctTipCount, mvTipCount, musicTipCount);
    }

    public Task<JsonElement> SearchMixedRawAsync(string keyword)
    {
        return rawApi.SearchMixedAsync(keyword);
    }

    public Task<JsonElement> SearchComplexRawAsync(string keyword, int page = 1, int pageSize = 30)
    {
        return rawApi.SearchComplexAsync(keyword, page, pageSize);
    }
}
