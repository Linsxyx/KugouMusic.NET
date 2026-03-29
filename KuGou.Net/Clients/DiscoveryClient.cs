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
    /// <param name="categoryId">tag，0：推荐，11292：HI-RES，其他可以从 playlist/tags 接口中获取（接口下的 tag_id 为 category_id的值）</param>
    /// <param name="page">页数</param>
    /// <param name="pageSize">每页多少首歌</param>
    public async Task<RecommendPlaylistResponse?> GetRecommendedPlaylistsAsync(int categoryId = 0, int page = 1,
        int pageSize = 30)
    {
        var uid = GetUserId();
        var dfid = GetDfid();
        var json = await rawApi.GetRecommendedPlaylistsAsync(uid, dfid, categoryId, page, pageSize);

        return KgApiResponseParser.Parse<RecommendPlaylistResponse>(json,
            AppJsonContext.Default.RecommendPlaylistResponse);
    }

    /// <summary>
    ///     获取新歌速递
    /// </summary>
    /// <param name="type">榜单类型，默认 21608</param>
    /// <param name="page">页数</param>
    /// <param name="pageSize">每页多少首歌</param>
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

    /// <summary>
    ///     获取风格推荐歌曲
    /// </summary>
    public async Task<JsonElement> GetRecommendedStyleSongsAsync()
    {
        return await rawApi.GetRecommendStyleSongAsync();
    }
}