using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("user")]
public class UserController(UserClient userClient) : ControllerBase
{
    [HttpGet("detail")]
    public async Task<IActionResult> UserDetail()
    {
        var result = await userClient.GetUserInfoAsync();
        return Ok(result);
    }

    [HttpGet("vip/detail")]
    public async Task<IActionResult> UserVipDetail()
    {
        var result = await userClient.GetVipInfoAsync();
        return Ok(result);
    }

    [HttpGet("playlist")]
    public async Task<IActionResult> UserPlaylist()
    {
        var result = await userClient.GetPlaylistsAsync();
        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> UserHistory()
    {
        var result = await userClient.GetPlayHistoryAsync();
        return Ok(result);
    }

    [HttpGet("listen")]
    public async Task<IActionResult> UserListen()
    {
        var result = await userClient.GetListenRankAsync();
        return Ok(result);
    }

    [HttpGet("follow")]
    public async Task<IActionResult> UserFollow()
    {
        var result = await userClient.GetFollowedSingersAsync();
        return Ok(result);
    }

    [HttpGet("cloud")]
    public async Task<IActionResult> UserCloud(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetCloudAsync(page, pagesize);
        return Ok(result);
    }

    [HttpGet("cloud/url")]
    public async Task<IActionResult> UserCloudUrl(
        [FromQuery] string hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "audio_id")] string? audioId = null,
        [FromQuery] string? name = null)
    {
        var result = await userClient.GetCloudUrlAsync(hash, albumAudioId, audioId, name);
        return Ok(result);
    }

    [HttpGet("follow/message")]
    public async Task<IActionResult> UserFollowMessage(
        [FromQuery(Name = "id")] string artistId,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetFollowMessagesAsync(artistId, pagesize);
        return Ok(result);
    }

    [HttpGet("video/collect")]
    public async Task<IActionResult> UserVideoCollect(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetCollectedVideosAsync(page, pagesize);
        return Ok(result);
    }

    [HttpGet("video/love")]
    public async Task<IActionResult> UserVideoLove([FromQuery] int pagesize = 30)
    {
        var result = await userClient.GetLikedVideosAsync(pagesize);
        return Ok(result);
    }
}
