using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("youth")]
public class YouthController(UserClient userService) : ControllerBase
{
    [HttpGet("channel/all")]
    public async Task<IActionResult> GetChannelAll(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userService.GetYouthChannelAllAsync(page, pagesize);
        return Ok(result);
    }

    [HttpGet("channel/amway")]
    public async Task<IActionResult> GetChannelAmway([FromQuery(Name = "global_collection_id")] string globalCollectionId)
    {
        var result = await userService.GetYouthChannelAmwayAsync(globalCollectionId);
        return Ok(result);
    }

    [HttpPost("channel/detail")]
    public async Task<IActionResult> GetChannelDetail([FromQuery(Name = "global_collection_id")] string globalCollectionIds)
    {
        var result = await userService.GetYouthChannelDetailAsync(globalCollectionIds);
        return Ok(result);
    }

    [HttpPost("channel/similar")]
    public async Task<IActionResult> GetChannelSimilar([FromQuery(Name = "channel_id")] string channelId)
    {
        var result = await userService.GetYouthChannelSimilarAsync(channelId);
        return Ok(result);
    }

    [HttpGet("channel/song")]
    public async Task<IActionResult> GetChannelSong(
        [FromQuery(Name = "global_collection_id")] string globalCollectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userService.GetYouthChannelSongsAsync(globalCollectionId, page, pagesize);
        return Ok(result);
    }

    [HttpGet("channel/song/detail")]
    public async Task<IActionResult> GetChannelSongDetail(
        [FromQuery(Name = "global_collection_id")] string globalCollectionId,
        [FromQuery] string fileid)
    {
        var result = await userService.GetYouthChannelSongDetailAsync(globalCollectionId, fileid);
        return Ok(result);
    }

    [HttpPost("channel/sub")]
    public async Task<IActionResult> SetChannelSubscription(
        [FromQuery(Name = "global_collection_id")] string globalCollectionId,
        [FromQuery] int t = 1)
    {
        var result = await userService.SetYouthChannelSubscriptionAsync(globalCollectionId, t != 0);
        return Ok(result);
    }

    [HttpGet("dynamic")]
    public async Task<IActionResult> GetDynamic()
    {
        var result = await userService.GetYouthDynamicAsync();
        return Ok(result);
    }

    [HttpGet("dynamic/recent")]
    public async Task<IActionResult> GetRecentDynamic()
    {
        var result = await userService.GetYouthRecentDynamicAsync();
        return Ok(result);
    }

    [HttpPost("listen/song")]
    public async Task<IActionResult> ReportListenSong([FromQuery] long mixsongid = 666075191)
    {
        var result = await userService.ReportYouthListenSongAsync(mixsongid);
        return Ok(result);
    }

    [HttpGet("union/vip")]
    public async Task<IActionResult> GetUnionVip()
    {
        var result = await userService.GetYouthUnionVipAsync();
        return Ok(result);
    }

    [HttpGet("user/song")]
    public async Task<IActionResult> GetUserSong(
        [FromQuery] string? userid = null,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] int type = 0)
    {
        var result = await userService.GetYouthUserSongsAsync(userid, page, pagesize, type);
        return Ok(result);
    }

    [HttpPost("vip")]
    public async Task<IActionResult> ReportVipAdPlay()
    {
        var result = await userService.ReportYouthVipAdPlayAsync();
        return Ok(result);
    }

    [HttpGet("day/vip")]
    public async Task<IActionResult> OneDayVip()
    {
        var result = await userService.ReceiveOneDayVipAsync();
        return Ok(result);
    }

    [HttpGet("day/vip/upgrade")]
    public async Task<IActionResult> UpgradeVip()
    {
        var result = await userService.UpgradeVipRewardAsync();
        return Ok(result);
    }

    [HttpGet("month/vip/record")]
    public async Task<IActionResult> GetVipRecordAsync()
    {
        var result = await userService.GetVipRecordAsync();
        return Ok(result);
    }
}
