using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("fm")]
public class FmController(FmClient fmClient) : ControllerBase
{
    [HttpGet("recommend")]
    public async Task<IActionResult> GetRecommend()
    {
        return Ok(await fmClient.GetRecommendAsync());
    }

    [HttpGet("songs")]
    public async Task<IActionResult> GetSongs(
        [FromQuery] string fmid,
        [FromQuery] int type = 2,
        [FromQuery] int offset = -1,
        [FromQuery] int size = 20)
    {
        return Ok(await fmClient.GetSongsAsync(fmid, type, offset, size));
    }

    [HttpGet("class")]
    public async Task<IActionResult> GetClassSong()
    {
        return Ok(await fmClient.GetClassSongAsync());
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetImages([FromQuery] string fmid)
    {
        return Ok(await fmClient.GetImagesAsync(fmid));
    }
}
