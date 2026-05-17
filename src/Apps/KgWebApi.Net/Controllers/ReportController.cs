using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
public class ReportController(ReportClient reportClient) : ControllerBase
{
    /// <summary>
    ///     提交听歌历史。(没什么用，历史记录存本地比较好)
    /// </summary>
    /// <param name="mixSongId">专辑音乐 id (album_audio_id/MixSongID 均可以)。</param>
    /// <param name="timestamp">当前时间戳。</param>
    /// <param name="playCount">当前播放次数。</param>
    /// <returns>提交听歌历史结果。</returns>
    [HttpPost("playhistory/upload")]
    public async Task<IActionResult> PlayHistoryUpload(
        [FromQuery(Name = "mxid")] long mixSongId,
        [FromQuery(Name = "time")] long? timestamp = null,
        [FromQuery(Name = "pc")] int playCount = 1)
    {
        var result = await reportClient.UploadPlayHistoryAsync(mixSongId, timestamp, playCount);
        return Ok(result);
    }

    /// <summary>
    ///     获取继续播放信息（对应手机版首页显示继续播放入口）。
    /// </summary>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>继续播放信息。</returns>
    [HttpPost("lastest/songs/listen")]
    public async Task<IActionResult> LatestSongsListen([FromQuery] int pagesize = 30)
    {
        var result = await reportClient.GetLatestSongsAsync(pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     累加听歌时长。
    /// </summary>
    /// <returns>听歌时长累加结果。</returns>
    [HttpPost("listen/timeadd")]
    public async Task<IActionResult> ListenTimeAdd()
    {
        var result = await reportClient.AddListenTimeAsync();
        return Ok(result);
    }
}
