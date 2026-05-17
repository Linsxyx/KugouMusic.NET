using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("comment")]
public class CommentController(CommentClient commentClient) : ControllerBase
{
    /// <summary>
    ///     获取歌曲评论。
    /// </summary>
    /// <param name="mixsongid">歌单下 mixsongid。歌手albumaudioid 专辑里没找到有这个字段</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>歌曲评论列表。</returns>
    [HttpGet("music")]
    [ProducesResponseType(typeof(MusicCommentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMusicComments(
        [FromQuery][Required(AllowEmptyStrings = false)] string mixsongid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetMusicCommentsAsync(mixsongid, page, pagesize));
    }

    /// <summary>
    ///     获取歌单评论。
    /// </summary>
    /// <param name="id">歌单 global_collection_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>歌单评论列表。</returns>
    [HttpGet("playlist")]
    [ProducesResponseType(typeof(MusicCommentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlaylistComments(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetPlaylistCommentsAsync(id, page, pagesize));
    }

    /// <summary>
    ///     获取专辑评论。
    /// </summary>
    /// <param name="id">专辑 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>专辑评论列表。</returns>
    [HttpGet("album")]
    [ProducesResponseType(typeof(MusicCommentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlbumComments(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetAlbumCommentsAsync(id, page, pagesize));
    }

    /// <summary>
    ///     获取评论数。(完全没用，用id拿评论也能拿到评论数)
    /// </summary>
    /// <param name="hash">音乐 hash。</param>
    /// <param name="specialId">评论下的 special_child_id 字段。</param>
    /// <returns>评论数字典。</returns>
    [HttpGet("count")]
    [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommentCount(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "special_id")][Required(AllowEmptyStrings = false)] string specialId)
    {
        return Ok(await commentClient.GetCommentCountAsync(hash, specialId));
    }

    [HttpGet("floor")]
    public async Task<IActionResult> GetFloorComments(
        [FromQuery(Name = "special_id")][Required(AllowEmptyStrings = false)] string specialId,
        [FromQuery][Required(AllowEmptyStrings = false)] string tid,
        [FromQuery][Required(AllowEmptyStrings = false)] string mixsongid,
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
        [FromQuery][Required(AllowEmptyStrings = false)] string mixsongid,
        [FromQuery(Name = "type_id")][Required(AllowEmptyStrings = false)] string typeId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] int sort = 1)
    {
        return Ok(await commentClient.GetMusicCommentClassifyAsync(mixsongid, typeId, page, pagesize, sort));
    }

    [HttpGet("music/hotword")]
    public async Task<IActionResult> GetMusicCommentHotword(
        [FromQuery][Required(AllowEmptyStrings = false)] string mixsongid,
        [FromQuery(Name = "hot_word")][Required(AllowEmptyStrings = false)] string hotWord,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await commentClient.GetMusicCommentHotwordAsync(mixsongid, hotWord, page, pagesize));
    }
}
