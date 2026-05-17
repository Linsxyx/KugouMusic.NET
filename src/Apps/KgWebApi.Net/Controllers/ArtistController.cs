using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("artist")]
public class ArtistController(ArtistClient artistClient) : ControllerBase
{
    /// <summary>
    ///     关注歌手。
    /// </summary>
    /// <param name="id">歌手 id。</param>
    /// <returns>关注歌手结果。</returns>
    [HttpPost("follow")]
    public async Task<IActionResult> Follow([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await artistClient.FollowAsync(id));
    }

    /// <summary>
    ///     取消关注歌手。
    /// </summary>
    /// <param name="id">歌手 id。</param>
    /// <returns>取消关注歌手结果。</returns>
    [HttpPost("unfollow")]
    public async Task<IActionResult> Unfollow([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await artistClient.UnfollowAsync(id));
    }

    /// <summary>
    ///     获取关注歌手新歌。
    /// </summary>
    /// <param name="lastAlbumId">最后专辑 id。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="optSort">排序，1：时间，2：亲密度。</param>
    /// <returns>关注歌手新歌列表。</returns>
    [HttpPost("follow/newsongs")]
    public async Task<IActionResult> GetFollowNewSongs(
        [FromQuery(Name = "last_album_id")] long lastAlbumId = 0,
        [FromQuery] int pagesize = 30,
        [FromQuery(Name = "opt_sort")] int optSort = 1)
    {
        return Ok(await artistClient.GetFollowNewSongsAsync(lastAlbumId, pagesize, optSort));
    }

    /// <summary>
    ///     歌手荣誉。
    /// </summary>
    /// <param name="id">歌手 id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>歌手荣誉信息。</returns>
    [HttpPost("honour")]
    public async Task<IActionResult> GetHonour(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await artistClient.GetHonourAsync(id, page, pagesize));
    }

    /// <summary>
    ///     获取歌手列表。
    /// </summary>
    /// <param name="musician">是否音乐人。</param>
    /// <param name="sexTypes">性别类型。</param>
    /// <param name="type">歌手分类。</param>
    /// <param name="hotsize">返回热门数量。</param>
    /// <returns>歌手列表。</returns>
    [HttpGet("lists")]
    public async Task<IActionResult> GetLists(
        [FromQuery] int musician = 0,
        [FromQuery(Name = "sextypes")] int sexTypes = 0,
        [FromQuery] int type = 0,
        [FromQuery] int hotsize = 30)
    {
        return Ok(await artistClient.GetListsAsync(musician, sexTypes, type, hotsize));
    }

    /// <summary>
    ///     获取歌手列表。
    /// </summary>
    /// <param name="sextype">性别类型。</param>
    /// <param name="type">歌手分类。</param>
    /// <param name="hotsize">返回热门数量。</param>
    /// <returns>歌手列表。</returns>
    [HttpGet("/singer/list")]
    public async Task<IActionResult> GetSingerList(
        [FromQuery] int sextype = 0,
        [FromQuery] int type = 0,
        [FromQuery] int hotsize = 200)
    {
        return Ok(await artistClient.GetSingerListAsync(sextype, type, hotsize));
    }

    /// <summary>
    ///     获取歌手MV。
    /// </summary>
    /// <param name="id">歌手 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <param name="tag">official: 官方版本，live：现场版本，fan：饭制版本，artist: 歌手发布, all: 获取全部，默认为获取全部</param>
    /// <returns>歌手MV。</returns>
    [HttpGet("videos")]
    [ProducesResponseType(typeof(ArtistVideoResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVideos(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string tag = "all")
    {
        return Ok(await artistClient.GetVideosAsync(id, page, pagesize, tag));
    }

    /// <summary>
    ///     获取歌手详情。
    /// </summary>
    /// <param name="id">歌手 ID。</param>
    /// <returns>歌手详情。</returns>
    [HttpGet("detail")]
    [ProducesResponseType(typeof(SingerDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetail([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await artistClient.GetDetailAsync(id));
    }

    /// <summary>
    ///     获取歌手歌曲。
    /// </summary>
    /// <param name="id">歌手 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <param name="sort">排序方式:new/hot</param>
    /// <returns>歌手歌曲分页结果。</returns>
    [HttpGet("audios")]
    [ProducesResponseType(typeof(SingerAudioResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudios(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "new")
    {
        return Ok(await artistClient.GetAudiosAsync(id, page, pagesize, sort));
    }

    /// <summary>
    ///     获取歌手专辑。
    /// </summary>
    /// <param name="id">歌手 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <param name="sort">排序方式:new/hot</param>
    /// <returns>歌手专辑列表。</returns>
    [HttpGet("albums")]
    public async Task<IActionResult> GetAlbums(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "new")
    {
        return Ok(await artistClient.GetAlbumsAsync(id, page, pagesize, sort));
    }
}
