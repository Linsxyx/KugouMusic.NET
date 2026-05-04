using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class UserClient(RawUserApi rawApi, KgSessionManager sessionManager)
{
    private (string UserId, string Token) GetAuth()
    {
        var s = sessionManager.Session;
        return (s.UserId, s.Token);
    }

    public bool IsLoggedIn()
    {
        var s = sessionManager.Session;
        return !string.IsNullOrEmpty(s.Token) && s.UserId != "0";
    }

    /// <summary>
    ///     获取用户详细信息
    /// </summary>
    public async Task<UserDetailModel?> GetUserInfoAsync()
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        var json = await rawApi.GetUserDetailAsync(uid, token);
        return KgApiResponseParser.Parse<UserDetailModel>(json, AppJsonContext.Default.UserDetailModel);
    }

    /// <summary>
    ///     获取用户 VIP 状态
    /// </summary>
    public async Task<UserVipResponse?> GetVipInfoAsync()
    {
        var json = await rawApi.GetUserVipDetailAsync();
        return KgApiResponseParser.Parse<UserVipResponse>(
            json,
            AppJsonContext.Default.UserVipResponse
        );
    }

    /// <summary>
    ///     获取当月已领取 VIP 天数
    /// </summary>
    public async Task<VipReceiveHistoryResponse?> GetVipRecordAsync()
    {
        var json = await rawApi.GetVipRecordAsync();
        return KgApiResponseParser.Parse<VipReceiveHistoryResponse>(
            json,
            AppJsonContext.Default.VipReceiveHistoryResponse);
    }

    /// <summary>
    ///     获取用户歌单
    /// </summary>
    public async Task<UserPlaylistResponse?> GetPlaylistsAsync(int page = 1, int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        var jsonElement = await rawApi.GetAllListAsync(uid, token, page, pageSize);
        var data = KgApiResponseParser.Parse<UserPlaylistResponse>(jsonElement,
            AppJsonContext.Default.UserPlaylistResponse);

        return data;
    }

    /// <summary>
    ///     获取听歌历史
    /// </summary>
    public async Task<JsonElement?> GetPlayHistoryAsync(string? bp = null)
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        return await rawApi.GetPlayHistoryAsync(uid, token, bp);
    }

    /// <summary>
    ///     获取听歌排行
    /// </summary>
    public async Task<JsonElement?> GetListenRankAsync(int type = 0)
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        return await rawApi.GetListenListAsync(uid, token, type);
    }

    /// <summary>
    ///     获取关注的歌手
    /// </summary>
    public async Task<JsonElement?> GetFollowedSingersAsync()
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        return await rawApi.GetFollowSingerListAsync(uid, token);
    }

    public async Task<JsonElement?> GetCloudAsync(int page = 1, int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        var session = sessionManager.Session;
        var mid = string.IsNullOrWhiteSpace(session.Mid) || session.Mid == "-"
            ? KgUtils.CalcNewMid(session.Dfid)
            : session.Mid;
        return await rawApi.GetCloudAsync(session.UserId, session.Token, mid, page, pageSize);
    }

    public Task<JsonElement> GetCloudUrlAsync(string hash, string? albumAudioId = null, string? audioId = null,
        string? name = null)
    {
        return rawApi.GetCloudUrlAsync(hash, albumAudioId, audioId, name);
    }

    public async Task<JsonElement?> GetFollowMessagesAsync(string artistId, int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.GetFollowMessagesAsync(sessionManager.Session.UserId, artistId, pageSize);
    }

    public async Task<JsonElement?> GetCollectedVideosAsync(int page = 1, int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        return await rawApi.GetCollectedVideosAsync(uid, token, page, pageSize);
    }

    public async Task<JsonElement?> GetLikedVideosAsync(int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.GetLikedVideosAsync(sessionManager.Session.UserId, pageSize);
    }

    // --- VIP 领取相关 ---

    public async Task<OneDayVipModel?> ReceiveOneDayVipAsync()
    {
        if (!IsLoggedIn()) return null;
        var json = await rawApi.GetOneDayVipAsync();
        return KgApiResponseParser.Parse<OneDayVipModel>(json, AppJsonContext.Default.OneDayVipModel);
    }

    public async Task<UpgradeVipModel?> UpgradeVipRewardAsync()
    {
        if (!IsLoggedIn()) return null;
        var (uid, _) = GetAuth();
        var json = await rawApi.UpgradeVipAsync(uid);
        return KgApiResponseParser.Parse<UpgradeVipModel>(json, AppJsonContext.Default.UpgradeVipModel);
    }

    public Task<JsonElement> GetYouthChannelAllAsync(int page = 1, int pageSize = 30)
    {
        return rawApi.GetYouthChannelAllAsync(page, pageSize);
    }

    public Task<JsonElement> GetYouthChannelAmwayAsync(string globalCollectionId)
    {
        return rawApi.GetYouthChannelAmwayAsync(globalCollectionId);
    }

    public Task<JsonElement> GetYouthChannelDetailAsync(string globalCollectionIds)
    {
        return rawApi.GetYouthChannelDetailAsync(globalCollectionIds);
    }

    public Task<JsonElement> GetYouthChannelSimilarAsync(string channelId)
    {
        return rawApi.GetYouthChannelSimilarAsync(channelId, sessionManager.Session.VipType);
    }

    public Task<JsonElement> GetYouthChannelSongsAsync(string globalCollectionId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetYouthChannelSongsAsync(globalCollectionId, page, pageSize);
    }

    public Task<JsonElement> GetYouthChannelSongDetailAsync(string globalCollectionId, string fileId)
    {
        return rawApi.GetYouthChannelSongDetailAsync(globalCollectionId, fileId);
    }

    public async Task<JsonElement?> SetYouthChannelSubscriptionAsync(string globalCollectionId, bool subscribe)
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.SetYouthChannelSubscriptionAsync(globalCollectionId, subscribe);
    }

    public async Task<JsonElement?> GetYouthDynamicAsync()
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.GetYouthDynamicAsync();
    }

    public async Task<JsonElement?> GetYouthRecentDynamicAsync()
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.GetYouthRecentDynamicAsync();
    }

    public async Task<JsonElement?> ReportYouthListenSongAsync(long mixSongId = 666075191)
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.ReportYouthListenSongAsync(mixSongId);
    }

    public Task<JsonElement> GetYouthUnionVipAsync()
    {
        return rawApi.GetYouthUnionVipAsync();
    }

    public Task<JsonElement> GetYouthUserSongsAsync(string? userid = null, int page = 1, int pageSize = 30, int type = 0)
    {
        var targetUserId = string.IsNullOrWhiteSpace(userid) ? sessionManager.Session.UserId : userid;
        return rawApi.GetYouthUserSongsAsync(targetUserId, page, pageSize, type);
    }

    public async Task<JsonElement?> ReportYouthVipAdPlayAsync()
    {
        if (!IsLoggedIn()) return null;
        return await rawApi.ReportYouthVipAdPlayAsync();
    }

    public Task<JsonElement> GetFavoriteCountAsync(string mixSongIds)
    {
        return rawApi.GetFavoriteCountAsync(mixSongIds);
    }

    public Task<JsonElement> GetServerNowAsync()
    {
        var session = sessionManager.Session;
        return rawApi.GetServerNowAsync(session.UserId, session.Token);
    }
}
