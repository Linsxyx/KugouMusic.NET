using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class RecommendClient(RawDiscoveryApi rawApi, KgSessionManager sessionManager)
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

    public async Task<JsonElement> GetAiRecommendAsync(string? albumAudioIds = null)
    {
        var session = sessionManager.Session;
        return await rawApi.GetAiRecommendAsync(GetUserId(), session.Mid, albumAudioIds);
    }

    public async Task<JsonElement> GetYuekuAsync()
    {
        return await rawApi.GetYuekuAsync();
    }

    public async Task<JsonElement> GetYuekuBannerAsync()
    {
        return await rawApi.GetYuekuBannerAsync(GetUserId());
    }

    public async Task<JsonElement> GetYuekuFmAsync()
    {
        return await rawApi.GetYuekuFmAsync();
    }

    public async Task<JsonElement> GetTopAlbumsAsync(int page = 1, int pageSize = 30)
    {
        return await rawApi.GetTopAlbumsAsync(sessionManager.Session.Token, page, pageSize);
    }

    public async Task<JsonElement> GetTopCardAsync(int cardId = 1)
    {
        return await rawApi.GetTopCardAsync(GetUserId(), sessionManager.Session.Mid, cardId);
    }

    public async Task<JsonElement> GetTopCardYouthAsync(int cardId = 3005, int pageSize = 30, string? tagId = null)
    {
        return await rawApi.GetTopCardYouthAsync(cardId, pageSize, tagId);
    }

    public async Task<JsonElement> GetTopIpAsync()
    {
        return await rawApi.GetTopIpAsync();
    }

    public async Task<JsonElement> GetPcDiantaiAsync()
    {
        return await rawApi.GetPcDiantaiAsync(GetUserId());
    }

    public Task<JsonElement> GetBrushAsync(int songPoolId = 0, string mode = "normal")
    {
        var session = sessionManager.Session;
        return rawApi.GetBrushAsync(GetUserId(), session.VipType, session.Mid, songPoolId, mode);
    }

    public Task<JsonElement> GetEverydayFriendAsync()
    {
        return rawApi.GetEverydayFriendAsync();
    }

    public Task<JsonElement> GetEverydayHistoryAsync(string mode = "list", string platform = "ios",
        string? historyName = null, string? date = null)
    {
        return rawApi.GetEverydayHistoryAsync(mode, platform, historyName, date);
    }
    
    /// <summary>
    /// 获取私人推荐音乐 (私人电台) / 听歌行为上报
    /// </summary>
    /// <param name="hash">音乐 hash (建议)</param>
    /// <param name="songid">音乐 songid (建议)</param>
    /// <param name="playtime">已播放时间秒数 (建议)</param>
    /// <param name="action">行为：'play'(正常), 'garbage'(不喜欢/垃圾桶)</param>
    /// <param name="mode">模式：'normal'(红心 Radio), 'small'(小众 Radio), 'peak'(巅峰 Radio)</param>
    /// <param name="songPoolId">推荐策略：0(根据口味), 1(根据风格), 2(特殊推荐池)</param>
    /// <param name="isOverplay">是否已播放完成</param>
    /// <param name="remainSongCount">剩余未播歌曲数，大于 4 不返回推荐歌曲</param>
    public async Task<PersonalFmResponse?> GetPersonalRecommendFMAsync(
        string? hash = null, 
        string? songid = null, 
        int? playtime = null,
        string action = "play",
        string mode = "normal",
        int songPoolId = 0,
        bool isOverplay = false,
        int remainSongCount = 0)
    {
        var session = sessionManager.Session;

        var json = await rawApi.GetPersonalRecommendAsync(
            session.UserId, 
            session.Token, 
            session.VipType, 
            session.Mid, 
            hash, songid, playtime, action, songPoolId, remainSongCount, isOverplay, mode
        );
        
        return KgApiResponseParser.Parse<PersonalFmResponse>(
            json, 
            AppJsonContext.Default.PersonalFmResponse
        );
    }
}
