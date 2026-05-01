using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("album")]
public class AlbumController(AlbumClient albumClient) : ControllerBase
{
    [HttpGet("shop")]
    public async Task<IActionResult> GetAlbumShop()
    {
        return Ok(await albumClient.GetAlbumShopAsync());
    }

    [HttpGet]
    public async Task<IActionResult> GetAlbum([FromQuery(Name = "album_id")] string albumId,
        [FromQuery] string? fields = null)
    {
        return Ok(await albumClient.GetAlbumRawAsync(albumId, fields));
    }

    [HttpGet("detail")]
    public async Task<IActionResult> GetDetail([FromQuery(Name = "id")] string id)
    {
        return Ok(await albumClient.GetDetailRawAsync(id));
    }

    [HttpGet("songs")]
    public async Task<IActionResult> GetSongs(
        [FromQuery(Name = "id")] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await albumClient.GetSongsAsync(id, page, pagesize));
    }
}
