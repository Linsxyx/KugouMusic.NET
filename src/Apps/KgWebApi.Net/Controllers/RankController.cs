using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("rank")]
public class RankController(RankClient rankClient) : ControllerBase
{
    /// <summary>
    ///     获取所有榜单
    /// </summary>
    /// <param name="withsong">是否包含榜单歌曲。</param>
    /// <returns>榜单列表。</returns>
    [HttpGet("list")]
    [KgPublicResponseCache]
    [ProducesResponseType(typeof(RankListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRankList([FromQuery] int withsong = 1)
    {
        var result = await rankClient.GetAllRanksAsync(withsong);
        return Ok(result);
    }

    /// <summary>
    ///     排行榜信息。
    /// </summary>
    /// <param name="rankid">排行榜 id。</param>
    /// <param name="rankCid">排行榜 cid。</param>
    /// <param name="albumImg">是否返回专辑图片。</param>
    /// <param name="zone">排行榜 zone。</param>
    /// <returns>排行榜信息。</returns>
    [HttpGet("info")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetRankInfo(
        [FromQuery][BindRequired] int rankid,
        [FromQuery(Name = "rank_cid")] int? rankCid = null,
        [FromQuery(Name = "album_img")] int albumImg = 1,
        [FromQuery] string? zone = null)
    {
        var result = await rankClient.GetRankInfoRawAsync(rankid, rankCid, albumImg, zone);
        return Ok(result);
    }

    /// <summary>
    ///     获取榜单歌曲
    /// </summary>
    /// <param name="rankid">榜单 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>榜单歌曲分页结果。</returns>
    [HttpGet("audio")]
    [KgPublicResponseCache]
    [ProducesResponseType(typeof(RankSongResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRankSongs(
        [FromQuery][BindRequired] int rankid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await rankClient.GetRankSongsAsync(rankid, page, pagesize);
        return Ok(result);
    }
}
