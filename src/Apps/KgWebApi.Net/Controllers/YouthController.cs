using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using KgWebApi.Net.Extensions;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("youth")]
public class YouthController(UserClient userService) : ControllerBase
{
    /// <summary>
    ///     频道 - 获取用户所有频道。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>用户所有订阅的频道。</returns>
    [HttpGet("channel/all")]
    public async Task<IActionResult> GetChannelAll(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userService.GetYouthChannelAllAsync(page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 频道安利。
    /// </summary>
    /// <param name="globalCollectionId">频道 id。</param>
    /// <returns>频道安利信息。</returns>
    [HttpGet("channel/amway")]
    public async Task<IActionResult> GetChannelAmway([FromQuery(Name = "global_collection_id")][Required(AllowEmptyStrings = false)] string globalCollectionId)
    {
        var result = await userService.GetYouthChannelAmwayAsync(globalCollectionId);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 详情。
    /// </summary>
    /// <param name="globalCollectionIds">频道 id。</param>
    /// <returns>频道详情。</returns>
    [HttpPost("channel/detail")]
    public async Task<IActionResult> GetChannelDetail([FromQuery(Name = "global_collection_id")][Required(AllowEmptyStrings = false)] string globalCollectionIds)
    {
        var result = await userService.GetYouthChannelDetailAsync(globalCollectionIds);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 相似频道。
    /// </summary>
    /// <param name="channelId">频道 id。</param>
    /// <returns>相似频道列表。</returns>
    [HttpPost("channel/similar")]
    public async Task<IActionResult> GetChannelSimilar([FromQuery(Name = "channel_id")][Required(AllowEmptyStrings = false)] string channelId)
    {
        var result = await userService.GetYouthChannelSimilarAsync(channelId);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 音乐故事。
    /// </summary>
    /// <param name="globalCollectionId">频道 id。</param>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>音乐故事列表。</returns>
    [HttpGet("channel/song")]
    public async Task<IActionResult> GetChannelSong(
        [FromQuery(Name = "global_collection_id")][Required(AllowEmptyStrings = false)] string globalCollectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await userService.GetYouthChannelSongsAsync(globalCollectionId, page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 音乐故事详情。
    /// </summary>
    /// <param name="globalCollectionId">频道 id。</param>
    /// <param name="fileid">音乐故事 fileid。</param>
    /// <returns>音乐故事详情。</returns>
    [HttpGet("channel/song/detail")]
    public async Task<IActionResult> GetChannelSongDetail(
        [FromQuery(Name = "global_collection_id")][Required(AllowEmptyStrings = false)] string globalCollectionId,
        [FromQuery][Required(AllowEmptyStrings = false)] string fileid)
    {
        var result = await userService.GetYouthChannelSongDetailAsync(globalCollectionId, fileid);
        return Ok(result);
    }

    /// <summary>
    ///     频道 - 订阅。
    /// </summary>
    /// <param name="globalCollectionId">频道 id。</param>
    /// <param name="t">1 为订阅，0 为取消订阅。</param>
    /// <returns>频道订阅结果。</returns>
    [HttpPost("channel/sub")]
    public async Task<IActionResult> SetChannelSubscription(
        [FromQuery(Name = "global_collection_id")][Required(AllowEmptyStrings = false)] string globalCollectionId,
        [FromQuery] int t = 1)
    {
        var result = await userService.SetYouthChannelSubscriptionAsync(globalCollectionId, t != 0);
        return Ok(result);
    }

    /// <summary>
    ///     动态。
    /// </summary>
    /// <returns>动态内容。</returns>
    [HttpGet("dynamic")]
    public async Task<IActionResult> GetDynamic()
    {
        var result = await userService.GetYouthDynamicAsync();
        return Ok(result);
    }

    /// <summary>
    ///     动态 - 最常访问。
    /// </summary>
    /// <returns>经常访问的频道和用户。</returns>
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

    /// <summary>
    ///     获取用户公开的音乐。
    /// </summary>
    /// <param name="userid">用户 id。</param>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="type">公开音乐类型。</param>
    /// <returns>用户公开的音乐列表。</returns>
    [HttpGet("user/song")]
    public async Task<IActionResult> GetUserSong(
        [FromQuery][Required(AllowEmptyStrings = false)] string userid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] int type = 0)
    {
        var result = await userService.GetYouthUserSongsAsync(userid, page, pagesize, type);
        return Ok(result);
    }

    /// <summary>
    ///     领取 VIP。
    /// </summary>
    /// <returns>领取 VIP 上报结果。</returns>
    [HttpPost("vip")]
    public async Task<IActionResult> ReportVipAdPlay()
    {
        var result = await userService.ReportYouthVipAdPlayAsync();
        return Ok(result);
    }

    /// <summary>
    ///     领取当天 VIP，不可多领。
    /// </summary>
    /// <returns>领取结果。</returns>
    [HttpGet("day/vip")]
    [ProducesResponseType(typeof(OneDayVipModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> OneDayVip()
    {
        var result = await userService.ReceiveOneDayVipAsync();
        return this.FromKgStatus(result);
    }

    /// <summary>
    ///     升级到概念版VIP。
    /// </summary>
    /// <returns>升级结果。</returns>
    [HttpGet("day/vip/upgrade")]
    [ProducesResponseType(typeof(UpgradeVipModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpgradeVip()
    {
        var result = await userService.UpgradeVipRewardAsync();
        return this.FromKgStatus(result);
    }

    /// <summary>
    ///     获取VIP 领取记录。
    /// </summary>
    /// <returns>VIP 领取记录。</returns>
    [HttpGet("month/vip/record")]
    [ProducesResponseType(typeof(VipReceiveHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVipRecordAsync()
    {
        var result = await userService.GetVipRecordAsync();
        return this.FromKgStatus(result);
    }
}
