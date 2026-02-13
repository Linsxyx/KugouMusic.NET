using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class DiscoveryClient(RawDiscoveryApi rawApi, KgSessionManager sessionManager)
{
    private string GetUserId()
    {
        return sessionManager.Session.UserId == "0" ? "0" : sessionManager.Session.UserId;
    }

    private string GetDfid()
    {
        return sessionManager.Session.Dfid;
    }

    /// <summary>
    ///     获取推荐歌单
    /// </summary>
    /// <param name="categoryId">分类ID，0=推荐，11292=Hi-Res</param>
    public async Task<JsonElement> GetRecommendedPlaylistsAsync(int categoryId = 0, int page = 1, int pageSize = 30)
    {
        var uid = GetUserId();
        var dfid = GetDfid();
        return await rawApi.GetRecommendedPlaylistsAsync(uid, dfid, categoryId, page, pageSize);
    }

    /// <summary>
    ///     获取新歌速递
    /// </summary>
    /// <param name="type">榜单类型，默认 21608</param>
    public async Task<JsonElement> GetNewSongsAsync(int type = 21608, int page = 1, int pageSize = 30)
    {
        var uid = GetUserId();
        return await rawApi.GetNewSongsAsync(uid, type, page, pageSize);
    }

    /// <summary>
    ///     获取每日推荐歌曲
    /// </summary>
    public async Task<DailyRecommendResponse?> GetRecommendedSongsAsync()
    {
        var uid = GetUserId();
        var json = await rawApi.GetRecommendSongAsync(uid);

        return KgApiResponseParser.Parse<DailyRecommendResponse>(
            json,
            AppJsonContext.Default.DailyRecommendResponse
        );
    }


    public async Task<JsonElement> GetRecommendedStyleSongsAsync()
    {
        return await rawApi.GetRecommendStyleSongAsync();
    }
}