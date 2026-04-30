using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("")]
public class DiscoveryController(RecommendClient recommendClient) : ControllerBase
{
    /// <summary>
    ///     歌单推荐
    /// </summary>
    [HttpGet("top/playlist")]
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


    [HttpGet("recommend/songs")]
    public async Task<IActionResult> GetRecommendSong()
    {
        var res = await recommendClient.GetRecommendedSongsAsync();
        return Ok(res);
    }


    [HttpGet("everyday/style/recommend")]
    public async Task<IActionResult> GetRecommendStyleSong()
    {
        var res = await recommendClient.GetRecommendedStyleSongsAsync();
        return Ok(res);
    }

    [HttpGet("ai/recommend")]
    public async Task<IActionResult> GetAiRecommend([FromQuery(Name = "album_audio_id")] string? albumAudioIds = null)
    {
        var res = await recommendClient.GetAiRecommendAsync(albumAudioIds);
        return Ok(res);
    }

    [HttpGet("yueku")]
    public async Task<IActionResult> GetYueku()
    {
        var res = await recommendClient.GetYuekuAsync();
        return Ok(res);
    }

    [HttpGet("yueku/banner")]
    public async Task<IActionResult> GetYuekuBanner()
    {
        var res = await recommendClient.GetYuekuBannerAsync();
        return Ok(res);
    }

    [HttpGet("yueku/fm")]
    public async Task<IActionResult> GetYuekuFm()
    {
        var res = await recommendClient.GetYuekuFmAsync();
        return Ok(res);
    }

    [HttpGet("top/album")]
    public async Task<IActionResult> GetTopAlbum(
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var res = await recommendClient.GetTopAlbumsAsync(page, pagesize);
        return Ok(res);
    }

    [HttpGet("top/card")]
    public async Task<IActionResult> GetTopCard([FromQuery(Name = "card_id")] int cardId = 1)
    {
        var res = await recommendClient.GetTopCardAsync(cardId);
        return Ok(res);
    }

    [HttpGet("top/card/youth")]
    public async Task<IActionResult> GetTopCardYouth(
        [FromQuery(Name = "card_id")] int cardId = 3005,
        [FromQuery] int pagesize = 30,
        [FromQuery(Name = "tagid")] string? tagId = null)
    {
        var res = await recommendClient.GetTopCardYouthAsync(cardId, pagesize, tagId);
        return Ok(res);
    }

    [HttpGet("top/ip")]
    public async Task<IActionResult> GetTopIp()
    {
        var res = await recommendClient.GetTopIpAsync();
        return Ok(res);
    }

    [HttpGet("pc/diantai")]
    public async Task<IActionResult> GetPcDiantai()
    {
        var res = await recommendClient.GetPcDiantaiAsync();
        return Ok(res);
    }
    
    /// <summary>
    /// 获取私人FM推荐 / 上报电台播放行为
    /// </summary>
    /// <param name="hash">音乐 hash</param>
    /// <param name="songid">音乐 songid</param>
    /// <param name="playtime">已播放时间 (秒)</param>
    /// <param name="action">行为: play (获取推荐), garbage (标记为不喜欢)</param>
    /// <param name="mode">模式: normal (发现), small (小众), peak (30s 高潮)</param>
    /// <param name="songPoolId">AI 策略池: 0 (口味Alpha), 1 (风格Beta), 2 (Gamma)</param>
    /// <param name="isOverplay">该歌曲是否自然播放到结束</param>
    /// <param name="remainSongCnt">列表剩余歌曲数 (如果填5，只会告诉服务器你的行为，不会返回新歌节省带宽)</param>
    [HttpGet("personal/fm")]
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
