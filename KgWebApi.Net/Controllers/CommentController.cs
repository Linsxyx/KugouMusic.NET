using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("comment")]
public class CommentController(CommentClient commentClient) : ControllerBase
{
    [HttpGet("music")]
    public async Task<IActionResult> GetMusicComments(
        [FromQuery] string mixsongid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetMusicCommentsAsync(mixsongid, page, pagesize));
    }

    [HttpGet("playlist")]
    public async Task<IActionResult> GetPlaylistComments(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetPlaylistCommentsAsync(id, page, pagesize));
    }

    [HttpGet("album")]
    public async Task<IActionResult> GetAlbumComments(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetAlbumCommentsAsync(id, page, pagesize));
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetCommentCount(
        [FromQuery] string? hash = null,
        [FromQuery(Name = "special_id")] string? specialId = null)
    {
        return Ok(await commentClient.GetCommentCountAsync(hash, specialId));
    }

    [HttpGet("floor")]
    public async Task<IActionResult> GetFloorComments(
        [FromQuery(Name = "special_id")] string? specialId,
        [FromQuery] string tid,
        [FromQuery] string? mixsongid = null,
        [FromQuery(Name = "resource_type")] string resourceType = "song",
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] int show_classify = 1,
        [FromQuery] int show_hotword_list = 1,
        [FromQuery] string? code = null)
    {
        return Ok(await commentClient.GetFloorCommentsAsync(
            specialId,
            tid,
            mixsongid,
            resourceType,
            page,
            pagesize,
            show_classify,
            show_hotword_list,
            code));
    }

    [HttpGet("music/classify")]
    public async Task<IActionResult> GetMusicCommentClassify(
        [FromQuery] string mixsongid,
        [FromQuery(Name = "type_id")] string typeId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] int sort = 1)
    {
        return Ok(await commentClient.GetMusicCommentClassifyAsync(mixsongid, typeId, page, pagesize, sort));
    }

    [HttpGet("music/hotword")]
    public async Task<IActionResult> GetMusicCommentHotword(
        [FromQuery] string mixsongid,
        [FromQuery(Name = "hot_word")] string hotWord,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetMusicCommentHotwordAsync(mixsongid, hotWord, page, pagesize));
    }
}
