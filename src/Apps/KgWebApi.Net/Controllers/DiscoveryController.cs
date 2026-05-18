using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("")]
public class DiscoveryController(RecommendClient recommendClient) : ControllerBase
{
    /// <summary>
    ///     歌单推荐
    /// </summary>
    /// <param name="categoryId">分类 ID。0：推荐，11292：HI-RES，其他可以从 /playlist/tags 接口中获取（接口下的 tag_id 为 category_id的值）</param>
    /// <param name="page">页码。</param>
    /// <returns>推荐歌单分页结果。</returns>
    [HttpGet("top/playlist")]
    [ProducesResponseType(typeof(RecommendPlaylistResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendPlaylist(
        [FromQuery(Name = "category_id")] int categoryId = 0,
        [FromQuery] int page = 1)
    {
        var res = await recommendClient.GetRecommendedPlaylistsAsync(categoryId, page);
        return Ok(res);
    }

    /// <summary>
    ///     新歌速递
    /// </summary>
    [HttpGet("top/song")]
    public async Task<IActionResult> GetNewSong(
        [FromQuery] int type = 21608,
        [FromQuery] int page = 1)
    {
        var res = await recommendClient.GetNewSongsAsync(type, page);
        return Ok(res);
    }

    /// <summary>
    ///     获取每日推荐歌曲。
    /// </summary>
    /// <returns>每日推荐歌曲结果。</returns>
    [HttpGet("recommend/songs")]
    [ProducesResponseType(typeof(DailyRecommendResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendSong()
    {
        var res = await recommendClient.GetRecommendedSongsAsync();
        return Ok(res);
    }

    /*/// <summary>
    ///     风格推荐
    /// </summary>
    [HttpGet("everyday/style/recommend")]
    public async Task<IActionResult> GetRecommendStyleSong()
    {
        var res = await recommendClient.GetRecommendedStyleSongsAsync();
        return Ok(res);
    }*/

    /// <summary>
    ///     AI 推荐。
    /// </summary>
    /// <param name="albumAudioIds">专辑音乐 id (album_audio_id/MixSongID 均可以)。</param>
    /// <returns>AI 推荐歌曲。</returns>
    [HttpGet("ai/recommend")]
    public async Task<IActionResult> GetAiRecommend([FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioIds)
    {
        var res = await recommendClient.GetAiRecommendAsync(albumAudioIds);
        return Ok(res);
    }

    /// <summary>
    ///     乐库。
    /// </summary>
    /// <returns>手机端乐库数据。</returns>
    [HttpGet("yueku")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetYueku()
    {
        var res = await recommendClient.GetYuekuAsync();
        return Ok(res);
    }

    /// <summary>
    ///     乐库 banner。
    /// </summary>
    /// <returns>乐库轮播图数据。</returns>
    [HttpGet("yueku/banner")]
    public async Task<IActionResult> GetYuekuBanner()
    {
        var res = await recommendClient.GetYuekuBannerAsync();
        return Ok(res);
    }

    /// <summary>
    ///     乐库电台。
    /// </summary>
    /// <returns>乐库电台数据。</returns>
    [HttpGet("yueku/fm")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetYuekuFm()
    {
        var res = await recommendClient.GetYuekuFmAsync();
        return Ok(res);
    }

    /// <summary>
    ///     新碟上架。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>新碟上架列表。</returns>
    [HttpGet("top/album")]
    public async Task<IActionResult> GetTopAlbum(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var res = await recommendClient.GetTopAlbumsAsync(page, pagesize);
        return Ok(res);
    }

    /// <summary>
    ///     歌曲推荐。
    /// </summary>
    /// <param name="cardId">推荐卡片 id。</param>
    /// <returns>歌曲推荐内容。</returns>
    [HttpGet("top/card")]
    public async Task<IActionResult> GetTopCard([FromQuery(Name = "card_id")] int cardId = 1)
    {
        var res = await recommendClient.GetTopCardAsync(cardId);
        return Ok(res);
    }

    /*/// <summary>
    ///     歌曲推荐
    /// </summary>
    /// <param name="cardId">推荐卡片 id。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="tagId">标签 id。</param>
    /// <returns>概念版歌曲推荐内容。</returns>
    [HttpGet("top/card/youth")]
    public async Task<IActionResult> GetTopCardYouth(
        [FromQuery(Name = "card_id")] int cardId = 3005,
        [FromQuery] int pagesize = 30,
        [FromQuery(Name = "tagid")] string? tagId = null)
    {
        var res = await recommendClient.GetTopCardYouthAsync(cardId, pagesize, tagId);
        return Ok(res);
    }*/

    /// <summary>
    ///     编辑精选。
    /// </summary>
    /// <returns>编辑精选数据。</returns>
    [HttpGet("top/ip")]
    [KgPublicResponseCache]
    public async Task<IActionResult> GetTopIp()
    {
        var res = await recommendClient.GetTopIpAsync();
        return Ok(res);
    }

    /// <summary>
    ///     banner。
    /// </summary>
    /// <returns>banner 轮播图数据。</returns>
    [HttpGet("pc/diantai")]
    public async Task<IActionResult> GetPcDiantai()
    {
        var res = await recommendClient.GetPcDiantaiAsync();
        return Ok(res);
    }

    /// <summary>
    ///     刷刷。
    /// </summary>
    /// <param name="songPoolId">AI 策略池。</param>
    /// <param name="mode">获取模式。</param>
    /// <returns>刷刷推荐结果。</returns>
    [HttpGet("brush")]
    public async Task<IActionResult> GetBrush(
        [FromQuery(Name = "song_pool_id")] int songPoolId = 0,
        [FromQuery] string mode = "normal")
    {
        var res = await recommendClient.GetBrushAsync(songPoolId, mode);
        return Ok(res);
    }

    /*/// <summary>
    ///     每日推荐。
    /// </summary>
    /// <returns>每日推荐列表。</returns>
    [HttpPost("everyday/friend")]
    public async Task<IActionResult> GetEverydayFriend()
    {
        var res = await recommendClient.GetEverydayFriendAsync();
        return Ok(res);
    }*/

    /// <summary>
    ///     历史推荐。
    /// </summary>
    /// <param name="mode">历史推荐模式。</param>
    /// <param name="platform">设备类型。</param>
    /// <param name="historyName">历史推荐名称。</param>
    /// <param name="date">日期。</param>
    /// <returns>历史推荐数据。</returns>
    [HttpPost("everyday/history")]
    public async Task<IActionResult> GetEverydayHistory(
        [FromQuery] string mode = "list",
        [FromQuery] string platform = "ios",
        [FromQuery(Name = "history_name")] string? historyName = null,
        [FromQuery] string? date = null)
    {
        var res = await recommendClient.GetEverydayHistoryAsync(mode, platform, historyName, date);
        return Ok(res);
    }
    
    /// <summary>
    /// 获取私人FM推荐 / 上报电台播放行为 (为什么这个接口这么麻烦酷)
    /// </summary>
    /// <param name="hash">音乐 hash</param>
    /// <param name="songid">音乐 songid</param>
    /// <param name="playtime">已播放时间 (秒)</param>
    /// <param name="action">行为: play (获取推荐), garbage (标记为不喜欢)</param>
    /// <param name="mode">模式: normal (红心), small (小众), peak (30s 速览)</param>
    /// <param name="songPoolId">AI 策略池: 0 (口味Alpha), 1 (风格Beta), 2 (Gamma)</param>
    /// <param name="isOverplay">该歌曲是否自然播放到结束</param>
    /// <param name="remainSongCnt">列表剩余歌曲数 (如果填5，只会告诉服务器你的行为不会返回歌曲)</param>
    /// <returns>私人 FM 推荐结果。</returns>
    [HttpGet("personal/fm")]
    [ProducesResponseType(typeof(PersonalFmResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonalFm([FromQuery] string? hash = null,
        [FromQuery] string? songid = null,
        [FromQuery] int? playtime = null,
        [FromQuery] string action = "play",
        [FromQuery] string mode = "normal",
        [FromQuery] int songPoolId = 0,
        [FromQuery] bool isOverplay = false,
        [FromQuery] int remainSongCnt = 0)
    {
        var res = await recommendClient.GetPersonalRecommendFMAsync(
            hash, songid, playtime, action, mode, songPoolId, isOverplay, remainSongCnt);
            
        return Ok(res);
    }
}
