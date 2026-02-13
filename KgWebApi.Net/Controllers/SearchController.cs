using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

/// <summary>
///     搜索相关API接口 - 使用新的KuGou.Net库
/// </summary>
[ApiController]
[Route("[controller]")]
public class SearchController(MusicClient musicClient, ILogger<SearchController> logger, LyricClient lyricClient)
    : ControllerBase
{
    /// <summary>
    ///     搜索歌曲或专辑等
    ///     GET /search?keywords=海阔天空&page=1
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string keywords = "",
        [FromQuery] int page = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keywords)) return BadRequest(new { error = "关键词不能为空" });

            logger.LogInformation("开始搜索，关键词: {Keywords}, 页码: {Page}", keywords, page);

            var result = await musicClient.SearchAsync(keywords, page);

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("搜索请求超时或取消");
            return StatusCode(504, new { error = "请求超时" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索异常，关键词: {Keywords}", keywords);
            return StatusCode(500, new { error = $"内部服务器错误: {ex.Message}" });
        }
    }

    /// <summary>
    ///     获取歌曲播放URL
    ///     GET /search/playUrl?hash=xxx&quality=128
    /// </summary>
    [HttpGet("playUrl")]
    public async Task<IActionResult> GetPlayUrl(
        [FromQuery] string hash,
        [FromQuery] string quality = "128")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hash))
                return BadRequest(new { error = "歌曲Hash不能为空" });

            logger.LogInformation("获取播放URL，Hash: {Hash}, Quality: {Quality}", hash, quality);

            var result = await musicClient.GetPlayInfoAsync(hash, quality);

            /*if (result == null)
                return NotFound(new { error = "未找到播放信息" });*/

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("获取播放URL请求超时或取消");
            return StatusCode(504, new { error = "请求超时" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取播放URL异常，Hash: {Hash}", hash);
            return StatusCode(500, new { error = $"内部服务器错误: {ex.Message}" });
        }
    }


    [HttpGet("Lyric")]
    public async Task<IActionResult> SearcLyric(
        [FromQuery] string? hash,
        [FromQuery] string? album_audio_id,
        [FromQuery] string? keywords,
        [FromQuery] string? man)
    {
        var result = await lyricClient.SearchLyricAsync(hash, album_audio_id, keywords, man);
        return Ok(result);
    }

    [HttpPost("Lyric")]
    public async Task<IActionResult> GetLyric(
        [FromQuery] string id,
        [FromQuery] string accesskey,
        [FromQuery] string fmt = "krc",
        [FromQuery] bool decode = true)
    {
        // 1. 获取原始数据 (包含解密后的 decodeContent 字符串)
        var result = await lyricClient.GetLyricAsync(id, accesskey, fmt, decode);


        return Ok(result);
    }
    /// <summary>
    ///     获取热搜
    /// </summary>
    [HttpGet("hot")]
    public async Task<IActionResult> GetHot()
    {
        var result = await musicClient.GetSearchHotAsync();
        return Ok(result);
    }


    [HttpGet("singer/songs")]
    public async Task<IActionResult> GetSingerSongs(
        [FromQuery] string authorId,
        [FromQuery] int page = 1,
        [FromQuery] string sort = "new")
    {
        var result = await musicClient.GetSingerSongsAsync(authorId, page, 30, sort);
        return Ok(result);
    }
}