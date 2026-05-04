using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("artist")]
public class ArtistController(ArtistClient artistClient) : ControllerBase
{
    [HttpPost("follow")]
    public async Task<IActionResult> Follow([FromQuery] string id)
    {
        return Ok(await artistClient.FollowAsync(id));
    }

    [HttpPost("unfollow")]
    public async Task<IActionResult> Unfollow([FromQuery] string id)
    {
        return Ok(await artistClient.UnfollowAsync(id));
    }

    [HttpPost("follow/newsongs")]
    public async Task<IActionResult> GetFollowNewSongs(
        [FromQuery(Name = "last_album_id")] long lastAlbumId = 0,
        [FromQuery] int pagesize = 30,
        [FromQuery(Name = "opt_sort")] int optSort = 1)
    {
        return Ok(await artistClient.GetFollowNewSongsAsync(lastAlbumId, pagesize, optSort));
    }

    [HttpPost("honour")]
    public async Task<IActionResult> GetHonour(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await artistClient.GetHonourAsync(id, page, pagesize));
    }

    [HttpGet("lists")]
    public async Task<IActionResult> GetLists(
        [FromQuery] int musician = 0,
        [FromQuery(Name = "sextypes")] int sexTypes = 0,
        [FromQuery] int type = 0,
        [FromQuery] int hotsize = 30)
    {
        return Ok(await artistClient.GetListsAsync(musician, sexTypes, type, hotsize));
    }

    [HttpGet("/singer/list")]
    public async Task<IActionResult> GetSingerList(
        [FromQuery] int sextype = 0,
        [FromQuery] int type = 0,
        [FromQuery] int hotsize = 200)
    {
        return Ok(await artistClient.GetSingerListAsync(sextype, type, hotsize));
    }

    [HttpGet("videos")]
    public async Task<IActionResult> GetVideos(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string tag = "all")
    {
        return Ok(await artistClient.GetVideosAsync(id, page, pagesize, tag));
    }

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
