using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using KgWebApi.Net.Extensions;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("user")]
public class UserController(UserClient userClient) : ControllerBase
{
    /// <summary>
    ///     获取当前登录用户详情。
    /// </summary>
    /// <returns>用户详情。</returns>
    [HttpGet("detail")]
    [ProducesResponseType(typeof(UserDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UserDetail()
    {
        var result = await userClient.GetUserInfoAsync();
        return this.FromKgStatus(result);
    }

    /// <summary>
    ///     获取当前登录用户 VIP 信息。
    /// </summary>
    /// <returns>用户 VIP 信息。</returns>
    [HttpGet("vip/detail")]
    [ProducesResponseType(typeof(UserVipResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UserVipDetail()
    {
        var result = await userClient.GetVipInfoAsync();
        return this.FromKgStatus(result);
    }

    /// <summary>
    ///     分页获取当前登录用户歌单。
    /// </summary>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>用户歌单结果。</returns>
    [HttpGet("playlist")]
    [ProducesResponseType(typeof(UserPlaylistResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UserPlaylist(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetPlaylistsAsync(page, pagesize);
        return this.FromKgStatus(result);
    }

    /// <summary>
    ///     获取用户最近听歌历史。
    /// </summary>
    /// <returns>近期听歌历史记录。</returns>
    [HttpGet("history")]
    public async Task<IActionResult> UserHistory()
    {
        var result = await userClient.GetPlayHistoryAsync();
        return Ok(result);
    }

    /// <summary>
    ///     获取用户听歌历史排行。
    /// </summary>
    /// <returns>用户听歌历史排行。</returns>
    [HttpGet("listen")]
    public async Task<IActionResult> UserListen()
    {
        var result = await userClient.GetListenRankAsync();
        return Ok(result);
    }

    /// <summary>
    ///     获取用户关注歌手。
    /// </summary>
    /// <returns>用户关注的歌手或用户列表。</returns>
    [HttpGet("follow")]
    public async Task<IActionResult> UserFollow()
    {
        var result = await userClient.GetFollowedSingersAsync();
        return Ok(result);
    }

    /// <summary>
    ///     获取用户云盘。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>用户云盘音乐列表。</returns>
    [HttpGet("cloud")]
    public async Task<IActionResult> UserCloud(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetCloudAsync(page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     获取用户云盘音乐 URL。
    /// </summary>
    /// <param name="hash">音乐 hash。</param>
    /// <param name="albumAudioId">专辑音频 id。</param>
    /// <param name="audioId">音频 id。</param>
    /// <param name="name">云盘音乐名称。</param>
    /// <returns>用户云盘音乐 URL。</returns>
    [HttpGet("cloud/url")]
    public async Task<IActionResult> UserCloudUrl(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "audio_id")] string? audioId = null,
        [FromQuery] string? name = null)
    {
        var result = await userClient.GetCloudUrlAsync(hash, albumAudioId, audioId, name);
        return Ok(result);
    }

    /// <summary>
    ///     获取关注歌手消息。
    /// </summary>
    /// <param name="artistId">需要获取歌手或用户消息的 userid。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>关注歌手或用户消息。</returns>
    [HttpGet("follow/message")]
    public async Task<IActionResult> UserFollowMessage(
        [FromQuery(Name = "id")][Required(AllowEmptyStrings = false)] string artistId,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetFollowMessagesAsync(artistId, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     获取用户收藏的视频。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>用户收藏的视频列表。</returns>
    [HttpGet("video/collect")]
    public async Task<IActionResult> UserVideoCollect(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetCollectedVideosAsync(page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     获取用户喜欢的视频。
    /// </summary>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>用户喜欢的视频列表。</returns>
    [HttpGet("video/love")]
    public async Task<IActionResult> UserVideoLove([FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetLikedVideosAsync(pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     歌曲收藏数。
    /// </summary>
    /// <param name="mixsongids">音乐 mixsongid，多个以逗号分隔。</param>
    /// <returns>歌曲收藏数。</returns>
    [HttpGet("/favorite/count")]
    public async Task<IActionResult> FavoriteCount([FromQuery][Required(AllowEmptyStrings = false)] string mixsongids)
    {
        var result = await userClient.GetFavoriteCountAsync(mixsongids);
        return Ok(result);
    }

    /// <summary>
    ///     获取服务器时间。
    /// </summary>
    /// <returns>服务器时间。</returns>
    [HttpPost("/server/now")]
    public async Task<IActionResult> ServerNow()
    {
        var result = await userClient.GetServerNowAsync();
        return Ok(result);
    }
}
