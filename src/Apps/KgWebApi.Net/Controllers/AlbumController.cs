using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("album")]
public class AlbumController(AlbumClient albumClient) : ControllerBase
{
    /// <summary>
    ///     新碟上架。
    /// </summary>
    /// <returns>新碟上架列表。</returns>
    [HttpGet("shop")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetAlbumShop()
    {
        return Ok(await albumClient.GetAlbumShopAsync());
    }

    /// <summary>
    ///     专辑信息。
    /// </summary>
    /// <param name="albumId">专辑 id，可以传多个。</param>
    /// <param name="fields">需要返回的信息字段。</param>
    /// <returns>专辑相关信息。</returns>
    [HttpGet]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetAlbum([FromQuery(Name = "album_id")][Required(AllowEmptyStrings = false)] string albumId,
        [FromQuery] string? fields = null)
    {
        return Ok(await albumClient.GetAlbumRawAsync(albumId, fields));
    }

    /// <summary>
    ///     专辑详情。
    /// </summary>
    /// <param name="id">专辑 id。</param>
    /// <returns>专辑详情。</returns>
    [HttpGet("detail")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetDetail([FromQuery(Name = "id")][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await albumClient.GetDetailRawAsync(id));
    }

    /// <summary>
    ///     获取专辑歌曲列表。
    /// </summary>
    /// <param name="id">专辑 ID。用户歌单里的MusiclibId就是专辑id</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>专辑歌曲列表。</returns>
    [HttpGet("songs")]
    [KgPublicResponseCache]
    [ProducesResponseType(typeof(List<AlbumSongItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSongs(
        [FromQuery(Name = "id")][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await albumClient.GetSongsAsync(id, page, pagesize));
    }
}
