using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("fm")]
public class FmController(FmClient fmClient) : ControllerBase
{
    /// <summary>
    ///     电台 - 推荐。
    /// </summary>
    /// <returns>推荐电台列表。</returns>
    [HttpGet("recommend")]
    public async Task<IActionResult> GetRecommend()
    {
        return Ok(await fmClient.GetRecommendAsync());
    }

    /// <summary>
    ///     电台 - 音乐列表。
    /// </summary>
    /// <param name="fmid">fmid，可以传多个。</param>
    /// <param name="type">fmtype。</param>
    /// <param name="offset">fmoffset。</param>
    /// <param name="size">fmsize。</param>
    /// <returns>电台音乐列表。</returns>
    [HttpGet("songs")]
    public async Task<IActionResult> GetSongs(
        [FromQuery][Required(AllowEmptyStrings = false)] string fmid,
        [FromQuery] int type = 2,
        [FromQuery] int offset = -1,
        [FromQuery] int size = 20)
    {
        return Ok(await fmClient.GetSongsAsync(fmid, type, offset, size));
    }

    /// <summary>
    ///     电台。（会返回所有电台数据，返回的json特别大，不建议使用）
    /// </summary>
    /// <returns>所有电台数据。</returns>
    [HttpGet("class")]
    public async Task<IActionResult> GetClassSong()
    {
        return Ok(await fmClient.GetClassSongAsync());
    }

    /// <summary>
    ///     电台 - 图片。
    /// </summary>
    /// <param name="fmid">fmid，可以传多个。</param>
    /// <returns>对应电台的图片。</returns>
    [HttpGet("image")]
    public async Task<IActionResult> GetImages([FromQuery][Required(AllowEmptyStrings = false)] string fmid)
    {
        return Ok(await fmClient.GetImagesAsync(fmid));
    }
}
