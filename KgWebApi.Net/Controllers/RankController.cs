using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")]
public class RankController(RankClient rankClient) : ControllerBase
{
    /// <summary>
    ///     获取所有榜单
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetRankList()
    {
        var result = await rankClient.GetAllRanksAsync();
        return Ok(result);
    }

    /// <summary>
    ///     获取榜单歌曲
    /// </summary>
    [HttpGet("songs")]
    public async Task<IActionResult> GetRankSongs(
        [FromQuery] int rankid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await rankClient.GetRankSongsAsync(rankid, page, pagesize);
        return Ok(result);
    }
}