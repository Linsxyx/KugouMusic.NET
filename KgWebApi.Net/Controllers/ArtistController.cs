using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("artist")]
public class ArtistController(ArtistClient artistClient) : ControllerBase
{
    [HttpGet("detail")]
    public async Task<IActionResult> GetDetail([FromQuery] string id)
    {
        return Ok(await artistClient.GetDetailAsync(id));
    }

    [HttpGet("audios")]
    public async Task<IActionResult> GetAudios(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "new")
    {
        return Ok(await artistClient.GetAudiosAsync(id, page, pagesize, sort));
    }

    [HttpGet("albums")]
    public async Task<IActionResult> GetAlbums(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "new")
    {
        return Ok(await artistClient.GetAlbumsAsync(id, page, pagesize, sort));
    }
}
