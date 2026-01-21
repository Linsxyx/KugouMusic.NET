using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController(UserClient userClient) : ControllerBase
{
    [HttpGet("Detail")]
    public async Task<IActionResult> UserDetail()
    {
        var result = await userClient.GetUserInfoAsync();
        return Ok(result);
    }

    [HttpGet("vip/Detail")]
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

    [HttpGet("Liston")]
    public async Task<IActionResult> UserListon()
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
}