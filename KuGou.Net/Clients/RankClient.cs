using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class RankClient(RawRankApi rawApi)
{
    /// <summary>
    ///     获取所有排行榜列表 (飙升榜、Top500、分类榜等)
    /// </summary>
    /// <param name="withSong">是否返回榜单下的前几首歌曲预览 (1:返回, 0:不返回)</param>
    public async Task<RankListResponse?> GetAllRanksAsync(int withSong = 1)
    {
        var json = await rawApi.GetRankListAsync(withSong);
        return KgApiResponseParser.Parse<RankListResponse>(json, AppJsonContext.Default.RankListResponse);
    }

    /// <summary>
    ///     获取推荐榜单
    /// </summary>
    public async Task<JsonElement> GetRecommendedRanksAsync()
    {
        return await rawApi.GetRankTopAsync();
    }

    /// <summary>
    ///     获取某个榜单的具体歌曲列表
    /// </summary>
    /// <param name="rankId">榜单 ID</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="rankCid">榜单 CID (可选，部分往期榜单需要)</param>
    public async Task<RankSongResponse?> GetRankSongsAsync(int rankId, int page = 1, int pageSize = 30,
        int? rankCid = null)
    {
        var json = await rawApi.GetRankAudioAsync(rankId, rankCid, page, pageSize);
        var data = KgApiResponseParser.Parse<RankSongResponse>(json, AppJsonContext.Default.RankSongResponse);
        return data;
    }

    /// <summary>
    ///     获取排行榜的往期历史 (Vol)
    /// </summary>
    /// <param name="rankId">榜单 ID</param>
    public async Task<JsonElement> GetRankHistoryAsync(int rankId)
    {
        return await rawApi.GetRankVolAsync(rankId);
    }
}