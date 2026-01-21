using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class RankClient(RawRankApi rawApi)
{
    /// <summary>
    ///     获取所有排行榜列表 (飙升榜、Top500、分类榜等)
    ///     <para>对应: /ocean/v6/rank/list</para>
    /// </summary>
    /// <param name="withSong">是否返回榜单下的前几首歌曲预览 (1:返回, 0:不返回)</param>
    public async Task<JsonElement> GetAllRanksAsync(int withSong = 1)
    {
        return await rawApi.GetRankListAsync(withSong);
    }

    /// <summary>
    ///     获取推荐榜单 (通常用于首页展示)
    ///     <para>对应: /mobileservice/api/v5/rank/rec_rank_list</para>
    /// </summary>
    public async Task<JsonElement> GetRecommendedRanksAsync()
    {
        return await rawApi.GetRankTopAsync();
    }

    /// <summary>
    ///     获取某个榜单的具体歌曲列表
    ///     <para>对应: /openapi/kmr/v2/rank/audio</para>
    /// </summary>
    /// <param name="rankId">榜单 ID</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="rankCid">榜单 CID (可选，部分往期榜单需要)</param>
    public async Task<JsonElement> GetRankSongsAsync(int rankId, int page = 1, int pageSize = 30, int? rankCid = null)
    {
        return await rawApi.GetRankAudioAsync(rankId, rankCid, page, pageSize);
    }

    /// <summary>
    ///     获取排行榜的往期历史 (Vol)
    ///     <para>对应: /ocean/v6/rank/vol</para>
    /// </summary>
    /// <param name="rankId">榜单 ID</param>
    public async Task<JsonElement> GetRankHistoryAsync(int rankId)
    {
        return await rawApi.GetRankVolAsync(rankId);
    }
}