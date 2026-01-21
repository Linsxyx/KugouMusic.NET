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

    private bool IsLoggedIn()
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
    ///     获取用户歌单
    /// </summary>
    public async Task<List<UserPlaylistItem>> GetPlaylistsAsync(int page = 1, int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        var (uid, token) = GetAuth();
        var jsonElement = await rawApi.GetAllListAsync(uid, token, page, pageSize);
        var data = KgApiResponseParser.Parse<UserPlaylistResponse>(jsonElement,
            AppJsonContext.Default.UserPlaylistResponse);

        return data?.Playlists ?? new List<UserPlaylistItem>();
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
    ///     获取听歌排行 (type=0 通常是全部时间)
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
}