using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")]
public class DiscoveryController(DiscoveryClient discoveryClient) : ControllerBase
{
    /// <summary>
    ///     歌单推荐
    /// </summary>
    [HttpGet("playlist/recommend")]
    public async Task<IActionResult> GetRecommendPlaylist(
        [FromQuery] int category_id = 0,
        [FromQuery] int page = 1)
    {
        var res = await discoveryClient.GetRecommendedPlaylistsAsync(category_id, page);
        return Ok(res);
    }

    /// <summary>
    ///     新歌速递
    /// </summary>
    [HttpGet("newsong")]
    public async Task<IActionResult> GetNewSong(
        [FromQuery] int type = 21608,
        [FromQuery] int page = 1)
    {
        var res = await discoveryClient.GetNewSongsAsync(type, page);
        return Ok(res);
    }


    [HttpGet("RecommendSong")]
    public async Task<IActionResult> GetRecommendSong()
    {
        var res = await discoveryClient.GetRecommendedSongsAsync();
        return Ok(res);
    }


    [HttpGet("RecommendStyleSong")]
    public async Task<IActionResult> GetRecommendStyleSong()
    {
        var res = await discoveryClient.GetRecommendedStyleSongsAsync();
        return Ok(res);
    }
}